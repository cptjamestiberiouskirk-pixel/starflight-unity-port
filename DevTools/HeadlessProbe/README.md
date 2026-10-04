# Headless probe for starflight-unity-port

A headless compile check and a play-mode probe for this project. They run Unity in batch mode against the project in the current directory, which can be the main checkout or any git worktree. Each worktree needs its own `Library/`, so the first run in a new one imports everything and takes about four minutes; later runs take 10 to 60 seconds. A GUI Editor that has a different checkout open is not disturbed, but Unity refuses to open a project that another Editor already has open.

Nothing in this folder is part of the game. It sits outside `Assets/`, so Unity neither compiles nor imports it. `probe.ps1` copies `ClaudeProbe.cs` into `Assets/` for one run and deletes it again, and `.gitignore` keeps that copy from ever being committed.

## Requirements

- Windows with PowerShell 7.
- The Unity version named in `ProjectSettings/ProjectVersion.txt`, installed by Unity Hub in its default place (`C:\Program Files\Unity\Hub\Editor\<version>`).
- Python 3 for the two helper scripts.
- Logs go to `%TEMP%\starflight-probe`.

Run everything from the project root.

## Compile check

```powershell
& "DevTools\HeadlessProbe\compile-check.ps1" -Name some-label
```

Prints Unity's exit code, any `error CS`, the `Tundra build` line, the package count, warnings from project code and `git status`. Exit code 0 plus `Tundra build success` means it compiles.

## Play mode probe

```powershell
& "DevTools\HeadlessProbe\probe.ps1" -Scenario h6
```

Copies `ClaudeProbe.cs` into `Assets/`, launches Unity with `-executeMethod ClaudeProbe.Run -probeScenario <name>`, prints the lines the probe logged (`[ClaudeProbe] ...`), then deletes the probe and its `.meta` again. A run takes 20 to 30 seconds. The copy in `Assets/` is ignored by git.

What the probe does:

- Opens `Spaceflight.unity` (or `Starport.unity` for scenarios whose name starts with `starport`) and enters play mode.
- **Never touches the real saves.** Between `DataController.Awake` and `Start` it replaces the private `_saveSystem` with an in-memory one, and it refuses to continue unless `DataController` asked that one about every slot. All five slots are therefore new games.
- Puts the player in hyperspace, from where the game drops into the Arth star system by itself. `EnsureCrew()` fills every crew role, as a launched ship always has.
- Waits until the planets of that star system have been generated before it starts the scenario. The game is paused while they are being generated, so a scenario that started earlier would lose about two seconds of every real-time wait.
- Counts exceptions (with their first stack frame), and un-pauses the Editor every tick, because the console's Error Pause preference pauses play mode on the first logged error even in batch mode.

### Scenarios that exist

