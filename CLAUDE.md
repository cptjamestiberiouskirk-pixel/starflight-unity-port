# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (**6000.3.2f1**) port of the 1986 space RPG *Starflight*. All gameplay C# lives in `Assets/Scripts/` (~225 files). There are **no assembly definitions** (everything compiles into `Assembly-CSharp` / `Assembly-CSharp-Editor`) and **no automated tests**.

The 270 stars / ~811 planets are fixed data from the original game, not random generation — preserve original values when touching game data.

## Commands

The project is normally driven from the Unity Editor. Press Play in **any** of the four build scenes (`Persistent`, `Intro`, `Starport`, `Spaceflight` in `Assets/Scenes/`) — see "Scene bootstrap" below.

Headless compile check (fails if the project is already open in an Editor; a fresh worktree has no `Library/` so the first run does a full import and is slow):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath . -logFile -
```

CI runs the same compile check headless on every PR and push to `master` (`.github/workflows/compile-check.yml`, GameCI EditMode test run; needs `UNITY_EMAIL`, `UNITY_PASSWORD`, and either `UNITY_SERIAL` (Pro/Student) or `UNITY_LICENSE` (Personal `.ulf` contents) as repo secrets).

`com.unity.test-framework` is installed but no test assemblies exist. If tests are added (they will need an `.asmdef` referencing the test framework), run them with `-runTests -testPlatform EditMode -testResults results.xml` and narrow to one test with `-testFilter <FullyQualifiedName>` (omit `-quit`).

Editor tooling is under the **`Starflight Remake/`** menu (`Assets/Tools/Editor/`, `Assets/Planet Generator/Editor/`). Notably `Starflight Remake/Planet Generator` regenerates `Assets/Resources/Planets/{planetId}.bytes` from the images in `Assets/Planet Generator/Data/`.

Root-level asset scripts (not part of the Unity build): `process_ship_debris.py` is a Blender script (`blender --background --python process_ship_debris.py -- --help`); `process_texture_debris.py` uses Pillow (`python process_texture_debris.py --help`).

`com.coplaydev.unity-mcp` is installed, so an MCP bridge to the running Editor (console, compile errors) is available when its server is connected.

## Architecture

### Scene bootstrap and singletons
`Persistent.unity` (build index 0) holds the `DontDestroyOnLoad` managers: `DataController`, `PanelController`, `InputController`, `SoundController`, `MusicController`, `SceneFadeController`, `PopupController` (all in `Scripts/Persistent/`). `DataController.Start()` loads game data and all save slots, then loads `DataController.m_sceneToLoad`.

Each scene controller (`IntroController`, `StarportController`, `SpaceflightController`) checks `DataController.m_instance == null` in `Awake()`; if so it sets `m_sceneToLoad` to itself and loads `Persistent` first. Code in `Start()` can therefore assume the persistent singletons exist. Singletons expose `public static X m_instance` assigned in `Awake()` (`SpaceflightController` assigns it in its constructor).

### Two data layers
- **GameData** (`Scripts/Game Data/`, `GD_*` classes): immutable definitions parsed with `JsonUtility` from `Assets/Resources/Starflight Game Data.json`, then post-processed by `GameData.Initialize()`. Access via `DataController.m_instance.m_gameData`.
- **PlayerData** (`Scripts/Player Data/`, `PD_*` classes): save state. Access via `DataController.m_instance.m_playerData`. Each `PD_*` has a `Reset()` that builds new-game state (and reads GameData, so DataController must exist).

Saves go through `ISaveSystem` → `JsonSaveSystem`, writing `Application.persistentDataPath/{fileName}{slot}.json` (5 slots) with **`JsonUtility`**. Consequences:
- Only Unity-serializable fields persist. Dictionaries, properties, and multi-dimensional arrays are silently dropped (e.g. `PD_General.m_lastCommIds` used to be `int[,]` and lost its data on every load; it's now a flat `int[]` behind `GetLastCommId()` / `SetLastCommId()`).
- `PlayerData.c_currentVersion`: any slot with a different version is **reset to a new game** on load. Bump it only for intentionally breaking `PD_*` changes; otherwise add backward-compat null/length checks for fields missing from older saves.
- Enums are stored as ints — append new values, never reorder.
- Nested containers (arrays or lists of lists) are dropped too; wrap the inner list in a `[Serializable]` class (see `PD_ShipsLog.EntryList`). Game data JSON keys must match field names exactly, since a misspelled key is silently ignored.
- `Radar` sorts `PlayerData.m_encounterList` by distance every frame, so never index it by encounter id; use `PlayerData.FindEncounter()`.

### Location state machine
`PD_General.Location` (`Starport, DockingBay, JustLaunched, StarSystem, Hyperspace, InOrbit, Planetside, Encounter, Disembarked`) drives everything. `DataController.GetCurrentSceneName()` maps `Starport` → Starport scene, everything else → Spaceflight scene.

Inside Spaceflight, `SpaceflightController.SwitchLocation()` hides every location then shows one. Each location is a MonoBehaviour in `Spaceflight/Locations/` with `Show()`/`Hide()` that toggle its GameObject. Changing location auto-saves. Per-frame logic early-outs on `SpaceflightController.m_instance.m_gameIsPaused`.

### Ship console (Spaceflight UI)
- `ShipButton` (`Spaceflight/Buttons/`) is a **plain C# class, not a MonoBehaviour** (`GetLabel()`, `Execute()`, `Update()`). `ButtonController.Awake()` instantiates every button into sets keyed by the `ButtonSet` enum (max 6 per set). To add a button, subclass `ShipButton` and add it to a set there; to add a menu, add a `ButtonSet` value before `Count`. `Execute()` typically calls `SpaceflightController.m_instance.m_buttonController.ChangeButtonSet(...)`.
- Displays (`Spaceflight/Displays/`) subclass `ShipDisplay` and are switched by `DisplayController`.

### Starport panels
`Panel` (`Scripts/Panel/`) is an abstract MonoBehaviour opened/closed via an `Animator` bool `"Open"`; the animation fires `PanelAnimationCallback` → `PanelController.Opened()/Closed()`. `PanelController` allows **one active panel at a time** and calls its `Tick()` each frame. `AstronautController` opens panels when the astronaut enters a door. `SaveGamePanel` lives in Persistent and is opened on Cancel with `SetCallbackObject(this)`.

### Input
`InputController` wraps the **legacy Input Manager** with custom axes (`N`, `NE`, … `NW`, `Submit`, `Cancel`) defined in `ProjectSettings/InputManager.asset` (active input handler is "Both"). Call `InputController.m_instance.Debounce()` after consuming a press. Editor-only debug keys in `SpaceflightController.Update()`: **F9** spawns a hostile Spemin encounter, **F10** destroys the player ship.

### Planet rendering
- **Production path**: `Planet` component → `PlanetGenerator` (`Scripts/Planet Generator/`) loads `Resources/Planets/{id}.bytes` asynchronously, decompresses on `Task.Run`, then runs `PG_*` filters (scale, blur, craters, albedo, specular, normal, water mask) as a step machine; `Process()` returns progress and is pumped from `SpaceflightController.Update()` while the "Commencing System Penetration" popup shows.
- **Experimental path**: `PlanetManager` + Burst `TerrainJob` + `PlanetData` ScriptableObject (`Spaceflight/PlanetGenerator/`), bridged by `ProceduralAdapter` (kill switch `ProceduralAdapter.EnableProceduralGeneration`). Currently wired only into `Test.unity`.

### Coordinates
`Tools` (`Scripts/Misc/Tools.cs`) converts between original game coordinates (0–255 grid) and world space (`(game - 128) * 256`), plus lat/long and map conversions for planet surfaces. `Notes.txt` documents original-game constants (comm subject IDs, stance values, landing sequence, disembark move scale).

### Combat and encounters
`CombatController` (Spaceflight scene singleton) owns weapon cooldowns, damage (shields then armor), and object pools for lasers, missiles, and hit/explosion effects. `Encounter.cs` (~2,000 lines) runs alien AI, comms, stance, and calls into `CombatController`. `SensorsDisplay.ScanType` order matches vessel IDs and **indexes Inspector arrays** (`Encounter.m_alienShipModelTemplate`, debris templates, sensor textures), so never reorder it and bounds-check those lookups.

## Conventions

Match the style of the file you are editing:
- **Legacy code (most files)**: tabs, Allman braces, spaces inside parentheses (`Foo( a, b )`), `m_` prefix for fields, `c_` prefix for constants, a comment above nearly every statement, public fields for Inspector wiring.
- **Newer code** (`PlanetManager`, `ProceduralAdapter`): 4 spaces, `_camelCase` private fields, `[SerializeField] private`.

From `.github/copilot-instructions.md` (project rules):
- Prefer `FindFirstObjectByType<T>()` over `FindObjectOfType<T>()`, `TryGetComponent` when existence is uncertain, `CompareTag()` over `tag ==`.
- No `GetComponent`/`Find`/allocations in `Update`/`FixedUpdate`/`LateUpdate`; cache in `Awake`. Rigidbody logic in `FixedUpdate`.
- Use explicit `!= null` for `UnityEngine.Object` (not `?.`/`??`, which bypass Unity's destroyed-object check). Null-check singletons outside guaranteed flows.
- Defensive guards are mandatory (past bugs came from these): array bounds, divide-by-zero, `maxIteration` limits on `while` loops, empty-string checks before indexing.
- When fixing errors: compiler errors > runtime exceptions > warnings, and don't break the `PD_*` persistence chain.

## Repo notes

- `Max/`, `Illustrator/`, `Photoshop/`, `Research/` (incl. the original manual), `Planets/`, `Spacescape-0.5.1/`, `Music/` at the root are source art and reference material outside `Assets/` — Unity does not import them.
- `.claude/` and `AGENTS.md` are gitignored.
- Commits use conventional-commit messages. Feature changes also update `CHANGELOG.md` (Keep a Changelog format) and `README.md` (see `.github/prompts/commitall.prompt.md`).
- `PROJECT_ANALYSIS_REPORT.md` is a point-in-time report; several bugs it lists were fixed in later commits, so re-verify line numbers before acting on it.
