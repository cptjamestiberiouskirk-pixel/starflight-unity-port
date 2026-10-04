# Changelog

All notable changes to the Starflight Unity Port will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Compile Check CI**: `.github/workflows/compile-check.yml` runs a headless compile with the project's Unity version on every pull request and push to `master` using GameCI (`game-ci/unity-test-runner`, EditMode). Needs the `UNITY_EMAIL` and `UNITY_PASSWORD` repo secrets, plus `UNITY_SERIAL` (Pro/Student) or `UNITY_LICENSE` (Personal). Running it by hand (`workflow_dispatch`) takes an optional `unityVersion` to compile against a different editor. A final step lists any committed `Packages/` or `ProjectSettings/` files Unity rewrote during the run
- **Unity CLI Editor Bridge**: `com.unity.pipeline` 0.8.0-exp.1 (experimental) lets the Unity CLI drive an Editor that has the project open: `unity status` to see it, then `unity command console`, `recompile`, `editor_play`, `eval` and about 160 others. Tools such as Claude Code can read the console and check compiles through it. Its server listens on 127.0.0.1 only and needs a per-Editor token. It is compiled out of release builds, and in development builds it stays off unless switched on in its settings. Brings `com.unity.nuget.mono-cecil` back as a dependency
- **Headless Play-Mode Probe**: `DevTools/HeadlessProbe/` runs the real Spaceflight or Starport scene headless in play mode, with an in-memory save system so that no save file is touched, and checks game behaviour through scenarios, one for every fix made since 2026-10-03. A scenario starts once the planets of the first star system have been generated, because the game is paused until then. `probe.ps1` runs one scenario, `run-all-scenarios.ps1` runs them all and `compile-check.ps1` is the local compile check. The folder is outside `Assets/`, so it is not part of the build, and `.gitignore` keeps the copy that a run puts into `Assets/` from being committed

### Changed

- **Unity Editor 6000.3.13f1**: `ProjectSettings/ProjectVersion.txt` moves from 6000.3.2f1 to 6000.3.13f1 (8c4f11e4fb20). The project compiles on 6000.3.13f1 in CI with no errors

### Removed

- **Stale Crash-Recovery Scene Copy**: `Assets/_Recovery/0.unity`, its `.meta` and the folder's `.meta`. It was Unity's crash-recovery copy of `Assets/Scenes/Spaceflight.unity`, committed with the combat work in b9727c5. Nothing referenced it and it was not a build scene. It differed from the real scene in two lines: the `ScanType.Unknown` slot of the `SensorsDisplay` background and mask texture arrays pointed at the Spemin Scout textures instead of the Spemin Warship debris textures. `Assets/_Recovery/` and `Assets/_Recovery.meta` are now in `.gitignore`
- **Unused AI Packages**: `com.unity.ai.generators` (deprecated by Unity in favour of `com.unity.ai.assistant`) and `com.unity.2d.enhancers`, which depends on it. Nothing in the project used either one. Their dependencies `com.unity.2d.common` and `com.unity.settings-manager` go with them
- **Unity AI Assistant and Inference Packages**: `com.unity.ai.assistant` and `com.unity.ai.inference`. No script, scene or prefab used either one. Six packages that only they pulled in go with them (`com.unity.ai.toolkit`, `com.unity.serialization`, `com.unity.dt.app-ui`, `com.unity.collections`, `com.unity.nuget.mono-cecil`, `com.unity.test-framework.performance`), along with what App UI had left in `ProjectSettings`: its entry in `EditorBuildSettings.asset` and the `APP_UI_EDITOR_ONLY` scripting define
- **MCP for Unity**: `com.coplaydev.unity-mcp`. The Unity CLI bridge (`com.unity.pipeline`) is the one Editor bridge now, and nothing in the project used the MCP package. `com.unity.nuget.newtonsoft-json`, which `PG_ContractResolver` needs, still arrives through `com.unity.pipeline`

### Fixed

