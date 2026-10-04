# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (**6000.3.13f1**) port of the 1986 space RPG *Starflight*. All gameplay C# lives in `Assets/Scripts/`. There are **no assembly definitions** (everything compiles into `Assembly-CSharp` / `Assembly-CSharp-Editor`) and **no automated tests**.

The 270 stars / ~811 planets are fixed data from the original game, not random generation: preserve original values when touching game data.

## Commands

The project is normally driven from the Unity Editor. Press Play in **any** of the four build scenes (`Persistent`, `Intro`, `Starport`, `Spaceflight` in `Assets/Scenes/`): see "Scene bootstrap" below.

Headless compile check (fails if the project is already open in an Editor; a fresh worktree has no `Library/` so the first run does a full import and is slow):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.13f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath . -logFile -
```

CI runs the same compile check headless on every PR and push to `master` (`.github/workflows/compile-check.yml`, GameCI EditMode test run; needs `UNITY_EMAIL`, `UNITY_PASSWORD`, and either `UNITY_SERIAL` (Pro/Student) or `UNITY_LICENSE` (Personal `.ulf` contents) as repo secrets).

**Headless play-mode probe:** `DevTools/HeadlessProbe/` (outside `Assets/`, not part of the build) runs the real Spaceflight or Starport scene headless in play mode, with an in-memory save system so no save file is touched, and checks behaviour through scenarios. From the project root in PowerShell: `& "DevTools\HeadlessProbe\compile-check.ps1"` is the compile check above with a summary, `& "DevTools\HeadlessProbe\probe.ps1" -Scenario <name>` runs one scenario and `& "DevTools\HeadlessProbe\run-all-scenarios.ps1"` runs them all. For a gameplay fix, run its scenario on the old code and on the new code, and add the scenario in the same PR. A scenario that has to pass on the new code must be seen to fail on the old code first. Its `README.md` lists the scenarios, the helpers and the pitfalls. A probe run that shows the "Starport clear" message can leave `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` modified, with no change in content: restore it before staging and stage files by name after a run.

`com.unity.test-framework` is installed but no test assemblies exist. If tests are added (they will need an `.asmdef` referencing the test framework), run them with `-runTests -testPlatform EditMode -testResults results.xml` and narrow to one test with `-testFilter <FullyQualifiedName>` (omit `-quit`).

Editor tooling is under the **`Starflight Remake/`** menu (`Assets/Tools/Editor/`, `Assets/Planet Generator/Editor/`). Notably `Starflight Remake/Planet Generator` regenerates `Assets/Resources/Planets/{planetId}.bytes` from the images in `Assets/Planet Generator/Data/`.

Root-level asset scripts (not part of the Unity build): `process_ship_debris.py` is a Blender script (`blender --background --python process_ship_debris.py -- --help`); `process_texture_debris.py` uses Pillow (`python process_texture_debris.py --help`).

**Editor bridge:** the `com.unity.pipeline` package (0.8.0-exp.1, experimental; that is the package version, not the version of the Unity CLI) is installed, so the Unity CLI can drive an Editor that has this project open. The commands useful here are `unity command console`, `recompile` / `recompile_status`, `editor_play` / `editor_stop`, `eval` (C# against the project's assemblies) and `run_tests`. The server only listens on 127.0.0.1 and does not load while the Editor is in Safe Mode, so a failed connection can mean compile errors. Stop a headless Editor started for a worktree by its PID: `unity command quit` fails outside play mode in this package version.
## Architecture

### Scene bootstrap and singletons
`Persistent.unity` (build index 0) holds the `DontDestroyOnLoad` managers: `DataController`, `PanelController`, `InputController`, `SoundController`, `MusicController`, `SceneFadeController`, `PopupController` (all in `Scripts/Persistent/`). `DataController.Start()` loads game data and all save slots, then loads `DataController.m_sceneToLoad`.

Each scene controller (`IntroController`, `StarportController`, `SpaceflightController`) checks `DataController.m_instance == null` in `Awake()`; if so it sets `m_sceneToLoad` to itself and loads `Persistent` first. Code in `Start()` can therefore assume the persistent singletons exist. Singletons expose `public static X m_instance` assigned in `Awake()` (`SpaceflightController` assigns it in its constructor, and clears it in `OnDestroy` so that the scene does not stay in memory: it is null in the other scenes, and code that can run there has to check it).

### Two data layers
- **GameData** (`Scripts/Game Data/`, `GD_*` classes): immutable definitions parsed with `JsonUtility` from `Assets/Resources/Starflight Game Data.json`, then post-processed by `GameData.Initialize()`. Access via `DataController.m_instance.m_gameData`.
- **PlayerData** (`Scripts/Player Data/`, `PD_*` classes): save state. Access via `DataController.m_instance.m_playerData`. Each `PD_*` has a `Reset()` that builds new-game state (and reads GameData, so DataController must exist).

Saves go through `ISaveSystem` → `JsonSaveSystem`, writing `Application.persistentDataPath/{fileName}{slot}.json` (5 slots) with **`JsonUtility`**. A save is written to `.tmp` and swapped in, the save it replaces is kept as `.bak`, loading falls back to the backup, and a save that can't be read is moved to `.corrupt`. `JsonSaveSystem` takes an optional directory, so it can be exercised away from the real saves. Consequences:
- Only Unity-serializable fields persist. Dictionaries, properties, and multi-dimensional arrays are silently dropped (e.g. `PD_General.m_lastCommIds` used to be `int[,]` and lost its data on every load; it's now a flat `int[]` behind `GetLastCommId()` / `SetLastCommId()`).
- `PlayerData.c_currentVersion`: any slot with a different version is **reset to a new game** on load. Bump it only for intentionally breaking `PD_*` changes; otherwise add backward-compat null/length checks for fields missing from older saves.
- Enums are stored as ints: append new values, never reorder.
- Nested containers (arrays or lists of lists) are dropped too; wrap the inner list in a `[Serializable]` class (see `PD_ShipsLog.EntryList`). Game data JSON keys must match field names exactly, since a misspelled key is silently ignored.
- `Radar` sorts `PlayerData.m_encounterList` by distance every frame, so never index it by encounter id; use `PlayerData.FindEncounter()`.
- A destroyed ship (`m_armorPoints <= 0`) is never saved: `DataController.SavePlayerData` refuses it, and game over reloads the active slot from its last save (`ReloadActiveGame`). Nothing may bring the armor of a destroyed ship back above 0 (see the guard in `PD_PlayerShip.UpdateRepairs`), or the lost game becomes saveable again. The one exception is the repair in `DataController.LoadPlayerData`, which sets the armor of a save written with a destroyed ship to 1 when it is loaded.

### Location state machine
`PD_General.Location` (`Starport, DockingBay, JustLaunched, StarSystem, Hyperspace, InOrbit, Planetside, Encounter, Disembarked`) drives everything. `DataController.GetCurrentSceneName()` maps `Starport` → Starport scene, everything else → Spaceflight scene.

Inside Spaceflight, `SpaceflightController.SwitchLocation()` hides every location then shows one. Each location is a MonoBehaviour in `Spaceflight/Locations/` with `Show()`/`Hide()` that toggle its GameObject. Changing location auto-saves. Per-frame logic early-outs on `SpaceflightController.m_instance.m_gameIsPaused`. `SpaceflightController.Update` also ticks the background work that is kept in the player data: shield recharge, armor repairs and medical treatment (`PD_PlayerShip.UpdateShields` and `UpdateRepairs`, `PD_CrewAssignment.UpdateTreatment`).

`SpaceflightController.UpdateEncounters` moves the alien groups and begins encounters. The star system and hyperspace locations call it; it does nothing for any other location (a ship that has `JustLaunched` has the star system on screen but has not entered it), and it begins at most one encounter per frame, the nearest. Hyperspace and the inside of a star system are separate coordinate spaces whose numbers overlap, so a mix-up gives positions that look possible: take the player's position from `m_lastHyperspaceCoordinates` or `m_lastStarSystemCoordinates` to match the location of the encounter.

In the Spaceflight scene the Cancel input (the Escape key) opens the save panel, except during a launch or landing animation (`PlayerCamera.IsLaunchingOrLanding()`) and once the ship has been destroyed (`CombatController.PlayerIsDestroyed()`). A game that is over stays paused.

### Ship console (Spaceflight UI)
- `ShipButton` (`Spaceflight/Buttons/`) is a **plain C# class, not a MonoBehaviour** (`GetLabel()`, `Execute()`, `Update()`). `ButtonController.Awake()` instantiates every button into sets keyed by the `ButtonSet` enum (max 6 per set). To add a button, subclass `ShipButton` and add it to a set there; to add a menu, add a `ButtonSet` value before `Count`. `Execute()` typically calls `SpaceflightController.m_instance.m_buttonController.ChangeButtonSet(...)`. A button whose `Execute()` returns true becomes the running button: `ButtonController` calls its `Update()` every frame, and returning true from that keeps the stick and the fire button away from the console. A long sequence (launch, landing, disembarking) returns true for as long as it runs and is ended by whatever changes the button set, for the landing and the launch an event of the camera animation (`PlayerCamera.PlayerHasLanded`, `PlayerHasLaunched`). The fire button activates the selected button 0.35 s after the press, and only if the button the press was made on is still the selected one: the buttons can change in between (an alien question puts Yes and No on the console).
- Displays (`Spaceflight/Displays/`) subclass `ShipDisplay` and are switched by `DisplayController`. A display's `Update` builds its text only when a value it shows has changed (see `StatusDisplay`); building it every frame makes garbage every frame.

### Starport panels
`Panel` (`Scripts/Panel/`) is an abstract MonoBehaviour opened/closed via an `Animator` bool `"Open"`; the animation fires `PanelAnimationCallback` → `PanelController.Opened()/Closed()`. `PanelController` allows **one active panel at a time** and calls its `Tick()` each frame. `AstronautController` opens panels when the astronaut enters a door. `SaveGamePanel` lives in Persistent and is opened on Cancel with `SetCallbackObject(this)`. It forgets the callback object once it has called it: the panel outlives the scenes and must not hold on to their controllers.

### Input
`InputController` wraps the **legacy Input Manager** with custom axes (`N`, `NE`, … `NW`, `Submit`, `Cancel`) defined in `ProjectSettings/InputManager.asset` (active input handler is "Both"). Call `InputController.m_instance.Debounce()` after consuming a press. Editor-only debug keys in `SpaceflightController.Update()`: **F9** spawns a hostile Spemin encounter, **F10** destroys the player ship.

### Planet rendering
- **Production path**: `Planet` component → `PlanetGenerator` (`Scripts/Planet Generator/`) loads `Resources/Planets/{id}.bytes` asynchronously, decompresses on `Task.Run`, then runs `PG_*` filters (scale, blur, craters, albedo, specular, normal, water mask) as a step machine; `Process()` returns progress and is pumped from `SpaceflightController.Update()` while the "Commencing System Penetration" popup shows. Three rules it relies on:
  - `Process()` runs on the main thread and never waits for the task: it looks at `IsCompleted` each frame. Only the main thread changes the step and the abort flag; the task only writes the progress and its own buffers.
  - A planet file that cannot be read (damaged, cut off, another version, wrong checksum: `ReadPlanetData` throws) fails the task, and `Process()` turns that into an abort of that one planet with an error in the log. An aborted planet has no maps and no elevation data: check `Planet.HasMaps()` before using its generator (Land and Disembark refuse such a planet). Never let an exception out of `Process()`: `SpaceflightController.Update` calls it first thing every frame, so the game would stay paused for good.
  - The textures a generator makes are never freed by Unity. `Planet` keeps the generator whose maps are on its material and calls `PlanetGenerator.Release()` once the next planet's maps are on it, when the orbit is empty in the new system, and in `OnDestroy`.
- **Objects on the surface**: `TerrainGridPopulator` places a planet's rocks, deposits and trees with `Random` seeded from the planet, so that they are in the same places every time, and puts the game's `Random.state` back afterwards. Anything else that needs repeatable random numbers has to do the same. `PG_Craters.Initialize` reads the crater textures once per session, and with `GetPixel` on purpose: `GetPixels` gives values that differ in the last bit for these 16 bit textures, and every planet is worked out from them.
- **Experimental path**: `PlanetManager` + Burst `TerrainJob` + `PlanetData` ScriptableObject (`Spaceflight/PlanetGenerator/`), bridged by `ProceduralAdapter` (kill switch `ProceduralAdapter.EnableProceduralGeneration`).

### Coordinates
`Tools` (`Scripts/Misc/Tools.cs`) converts between original game coordinates (0–255 grid) and world space (`(game - 128) * 256`), plus lat/long and map conversions for planet surfaces. `Notes.txt` documents original-game constants (comm subject IDs, stance values, landing sequence, disembark move scale).

### Combat and encounters
`CombatController` (Spaceflight scene singleton) owns weapon cooldowns, damage (shields then armor), and object pools for lasers, missiles, and hit/explosion effects. `Encounter.cs` (~2,000 lines) runs alien AI, comms, stance, and calls into `CombatController`. `SensorsDisplay.ScanType` order matches vessel IDs and **indexes Inspector arrays** (`Encounter.m_alienShipModelTemplate`, debris templates, sensor textures), so never reorder it and bounds-check those lookups.

Combat rules:
- Alien ships have saved armor and shield points, 100 per vessel class (`PD_AlienShip`). Shields absorb first.
- Firing on any alien calls `Encounter.PlayerAttacked()`, which keeps that encounter hostile until the player leaves it (`PD_Encounter.m_attackedByPlayer`). The Uhlek are hostile on sight and never talk: the game data has no comm lines for them.
- Alien fire is only reached through the race switch in `Encounter.Update`. A race without its own case gets the `default` case, which only shoots back.
- Player weapons have no ammunition. Every shot uses Endurium through `PD_PlayerShip.UseUpFuel` and needs `HasFuel()`.
- Shields keep their charge when lowered and recharge slowly. Repair and Treat start work that takes time; the buttons do not change armor or vitality themselves.
- The most armor and shield points the player ship can have come from `PD_PlayerShip.GetMaximumArmorPoints()` and `GetMaximumShieldPoints()`: the points of the installed equipment, or the bare hull's 250 with no armor plating (`HasArmorPlating()`). Displays, the engineering buttons, repairs and the hull breach warning all use them; never hard-code a maximum.
- An encounter forgets the combat target when it begins, and takes every missile out of the air when it begins and when it ends (`CombatController.ClearMissiles`). The other pooled effects are left to finish: the explosion of the player ship is what calls the game over screen.
- The player ship is destroyed once. From then on `CombatController.PlayerIsDestroyed()` is true: `ApplyDamageToPlayer` and `AlienFiresAtPlayer` do nothing, and the player's weapons do not fire. An explosion calls its callback after 1.5 s and is switched off after 2.2 s, when its particles are gone; `GetAvailableExplosion` hands it out at full size. A missile stands still while the game is paused. A player launch that finds no free missile in the pool does nothing: no fuel, no `PlayerAttacked()`.
- Five right answers unlock Mechan 9 (`PD_General.m_mechan9Unlocked`, saved), but not in an encounter in which the player has fired on the Mechans.
- What the aliens say goes through `Encounter.AddComm`, which fills in the name of the player's captain (`*`) and of the player's ship (`&`) before the words are garbled. Names are stored as they were typed, so the text can hold empty words (two spaces in a row), and the word loop skips them.

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

- `Max/`, `Illustrator/`, `Photoshop/`, `Research/` (incl. the original manual), `Planets/`, `Spacescape-0.5.1/`, `Music/` at the root are source art and reference material outside `Assets/`: Unity does not import them.
- Under `.claude/` only `settings.json`, `rules/`, `agents/` and `skills/` are tracked; the rest of `.claude/` and `AGENTS.md` are gitignored.
- `CODE_REVIEW_2026-10-03.md` is the current review (open findings, fixes and how they were checked); re-verify line numbers there. `PROJECT_CONTEXT.md` and `PROJECT_ANALYSIS_REPORT.md` are historical.

## Working rules

- Label claims CONFIRMED (seen in code, logs or the console), INFERRED (one link unverified: name it) or HYPOTHESIZED. Say what was not tested. Nothing is fixed until it has compiled; say so when a fix only masks a symptom.
- No em dashes anywhere: code, comments, docs, commit messages, PR titles and bodies.
- One small PR per change, on its own branch from the current `origin/master`. Stage files by name, use conventional-commit messages, add a `CHANGELOG.md` bullet under `## [Unreleased]` for every change and update `README.md` for features (`.github/prompts/commitall.prompt.md` has the steps). Do not implement a design-dependent item before the project owner has made the call.
- Squash-merge (`gh pr merge <n> --squash`, without `--delete-branch`) only once the `compile` check of the PR's head commit is SUCCESS, never on a red or pending check. `master` has no branch protection, so do not use GitHub auto-merge.
- Before pushing a branch that edits `CHANGELOG.md`, trial-merge it against every open PR branch (`git merge-tree --write-tree --name-only HEAD origin/<branch>`); on a conflict, move the new bullet so an unchanged line separates the two insertions.
- Unity package updates: one per branch, compile-checked before the commit, never in bulk. The CI report lists `com.unity.sdk.linux-x86_64`, `com.unity.toolchain.linux-x86_64-linux` and `com.unity.sysroot.base` as added: container noise, never commit them.