| Name | Covers |
|---|---|
| `batch1` | PR 3 fixes on the Spaceflight side: encounter identity (H1), alien comms and comm progress through save and load (H2, PR 2), storage clamp (H4), shields without fuel (M6), missile immunity data (M2), save on quit (M9) |
| `starport` | PR 3 fixes in Starport: training (M4), crew delete and the load repair (H3), trade depot amounts (H4, M3) |
| `h6`, `m15` | combat target and model slots |
| `m13`, `m14` | destroyed encounters and destroyed ships |
| `m7` | sensor scan types |
| `m8` | `JsonSaveSystem` in a scratch directory |
| `m24` | gravity text |
| `starport-m22` | panel close guard |
| `starport-ship` | armor sale (M5) and cargo pod sale (M21) |
| `h5` | alien ship hit points (100 per class, shields first, old saves, save round trip) |
| `m19` | shield charge kept when lowered, slow recharge |
| `h7` | game over reloads the last save, a destroyed ship is never saved, copied or carried through a slot switch (goes through the Intro, Spaceflight and Starport scenes) |
| `m12` | hostile when fired on, until the encounter is left; Uhlek hostile on sight; the Enterprise shoots back (about 60 s) |
| `m20` | repair and treat take time, treat is for the crew on board |
| `h8` | no missile count, every shot uses Endurium, no Endurium no weapons |
| `m10` | a deposit can only be picked up once (orbit, surface, terrain vehicle through the real Disembark button) |
| `m23` | the console is locked during the 35 s landing, and the landing prints each message once and in order (about 55 s) |
| `missiles` | a missile in the air when the player leaves an encounter does not arrive in the next one (alien and player missiles), and missiles still hit inside their own encounter |
| `m11` | the maximum armor and shield points come from the ship: the damage line and the gauges of the status display, the Damage report, the repair of a bare hull, the hull breach warning below a quarter of the maximum |
| `m18` | generating the planet maps does not hold up the main thread: the frames and the positions of the progress bar while a planet is processed in the background. It also logs the long frames that remain and the garbage collections around them |
| `m17` | a planet file that cannot be read aborts that planet and nothing else: eight kinds of bad file made in memory from a real one (garbage, another version, cut off, wrong checksum, one flipped bit, too long, empty), a star system with one such planet, and all 811 planet files of the project through the reader (about 35 s on the code before the fix, which throws every frame for 12 s) |
| `m16` | the maps of a star system's planets are destroyed when the system is left: the textures made at runtime and the planet files loaded after each of four changes of star system, an elevation map, a landing at the end, and what is left after the Spaceflight scene is gone |
| `nomaps` | a planet whose maps could not be generated (planet 90 is given a file that cannot be read): Land refuses it with a message, a planet with maps can still be landed on, and Disembark refuses on the surface of a planet without maps |
| `m25` | the blur of the albedo map: a black map with single white pixels shows how much each neighbour takes, also across the edge where the map wraps around. It also writes the albedo map of planet 90, scattered with a fixed seed, to `%TEMP%\starflight-probe\m25-albedo-planet-90.bin` (three bytes a pixel), so that two runs can be compared pixel by pixel |
| `m26` | the planet generator tool (editor assembly, reached by reflection): it prepares the height map of all 811 planets from their source images, checks that the south pole padding rises to the highest point of the bottom row, and compares every row with the prepared height map in the planet file. Nothing is written, and the tool's settings in the editor preferences are not touched |
| `m27` | the planet generator tool again: a blur with a radius of zero, the tool's check of its settings, a game data file that is not there, how many steps a single rain drop takes with and without evaporation and friction, and the whole erosion pass with neither on a small map. The tool is never started on the real planets |
| `starport-ledger` | what the bank ledger says after the Trade Depot and Ship Configuration panels close with more or less money than they opened with, and the most of an element the player can afford with a very large balance |
| `cargo` | volumes in cubic meters in the ship's cargo list, in the terrain vehicle's pickup message and cargo list; a deposit that does not fit into the hold; the size of a scanned vessel in the sensor analysis |
| `savedata` | the scene a saved game is loaded into for every location, the save panel's description of a game saved in the terrain vehicle, the terrain vehicle of a new game, and the stardate with the computer set to the Thai, Saudi Arabian, Persian and German cultures |
| `combat` | a missile in the air while the game is paused, the size of an explosion that is used again, a launch with every missile of the pool in the air, and a ship that is fired at and hit again while it explodes |
| `encounters` | a ship that has just launched and the hyperspace encounters, the side the aliens appear on in a star system, two encounters that reach the ship in the same frame, and the radar with an encounter dead astern |
| `comms` | the captain's name in what the aliens say, Mechan 9 after five right answers with and without having fired on the Mechans, and a press of the fire button when the aliens ask a question before the press is carried out |
| `terrain` | the crater maps (read once, the same values as the textures), the game's random numbers after rocks are placed, an object on the right edge of the map, a scan while a deposit is being picked up, and scan labels after going back into the ship |
| `savepanel` | the Escape key in space, two seconds into a landing, after the landing and while the ship explodes, and whether a game that is over stays paused |

Run them all with `& "DevTools\HeadlessProbe\run-all-scenarios.ps1" -Tag some-label` (about 9 minutes; one summary block per scenario with the failed checks and exceptions).

### Before and after

A check that cannot fail proves nothing, so run the scenario on the old code as well:

1. Commit the fix (or copy the fixed files aside).
2. Put the old version of the changed files back, for example `git show origin/master:<path> | Set-Content <path> -Encoding utf8NoBOM`.
3. Run the scenario. It should show the bug.
4. Restore the fixed files and run it again.

Use reflection in the probe for anything that only exists on one side, so the same probe compiles against both.