- **Alien Comm Progress Lost on Load**: `PD_General.m_lastCommIds` was an `int[,]`, which `JsonUtility` does not serialize, so per-race question/answer progress was never written to the save file and reset on every load. It's now a flat `int[]` (20 races x 16 subjects) accessed through `GetLastCommId()` / `SetLastCommId()`. Older saves without the field still load (the table is re-allocated on first use) and the save version is unchanged
- **Compiler Errors from 0.9.0 Bug-Fix Pass**: `AnalysisButton` bounds checks used `.Count` on the `GD_Vessel[]` vessel list (now `.Length`), and the iteration cap added to the `TradeDepotPanel` selection-box loop left `selectionBoxOffset` not definitely assigned (CS0165; now initialized, behavior unchanged). Found by the new compile check
- **Playing in the Editor Modified `Black.mat`**: the viewport fade wrote its opacity into the shared material asset, so every play session in the Editor left `Assets/Shared/Colors/Black.mat` modified in git. `Viewport` now fades its own copy of the material
- **Wrong Encounter Loaded**: `Radar` keeps `PlayerData.m_encounterList` sorted by distance, but `Encounter.Show` (since b9727c5) picked the encounter by array index, so most encounters loaded another encounter's ships, stance and position and saved kills to the wrong record. Encounters are found by `m_encounterId` again through `PlayerData.FindEncounter()`
- **Destroyed Encounters Kept Coming Back**: an encounter whose ships had all been destroyed still chased the player, stayed on the radar and started again as an empty encounter. `SpaceflightController.UpdateEncounters` now skips encounters with no living ships
- **Alien Comms Missing from Ship's Log After Load**: `PD_ShipsLog.m_alienComms` was a `List<Entry>[]`, which `JsonUtility` does not serialize, so the entries were never saved and the Alien Comms buttons threw a NullReferenceException after loading. It's now an array of serializable `EntryList` wrappers read through `GetAlienComms()`
- **Save Locked After Deleting Assigned Crew**: deleting a personnel file left crew roles pointing at it, so Crew Assignment and the Docking Bay threw on every open and the ship could never launch. Deleting now unassigns the crewmember, and loading repairs saves already in that state
- **Starport Panels Stayed Live While Closing**: a second click on Exit closed the panel again, so the Trade Depot and Ship Configuration logged their bank transaction twice, and the panel's buttons still worked while it slid away. A closing panel now ignores further Exit clicks and input, and its controls are switched off until it is opened again
- **Console Stayed Live During the Landing**: the Descend button did not keep the console to itself, so for the 35 seconds of the landing animation the stick still moved the selection and the fire button still activated whatever was selected. Abort brought the bridge buttons back while the ship kept landing. The console is locked now until the ship is down, the same way it is during a launch
- **Landing Said "Safe landing, captain." 23 Seconds Early**: the Descend button printed its own messages on a timer taken from the original game's 12 second landing, on top of the ones the 35 second landing animation prints. "Autopilot engaged. Descending..." appeared twice, and "Safe landing, captain." appeared at 12 seconds and again at touchdown. The button now only prints "Topography net locked on."; the animation reports the start of the descent and the landing
- **Trade Depot Sell Exploit**: selling didn't check the amount against the cargo hold, so any amount could be sold for money and the element volume went negative. Selling more than you have now shows an error, and `PD_ElementStorage.Remove` refuses to go below zero
- **Selling Armor Kept Its Armor Points**: selling the armor reset the armor class but not the armor points, so buying class 5 armor and selling it straight back left a ship with no armor and 1,500 armor points for a net 1,200 MU. Selling armor now puts the ship back at the bare hull's 250 points, or fewer if it was damaged
- **A New Ship Showed "83% Hull Damage"**: the status display measured the armor against 1,500 points and the shields against 2,500, the best equipment there is, so an undamaged new ship read "83% Hull Damage" and its gauges could only fill up with class 5 equipment. The Damage report and the Repair button took the maximum from the armor plating instead, which is 0 for a ship without any, so the bare hull (250 points) was reported as "None installed" and could not be repaired. The ship now has one answer for the most armor and shield points it can have (`PD_PlayerShip.GetMaximumArmorPoints()` and `GetMaximumShieldPoints()`): the points of the installed armor, or the bare hull's 250. The damage text, both gauges, the Damage report and the Repair button all use it, the engineer repairs a bare hull ("Beginning repairs on the hull, Captain."), and the "Hull breach imminent" warning comes below a quarter of that maximum instead of below a fixed 250 points
- **Trade Depot Amount Parsing**: the decimal part was added as a raw integer (`5.25` became 7.5) and a lone `-` threw `FormatException`. Amounts are parsed as tenths with the invariant culture, accept a comma as the decimal point, and reject signs
- **Cargo Pods Could Be Sold From Under Their Cargo**: a cargo pod could be sold while the hold needed its space, leaving a negative free volume that the Trade Depot's "hold is full" check then missed. A pod can only be sold when the cargo fits without it ("Unload cargo first"), and the Trade Depot treats a negative free volume as full
- **Mineral Deposits Could Be Picked Up Twice**: a deposit stays on the surface for the 1.5 seconds of its transporter effect and could be picked up again during that time, so pressing Cargo twice put it in the hold twice. A deposit can be picked up only once now
- **Cargo Volumes Were Shown Ten Times Too Large**: the cargo holds count in tenths of a cubic meter, and several texts printed that number as cubic meters. The ship's cargo list showed 200 for 20.0 cubic meters of Endurium, the terrain vehicle said "Picked up 3 cubic meters" for a deposit of 0.3, and its cargo list read "Capacity: 3/500 m³" for 0.3 of 50.0. They all show cubic meters now. What is picked up has not changed
- **A Deposit Too Large for the Hold Vanished**: when the terrain vehicle had room for only part of a deposit, it took that part and the whole deposit disappeared. The rest now stays on the ground, and the message says how much is left
- **Bank Ledger Showed Income as "-1400+"**: leaving the Trade Depot or Ship Configuration with more money than before wrote the amount with its minus sign and a plus behind it. It reads "1400+" now, as spending reads "500-"
- **Buy Maximum Overflowed Above 214 Million MU**: the Trade Depot worked out the most the player can afford by multiplying the balance by ten in 32 bits, so with 300,000,000 MU it came to a negative amount. It is worked out with 64 bits now
- **Sensor Analysis Said "0 times the size of our ship"**: the size of a scanned vessel is its mass divided by ours, and the division dropped everything after the point, so a vessel half our size was 0 times and one 8.9 times was 8. It has one decimal now
- **Garbled Alien Words Dropped**: 70445da left the capitalization `if` without a body, so it guarded adding the word and most garbled words disappeared from alien messages. The capitalization block is restored
- **Missile Immunity Never Loaded**: the game data key was spelled `m_immuneToMissles`, so `GD_Vessel.m_immuneToMissiles` was always false. The key is renamed (no values changed); Gazurtoid ships and the Mysterion are immune again
- **Missile Launchers Could Never Fire**: the missile count was only ever counted down. Nothing stocked it, so a bought launcher always answered "Out of missiles!". Missiles are no longer counted. Every launch uses 0.02 cubic meters of Endurium and every laser shot 0.01, and neither weapon fires with no Endurium on board
- **Most Alien Ships Scanned as "Unknown"**: the sensor display has no picture for 15 of the 23 vessel types, and when the picture was missing it also replaced the scan type with Unknown. Scanning an Elowan, Thrynn, Veloxi, Gazurtoid or Uhlek ship (or debris) showed "Unknown object detected" and the analysis said "Insufficient data". Now only the picture falls back to the unknown one; the readout and the analysis are for the real ship
- **Training Blocked Once Medicine Was Maxed**: the Train check compared only the last skill. It now compares the totals across all skills
- **Repair and Treat Were Instant and Free**: one press of Repair restored five armor points per point of engineering skill on the spot, and one press of Treat healed every hired person at once, including people who were left at Starport. Both take time now. The engineer repairs the armor at 0.02 points a second per point of skill (never less than 0.5), the doctor treats the most injured living crew member on board at the same rate, one patient per command, and both report when they are done. Pressing the button again shows the progress and the time left
- **Planet Gravity Lost a Zero**: gravity is stored in hundredths of a G, and the text dropped the leading zero of the fraction, so 105 read "1.5 G" instead of "1.05 G" and 5 read "0.5 G" instead of "0.05 G" (105 of the 811 planets). The fraction is always two digits now
- **Entering a Star System Froze the Game**: the maps of each planet are worked out on a background thread, but the main thread then waited for that thread to finish, so the game stood still for as long as each planet took and the "Commencing System Penetration" progress bar only moved once per planet. The main thread now looks each frame whether the planet is done and carries on when it is, so the game keeps running and the bar moves while a planet is being worked on
- **Planet Colours Were Blurred to One Side Only**: the filter that makes a planet's colour map softens it with a small blur, but a slip in the index meant every pixel took a quarter from its right neighbour and nothing from its left. It takes a quarter from each side now. A second blur, from row to row, built a 32 MB map for every planet and then threw it away; it is gone, and nothing it did was ever on screen. On planet 90 the fix changes 7% of the pixels, by 1.4 of 255 on average and by 64 of 255 at the most, at the edges between colours
- **Planet Generator Tool Padded the South Pole With the North Pole's Heights**: the tool adds rows above and below each planet's 24 rows of source data, and those rows blend to one height per pole. For the south pole it looked at the colours of the bottom row but took the heights from the top row. It reads the bottom row now. This only shows in planets that are generated again: the 811 planet files in the project were made with the old code and are not touched. For 273 of them the south pole would come out at a different height, by a fifth of the height range at the median
- **Planet Generator Tool Could Hang or Fill Planets With NaN**: an exception anywhere in the tool left its progress bar on screen, so the Editor looked as if it had hung (a wrong game data file name was enough). With Evaporation Constant at 0 a rain drop of the erosion pass took 25 times as long to end, and with Friction Constant at 0 as well it never ended. Mountain Scale or Lacunarity at 0, or a Final Blur Radius of 0, divided by zero and filled the height map with NaN. The tool now checks its settings before it starts and says what is wrong, always takes its progress bar away again, gives every drop a limit of steps (twice what its water can last), and treats a blur radius of 0 as no blur. Texture Map Height has to be 1024, the one size the game can read
- **A Damaged Planet File Locked the Game**: when the data file of a planet could not be read (damaged, or made by another version of the planet generator), the error came back on the main thread every frame, so the game stayed paused in the middle of "Commencing System Penetration" for good. Entering the system had already saved the game, so loading the save led straight back into it. A file that was cut off inside its last part raised no error at all and left part of the planet's surface data empty. A planet whose file cannot be read is now skipped with an error in the log, and the other planets of the system are generated as usual. The reader checks the version, the size of the maps, that all of the data is there and nothing more, and the checksum of the file. All 811 planet files in the project pass it
- **Landing on a Planet That Could Not Be Generated**: when the maps of a planet cannot be generated (its data file is missing or damaged), the planet has no elevation data, and the Descend button ran into that with an exception after "Computing descent profile...". The Land button now refuses such a planet ("We can't land here. The surface of this planet could not be mapped."), and so does Disembark for a saved game that is already on its surface
- **Planet Maps Were Never Freed**: every planet of a star system gets its texture maps made when the ship enters the system, and the maps of the system before were left in memory, with the planet files they were made from. Loading the next scene (docking at Starport) cleared most of them out, but not the maps of the last system. Flying through four systems took the textures made at runtime from 22 MB to 69 MB in a headless run, which has no graphics memory to count. The maps of a planet are now destroyed as soon as the maps of the planet that takes its place are on its model, or at once when its orbit is empty in the new system or the scene is left, and a planet file is unloaded as soon as its data has been copied
- **Raising Shields With No Fuel**: shields could be raised with no Endurium, then every game hour fuel use threw a NullReferenceException. Raising shields now requires Endurium
- **Lowering and Raising Shields Refilled Them**: lowering the shields threw their charge away and raising them set it to full, so two button presses in the middle of a fight restored the shields completely. The shields now keep their charge when they are lowered, only absorb damage while they are up, and regain 1% of their full charge every 5 seconds (the rate in the original design notes). Starport recharges them fully while the ship is docked. A save made with the shields down gets a full charge once
- **Progress Lost on Quit**: the game only saved on location changes and panel closes. It now also saves when the application quits, except when the ship has been destroyed
- **A Game Saved in the Terrain Vehicle Loaded Into Starport**: the game saves when the terrain vehicle is entered, but the list that says which scene a saved game belongs to did not know that location, so continuing such a game opened the Starport scene. It opens the Spaceflight scene now. The save panel did not know the location either and ran the ship's line into the location line; it says "Terrain Vehicle" now
- **Stardate Depended on the Computer's Calendar**: the stardate text was made with the calendar of the computer's region. On a computer set to the Thai calendar the year read 5163 in place of 4620, and on one set to the Saudi Arabian calendar, which has no year 4620, working out the game time failed on every frame of spaceflight. The stardate is always made with the same calendar now
- **New Game Terrain Vehicle Was Never Set Up**: a new game made its terrain vehicle but did not reset it, so it started with no fuel and no cargo holds, and only worked because disembarking refuels it and the first pickup makes the hold. A new game resets it like everything else now
- **Game Over Put You Back in the Lost Fight**: after the ship was destroyed, returning to the title screen kept the lost game in memory, so continuing dropped the player into the same encounter with no armor and the aliens still hostile, and closing the save panel while the ship was exploding wrote the destroyed ship to the slot. The title screen now reloads the slot from its last save (the one made when the encounter began), and a destroyed ship is never saved, copied to another slot or carried through a slot switch. A save that an older build wrote with a destroyed ship loads with 1 armor point so that it can be saved again
- **A Crash While Saving Could Wipe the Slot**: saves were written straight over the old file, and a save that could not be read was silently replaced by a new game and then overwritten. A save is now written to a temporary file and swapped in, the save it replaces is kept as `.bak`, loading falls back to that backup, and a save that can't be read is moved to `.corrupt` so nothing overwrites it
- **Debris and Missiles Used the Wrong Alien Ship Model**: combat used a ship's position in the encounter's ship list as its model slot, but the model slots are packed so that only living ships get one. After leaving and re-entering an encounter with ships already destroyed, a kill spawned its debris into another ship's slot or an empty one, and an empty slot made `Encounter.Update` throw every frame so the player could not leave. `Encounter` now keeps a ship-to-slot map behind `GetAlienShipModel()`, used for debris and for missile homing
- **Aliens Never Fought Back**: only the Mechans ever turned hostile, firing on a ship changed nothing, and the Uhlek had no update at all, so their 67 encounters never acted. Firing a laser or launching a missile at any alien now makes that encounter hostile until the player leaves it, the Uhlek attack on sight and never talk, and a race with no update of its own (the Enterprise) shoots back once it has been fired on
- **Destroyed Ships Kept Flying**: a destroyed ship was still moved every frame, so its wreckage chased the player. Only vessels 1 to 4 and 20 have a debris model, so destroyed Elowan, Thrynn, Veloxi, Gazurtoid and Uhlek ships kept their normal model as well and looked alive. Destroyed ships now stay where they died, a ship with no debris model disappears with its explosion, and the encounter camera only keeps living ships in view
- **Combat Target Carried Over Between Encounters**: the selected target was never cleared, so a target picked in one encounter was still set in the next one, and firing only checked that the index was in range. A stale target could be a ship that was dead or not in the encounter yet, so the player could destroy a ship that was not on screen. The target is now cleared whenever an encounter is entered, and firing at a target that is not a living ship in the encounter clears it and says so
- **Unity Object Checks That Missed Destroyed Objects**: four places tested Unity objects with `??` or `?.`, which do not see that an object has been destroyed (the Target button, the F10 debug key, and the mineral and artifact pickups), and the terrain vehicle's collider compared tags with `==` and read the first contact point of a collision without checking that there was one. They use `== null`, `CompareTag` and a contact count check now, as the project rules ask. Nothing a player could see changes
- **Missiles Carried Over Between Encounters**: a missile still in the air when the player left an encounter kept flying and still did its damage when it arrived. An alien missile could hit the player in the next encounter, and a player missile damaged the ship with the same number in the next encounter. Every missile is now taken out of the air when an encounter ends and when one begins
- **Ship Could Be Destroyed More Than Once**: a hit on a ship that was already exploding destroyed it again, with another wreck, another explosion and another call of the game over screen. The aliens' own fire did this during the 1.5 seconds of the explosion. The ship is destroyed once now: missiles in the air are taken out when it goes, the aliens stop firing at the wreck, and the weapons of a destroyed ship do not fire
- **Missiles Kept Flying While the Game Was Paused**: a missile in the air flew on behind the save panel, the starmap and the ship's log, and did its damage there. Missiles stand still while the game is paused now and fly on afterwards
- **Explosions Stayed at Half Size**: a missile hit plays its explosion at half size, and the explosion kept that size when it was used again, so the next ship to be destroyed could go up in a half-size explosion. Every explosion starts at full size now
- **A Launch With Every Missile in the Air Cost Fuel and Launched Nothing**: only eight missiles can be in the air at once, the aliens' included. With all eight in the air a launch still used its Endurium, made the aliens hostile and said "Missile launched!", but nothing was launched. Such a launch does nothing and costs nothing now
- **Alien Ships Had No Hit Points**: damage to an alien ship was worked out from scratch on every hit and never stored, so a hit either destroyed the ship outright or did nothing at all (a class 1 laser could never destroy a Spemin Warship, and one-shot an Elowan). Alien ships now have armor and shield points, 100 per class of their vessel, saved with the game. Shields absorb damage first. Ships in older saves get their points on the first hit

