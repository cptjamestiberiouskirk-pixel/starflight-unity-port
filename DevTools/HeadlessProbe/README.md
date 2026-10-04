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
- **Real-time checks need tolerance.** Unity caps `Time.deltaTime` on slow frames and the scene is still generating planets when a scenario starts. For exact numbers call the update method directly with a time step.
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