A change in behaviour gets its scenario in the same pull request. Two open pull requests that both add a scenario touch the same two places in `ClaudeProbe.cs` (the scenario switch and the end of the scenario list), so the second one to merge has to rebase.

### Adding a scenario

Add a `case "<name>":` in `Start()` and an `IEnumerator Scenario...()` method. `python add_scenario.py ClaudeProbe.cs <name> <MethodName> <snippet file>` does both: write the method (and any helpers) to a snippet file first.

Helpers already there: `EnterEncounter`, `LeaveEncounter`, `Kill` (through `CombatController.ApplyDamageToAlien`), `Hit` / `HitsToKill`, `FindEncounter( location, minShips, atOnce, skip, race )`, `ForceVessel`, `FirstLivingAlien`, `BringAliensClose`, `DamageTaken`, `FireRepeatedly`, `Stance`, `Endurium`, `Stored` / `Describe` (the in-memory save slots), `WaitForScene`, `AddPerson`, `EnterOrbit( planetId )`, `WaitForLocation`, `PressButton( buttonSet, index )` (through the button controller), `TerrainVehicleCargo`, `SetInput` / `ConsoleFrameWith` (one frame of the button controller with a stick direction or the fire button held), `Console`, `Dump`, `GetField` / `SetField` / `Call` (reflection, including private members), `FindPanel<T>`, `OpenPanel` / `ClosePanel`, `Check`, `MessagesText`, `MessageList`, `Frames`. End with `Finish( "scenario=... key=value ...", 0 )`. Wrap game calls that may throw in `try`/`catch`, since an exception inside the coroutine stops the scenario and it then ends in `RESULT timeout`.

Pitfalls:

- **Write scenario snippets to a file with an editor, not through a shell heredoc.** An apostrophe in a comment breaks the heredoc, and `'\n'` in C# comes out as a real line break after a round trip through a Python string.
- **The message box text is redrawn in `LateUpdate`.** `MessagesText()` right after a button press shows the old text. Use `MessageList()` (the list in the player data) or wait a few frames.
- **Alien ship types are random.** Call `ForceVessel( encounterId, vesselId )` before entering when the test needs armed ships (a transport has no weapons).
- **Real-time checks need tolerance.** Unity caps `Time.deltaTime` on slow frames, and the game is paused whenever a star system is generating its planets (a scenario that changes star system has to wait for `m_starSystem.GeneratingPlanets()` to be false itself). For exact numbers call the update method directly with a time step.
- **A scenario that changes scenes** works: the probe object is `DontDestroyOnLoad`. Wait with `WaitForScene`.
- The watchdog ends a run after 120 s, so keep the real-time waits of one scenario under about 60 s.
- **A failed check is not always the change under test.** Missiles in flight survive the end of an encounter, so one fired in the last encounter can hit the player in the next. The `m12` scenario calls `ClearMissiles()` before it leaves an encounter for that reason. Read the log around a failure before blaming the change.
- **Console input without real input:** `SetInput( "m_south", true )` sets an `InputController` property by reflection, and `ConsoleFrameWith( "m_submit" )` runs one `ButtonController.Update` with it held. `InputController.Update` overwrites the property on the next frame, so set it and call the update in the same step.

`changelog_add.py "<title of an existing bullet>" <file with the new bullet>` inserts a CHANGELOG bullet after an existing one (run it from the project root).

### Facts about the data that decide what is worth testing

- Hyperspace encounters show all their ships at once. The 128 star-system encounters have 6 ships, 3 at a time; those are the ones where a ship's index and its model slot drift apart.
- Only vessels 1 to 4 (Spemin, Mechan) and 20 have a debris model. Encounters 115 and 116 are Spemin, 7 is Elowan.
- Mechans are hostile to a ship with no human crew.
- The Arth system (where a scenario starts) has four planets besides Arth: 90, 91 and 92 are frozen, 94 is a small rock planet. Planet 90 has a mineral density of 43% (762 deposits). Landing and disembarking work headless; the landing animation takes 35 s.
- A new game has 20.0 cubic meters of Endurium (200 tenths), 1,000,000 MU and 250 armor points.

## Limits

- No graphics. Anything visual has to be checked in the GUI Editor.
- It reports what the code does, not what a player sees, and it drives the game through its own methods, not through real input.