## [0.9.0] - 2026-01-04

### Added

#### Combat System Enhancements
- **Player Ship Destruction & Game Over**: Complete implementation of player death sequence with explosion effects, debris spawning, and game over screen
- **Debris System**: New `DebrisTumble.cs` component for destroyed ship wreckage with tumbling physics
- **Victory Detection**: `AllEnemyShipsDestroyed()` method to detect when all enemy ships are destroyed
- **Debris Scanning**: Ability to scan debris fields after ship destruction for salvage opportunities
- **Combat Exit After Victory**: Players can now leave encounters after destroying all enemy ships

#### Encounter System Improvements  
- **Alien Stance Tracking**: Fixed hostile stance persistence throughout combat encounters
- **Ship Model Validation**: Added bounds checking and error logging for ship model templates
- **Debris Spawning**: `SpawnDebrisForShip()` method creates debris at destroyed ship locations

#### Sensor Display Enhancements
- **Debris Scan Type**: New `ScanType.Debris` (index 24) for scanning destroyed vessel wreckage
- **Fallback Textures**: Improved texture array bounds checking with Unknown type fallback
- **Debris Analysis**: Analysis button now shows debris information when scanning wreckage

#### Terrain & Exploration
- **TerrainArtifact.cs**: New component for artifact placement on planet surfaces
- **TerrainRuins.cs**: New component for ancient ruin locations on planets

