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

Unity is stopped if it has not come back after 420 seconds. `-TimeoutSeconds 100` shortens that, for a scenario that is expected to hang on the old code: a hang on the main thread also stops the probe's own 120 second watchdog.

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
| `sensorpictures` | the picture each kind of scan puts in the sensor window: a planet in orbit (its mask), a Spemin scout (control: its own picture), every vessel whose picture was traced by `DevTools/SensorPictures` (its own picture; the list grows with each race), the window clipping a wide picture (the Enterprise) to its magenta panel, an unknown object, which has no picture (window left empty and black inside its magenta border, no readout, the original's message at the end of the scan, also straight after a scan that had one, scan type kept; a scan with a picture and a display just shown keep it magenta), debris that names no vessel (window left empty), the debris of every vessel scanned by its vessel id, and the wreck of a ship destroyed in a real encounter for a Spemin scout, a Mechan scout, a Thrynn scout (its own debris) and a Spemin warship (control: the picture every wreck had before), and the readout once the scan is over for a Spemin scout, a Mechan scout and a Minstrel (mass in tons, bio, energy, as the original shows them) and a Spemin scout wreck, with a planet's minerals as the control (about 22 s of scanning) |
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
| `visual` | a floating object when its timer starts again, when an explosion is switched off, and how far the landing site crosshair moves in a second |
| `perframe` | how much memory a frame of the status display and of the terrain vehicle display takes when nothing changes, with a control that shows the measurement works, and that both still follow what changes |
| `starport-transport` | the docking bay transporter: how much memory an update of the astronaut's opacity takes, that the fade reaches the astronaut, and that the material assets are left alone |
| `shipslog` | a ship's log of forty entries scrolled to its end, then an empty log, then the first one again: that it opens (the review thought it could hang) and where it is scrolled to |
| `leaks` | the materials in memory before, during and after a deposit's transporter effect, and whether the spaceflight controller and the maps of two planets are still in memory after the scene has been left (weak references, after the save panel has been used) |
| `latent` | the message box slide with a duration of one and of two seconds, the experimental planet mesh at a resolution of 110 with no parent (its triangle indices read back, the mesh after its planet is destroyed), and the adapter switched off with no planet controller |
| `editortools` | the textures in memory before and after the planet generator's four save functions, the shader inspector's compression test with a render texture in the normal map slot, and what an empty file name does to Path.GetDirectoryName |
| `commlink` | hostile aliens before, during and after a comm link (Spemin scouts through the real `Connect` and `Disconnect`; the Uhlek with the comm flag set by hand, since they never talk), a player missile and an alien missile that run out of time (an explosion, no damage) next to a missile that arrives, and that `Encounter.LeaveEncounterAfterVictory` is gone (about 65 s) |
| `deposits` | the size of all 762 deposits of planet 90 (1 to 5 cubic meters) with a checksum of where they are, so that two runs can be compared, a pickup through the real button, how many deposits fill the terrain vehicle's hold, the cargo display that is not in any scene, and what the ship's hold takes on the way back |
| `unmapped` | a planet whose maps could not be generated (planet 90 with a file that cannot be read): the four maps on its material before and after, whether the maps from before are destroyed, the messages within orbital range and in orbit, and a game that is loaded in the terrain vehicle on that planet (where it ends up, the exceptions of its first second, its cargo, what is saved), with a planet that has its maps as the control |
| `orbit` | encounters in orbit (encounter 316 at planet 115 of star 30): that it begins when the ship goes into orbit and not at another planet, where its ships come from, where the ship is after flying out of it, that it begins again, a real launch from the planet's surface (the encounter waits for the end of the 30 s animation), and that it is over once its ships are destroyed. Also the way out of a star system and a hyperspace encounter as a control, and how many of the game data's encounters in orbit name an orbit that has a planet (about 50 s) |
| `erosion` | the erosion pass of the planet generator tool (editor assembly, by reflection): six runs on the same 256 by 128 bowl and three on rough ground with the tool's default settings, how many different results they give, and the seconds per run. A bowl sends every drop to the middle of the map, which is where the eight threads of the old code got in each other's way. No planet file is read or written (about 65 s) |
| `smallfixes` | how much memory an update of the game time takes when the hour does not change (with the control that shows the measurement works) and that the stardate texts still follow the time, the Statement button in a neutral posture, the planet generator tool's contract resolver (two includes for one type, an include for a type with ignores), the system display with a planet in orbit 9, the legend texture field, and last the ship's log scroll loops with rows of no height and a list that is scrolled down. **The code before the fix hangs in that last step**: run it there with `-TimeoutSeconds 100` and read the check lines above it |
| `starport-savedata` | a save with more armor points than its armor allows is cut back when it is loaded (and one written with a destroyed ship still loads with 1 point), the dates of the bank, of the Operations notices and of their ship's log entries with the computer set to the Thai calendar, a ship's log entry dated by an older build after a load, and the starting balance of a build and of the Editor |
| `encounterdata` | the encounter data corrected to the original: the Thrynn scouts 144 to 146 in hyperspace (game data, a new game, and a save that has scout 144 in a star system, with another encounter of that save as the control), every encounter in orbit at an orbit that has a planet, and in the game the Veloxi drone 302 at the planet in orbit 5 of star 29 (the other planet as the control) and the Spemin home fleet 139 at the one planet of star 32 (about 20 s) |
| `drones` | the Veloxi drone 302 at the planet in orbit 5 of star 29: its two lines from the recovered data file, the three numbers it asks (1 to 99), permission to orbit after three right answers and the ship back in orbit, no new encounter on going into orbit again in the same visit, the drone asking again on a new visit (through hyperspace), a wrong answer (permission denied, the comm link ended, the drone hostile), the way out after a refusal, and the comm link ended by the player before answering (about 45 s) |
| `gameclock` | how many game seconds pass in 1.5 real seconds in the star system (control), in orbit, on a planet's surface, in the terrain vehicle (through the real Disembark button), in an encounter and in the docking bay (control: none), and the fuel raised shields use at the star hour in an encounter (about 40 s) |
| `shipmodels` | alien ship models: `Encounter.Start` with an empty model slot, which vessels still clone the placeholder ("Not Modeled Yet", a stretched sphere), and every vessel in a real hyperspace encounter: that it gets a model, its size in the ship's own frame (at least a quarter of the player ship), and its debris on the frame it appears (the size and the orientation of the ship). Vessels in `c_shipModelLengths` (the procedural stand-ins of `DevTools/ShipModels`) are also checked against their length, that they are longest along the nose and that their engines (a material named `... Engine`) are at the back (about 30 s) |
| `recovereddata` | the lists of the recovered data file (planet messages, artifact sites, colony evaluations, story texts): their counts, that every record resolves to a planet (and an artifact) of the game data, eight planets worked out by hand, the guards in orbit at the planets STRINFO names, the story texts with their line breaks, and the game data file unchanged as the control. Reads the lists by reflection, so it compiles on the code before them (about 25 s) |
| `flaredata` | the flare day of the 36 stars that flared before the game began (negative, Earth's sun -60), the 234 still to flare (days 4 to 792, Arth 300) and their date fields in a calendar of 10 months of 30 days as controls, and the shine of Earth's sun (a stable sun) next to Arth's (about 35 s) |
| `calendar` | the stardate texts of the clock in the original's calendar of 10 months of 30 days (day 0 as the control, the turn of a month, day 40, day 299, day 300, day 365), the day of Arth's flare against the date fields of its star, and a save made in the real-world calendar (bank, ship's log, current date) next to one made in the original's (the control) after loading (about 30 s) |
| `pickups` | on planet 90 in the terrain vehicle: a deposit taken whole through the real Cargo button, one taken in part (the hold has room for 1 cubic meter), and one left alone as the control, found again by where they were placed after going back into the ship and out again, and the record of them through a save and a load (about 35 s) |
| `ruins` | on Earth (planet 5), landed at 11N x 104W: the ruins of the recovered data's messages (one per site, one per message at a random place: 12), the ruin of the site at its latitude and longitude with both of its messages, no rock or tree next to a ruin, the real Cargo button recording the two messages in the ship's log dated that day and only once, the scan naming the ruin, the same places after going out again, and planet 90 with no ruins as the control (about 60 s) |
| `artifactsites` | the owner's rulings on the Rod Device (54N x 13E) and Koann 3 (planet 58), then on Earth at 11N x 104W: the Hypercube's site in the ruin of the invoice (still 12 ruins), the scan seeing the artifact, the real Cargo button taking it into the terrain vehicle once and the taking in the save, the Hypercube in the ship's hold after going back in, nothing more to take after going out again, and the ruin's two messages recorded as before (the control) (about 35 s) |
| `formations` | on Sphexi (the drones' permission granted first), the Crystal Orb's ruin at 46N x 14E in the middle of six ruins at one distance and 60 degrees apart, on the first planet of 56, 144 the Crystal Pearl's ruin at 28N x 13W with fifteen ruins none of which is south or west of it, and Earth's 12 ruins as the control (about 40 s) |
| `dropcargo` | on planet 90 with 3 cubic meters of an element and a Hypercube in the terrain vehicle: the real Cargo button listing the hold with the drop buttons, Next and Drop through the button controller (the Hypercube, then the element, and the console back to the vehicle's buttons), the drops in the save, the scan reporting them, both lying at the spot after going back in and out again, and the Cargo button taking them all back (about 35 s) |
| `cargodisplay` | on planet 90 with an element and a Hypercube in the terrain vehicle: the cargo display shown in place of the terrain vehicle's when the real Cargo button opens the list, its labels and values, how much memory an update takes when nothing changed (with the control that shows the measurement works), the display after the Hypercube is dropped, and Back bringing the terrain vehicle's display back (about 25 s) |
| `crystalfield` | the field of the Crystal Planet: armor lost in 3 s with the shields down, in orbit around planet 90 (the control, none), around the Crystal Planet without the Crystal Orb in the ship's hold (at least 10) and with it (none) (about 40 s) |
| `blackegg` | the Black Egg: dropped from the terrain vehicle on planet 90 with the real Cargo list (the messages say it is armed); back in orbit the countdown, BOOM, planet 90 destroyed in the save and gone from its star system with the ship in the star system; an egg on the Crystal Planet at 0 x 0 (damaged but not destroyed, the egg used up) and at the control nexus 47N x 45E (destroyed, with Interstel's message); back in planet 90's star system after another, planet 90 still gone and the others there (about 70 s) |
| `crystalcone` | the Crystal Cone: the messages of an orbit around the Crystal Planet without the Cone (the control, nothing about the nexus) and with it (the control nexus at 47N x 45E), Land there with the Cone (reported again), and an orbit around planet 90 with the Cone (the control, nothing) (about 50 s) |
| `win` | the win: a Black Egg at the Crystal Planet's control nexus setting the win and the pending bonus; after the win, in the system of the star that flares soonest through the day of its flare (no flare; `gameover` has the control); docking at the Starport paying 500,000 MU with a ledger entry and a message, and docking again paying nothing (about 60 s) |
| `starport-win` | the Starport's Evaluation screen before the win (the control, the scene's text about colony recommendations) and after it (Interstel's supplemental evaluation on the completion of the mission) (about 20 s) |
| `arthflare` | Arth's sun flares: in the system of a star that flared before the game began, the day before the flare (the control, the Starport there) and on the day (the Starport destroyed, the ship fine); Distress then (no response); in Arth's system after it (no Starport model, no docking in orbital range, the Starport does not answer); after the win (the control, no flare); in Arth's system on the day without the win (the ship incinerated with STRINFO's text, the game over) (about 60 s) |
| `gameover` | every way to lose ends in the same game over: coming into the system of the star that flares soonest on its flare day (the control, fine), in its system the day before (fine, the old code's arrival damage gone), then its flare day coming with the ship there (incinerated, the game over screen with STRINFO's text and the star's coordinates instead of "Ship destroyed!") (about 50 s) |
| `racelosses` | what a Black Egg on Elan or on the Uhlek mind-ganglion does: damage from an Uhlek encounter in 10 s before the ganglion's planet is destroyed (the control) and after (none); an Elowan warship encounter before Elan is destroyed (the control, not hostile, nothing of Elan) and after (hostile, the comm of the game data about the deed most unthinkably foul, no answer to a hail, and they attack) (about 80 s) |
| `tesseract` | the Tesseract: the fuel the engines use in 3 s of hyperspace from a standstill, in a corner of it with no encounter able to begin, without the Tesseract (the control) and with it in the hold (about half, 0.4 to 0.6 for the frame timing) (about 20 s) |
| `whiningorb` | the Whining Orb, with a communications officer of no skill: a Spemin statement from the game data without the Orb (the control, garbled), with the Orb in the hold (every word), and an Elowan statement with the Orb (the control, still garbled) (about 30 s) |

Run them all with `& "DevTools\HeadlessProbe\run-all-scenarios.ps1" -Tag some-label` (74 scenarios, about 42 minutes; one summary block per scenario with the failed checks and exceptions).

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
- **A run writes one font asset again.** A run that shows the "Starport clear" message makes Unity write `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` again (the message has an arrow that only the dynamic fallback font can draw). The file is committed in the format Unity writes, and `.gitattributes` checks it out with LF line endings, so the run leaves the working tree clean. In a checkout made before that (CRLF in the working copy), `git status` lists the file as modified with an empty diff after such a run: `git checkout -- "<path>"` clears it. Stage files by name after a run in any case.
- **Console input without real input:** `SetInput( "m_south", true )` sets an `InputController` property by reflection, and `ConsoleFrameWith( "m_submit" )` runs one `ButtonController.Update` with it held. `InputController.Update` overwrites the property on the next frame, so set it and call the update in the same step.

`changelog_add.py "<title of an existing bullet>" <file with the new bullet>` inserts a CHANGELOG bullet after an existing one (run it from the project root).

### Facts about the data that decide what is worth testing

- Hyperspace encounters show all their ships at once. The 128 star-system encounters have 6 ships, 3 at a time; those are the ones where a ship's index and its model slot drift apart.
- Only vessels 1 to 4 (Spemin, Mechan) and 20 have a debris model. Encounters 115 and 116 are Spemin, 7 is Elowan.
- Mechans are hostile to a ship with no human crew.
- Twelve encounters are in orbit around a planet (location 2). Star 30 has one of them (316, a derelict at planet 115) and no other encounter, which makes it the quiet place to test them. Since the encounter data was corrected to the original (2026-10-09) all twelve name an orbit that has a planet; before that 139, 165, 302 and 305 could never begin. Star 32 (82,148) has the Spemin home fleet and five Spemin star system groups.
- The Arth system (where a scenario starts) has four planets besides Arth: 90, 91 and 92 are frozen, 94 is a small rock planet. Planet 90 has a mineral density of 43% (762 deposits, 2218 cubic meters in all). Landing and disembarking work headless; the landing animation takes 35 s.
- A new game has 20.0 cubic meters of Endurium (200 tenths), 250 armor points and, in the Editor, 1,000,000 MU (a build starts with the original 12,000 MU; the probe always runs in the Editor).

## Limits

- No graphics. Anything visual has to be checked in the GUI Editor.
- It reports what the code does, not what a player sees, and it drives the game through its own methods, not through real input.