#### Debug Features
- **F9 Test Encounter**: Spawns hostile Spemin for combat testing
- **F10 Instant Death**: Destroys player ship for game over testing
- **Extensive Logging**: Debug output for stance changes, combat events, debris spawning

### Fixed

#### Critical Bug Fixes
- **ESC Key on Game Over**: Now correctly returns to Intro scene instead of loading saved game
- **NullReferenceException in PD_ShipsLog**: Added null checks for `m_alienComms` array (line 118)
- **Encounter Exit Bug**: Fixed inability to leave encounter after destroying all enemies
- **Hostile Stance Not Persisting**: Fixed stance resetting during combat flow

#### Combat Fixes
- **Enemy Ships Not Firing**: Fixed `UpdateHostileFire()` to properly iterate alien ships
- **Weapon Arming**: Fixed weapon arm/disarm state management during encounters
- **Damage Application**: Corrected damage calculation for shields and armor

#### Display Fixes
- **Sensors Display Bounds**: Added bounds checking for background and mask texture arrays
- **Status Display Updates**: Fixed real-time updates during combat

### Changed

- **RestartGame()**: Now loads "Intro" scene instead of "Persistent" for proper restart flow
- **SpaceflightController**: Refactored game over and restart logic
- **CombatController**: Improved object pooling for combat effects
- **Encounter.cs**: Major refactoring of combat AI and stance management (~277 lines added)

### Technical Notes

- All changes tested with Unity 6 (6000.3.2f1)
- No breaking changes to save file format
- Added backwards compatibility for older saves missing `m_alienComms` array

---

## [0.8.0] - Previous Release

### Added
- Initial combat system implementation
- Alien encounter framework for 10 races
- Planet generation system
- Starport operations

*For earlier changes, see git commit history.*
