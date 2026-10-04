# Changelog

All notable changes to the Starflight Unity Port will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Compile Check CI**: `.github/workflows/compile-check.yml` runs a headless compile with the project's Unity version on every pull request and push to `master` using GameCI (`game-ci/unity-test-runner`, EditMode). Needs the `UNITY_EMAIL` and `UNITY_PASSWORD` repo secrets, plus `UNITY_SERIAL` (Pro/Student) or `UNITY_LICENSE` (Personal). Running it by hand (`workflow_dispatch`) takes an optional `unityVersion` to compile against a different editor. A final step lists any committed `Packages/` or `ProjectSettings/` files Unity rewrote during the run
- **Unity CLI Editor Bridge**: `com.unity.pipeline` 0.8.0-exp.1 (experimental) lets the Unity CLI drive an Editor that has the project open: `unity status` to see it, then `unity command console`, `recompile`, `editor_play`, `eval` and about 160 others. Tools such as Claude Code can read the console and check compiles through it. Its server listens on 127.0.0.1 only and needs a per-Editor token. It is compiled out of release builds, and in development builds it stays off unless switched on in its settings. Brings `com.unity.nuget.mono-cecil` back as a dependency

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
- **Trade Depot Sell Exploit**: selling didn't check the amount against the cargo hold, so any amount could be sold for money and the element volume went negative. Selling more than you have now shows an error, and `PD_ElementStorage.Remove` refuses to go below zero
- **Selling Armor Kept Its Armor Points**: selling the armor reset the armor class but not the armor points, so buying class 5 armor and selling it straight back left a ship with no armor and 1,500 armor points for a net 1,200 MU. Selling armor now puts the ship back at the bare hull's 250 points, or fewer if it was damaged
- **Trade Depot Amount Parsing**: the decimal part was added as a raw integer (`5.25` became 7.5) and a lone `-` threw `FormatException`. Amounts are parsed as tenths with the invariant culture, accept a comma as the decimal point, and reject signs
- **Cargo Pods Could Be Sold From Under Their Cargo**: a cargo pod could be sold while the hold needed its space, leaving a negative free volume that the Trade Depot's "hold is full" check then missed. A pod can only be sold when the cargo fits without it ("Unload cargo first"), and the Trade Depot treats a negative free volume as full
- **Garbled Alien Words Dropped**: 70445da left the capitalization `if` without a body, so it guarded adding the word and most garbled words disappeared from alien messages. The capitalization block is restored
- **Missile Immunity Never Loaded**: the game data key was spelled `m_immuneToMissles`, so `GD_Vessel.m_immuneToMissiles` was always false. The key is renamed (no values changed); Gazurtoid ships and the Mysterion are immune again
- **Most Alien Ships Scanned as "Unknown"**: the sensor display has no picture for 15 of the 23 vessel types, and when the picture was missing it also replaced the scan type with Unknown. Scanning an Elowan, Thrynn, Veloxi, Gazurtoid or Uhlek ship (or debris) showed "Unknown object detected" and the analysis said "Insufficient data". Now only the picture falls back to the unknown one; the readout and the analysis are for the real ship
- **Training Blocked Once Medicine Was Maxed**: the Train check compared only the last skill. It now compares the totals across all skills
- **Planet Gravity Lost a Zero**: gravity is stored in hundredths of a G, and the text dropped the leading zero of the fraction, so 105 read "1.5 G" instead of "1.05 G" and 5 read "0.5 G" instead of "0.05 G" (105 of the 811 planets). The fraction is always two digits now
- **Raising Shields With No Fuel**: shields could be raised with no Endurium, then every game hour fuel use threw a NullReferenceException. Raising shields now requires Endurium
- **Lowering and Raising Shields Refilled Them**: lowering the shields threw their charge away and raising them set it to full, so two button presses in the middle of a fight restored the shields completely. The shields now keep their charge when they are lowered, only absorb damage while they are up, and regain 1% of their full charge every 5 seconds (the rate in the original design notes). Starport recharges them fully while the ship is docked. A save made with the shields down gets a full charge once
- **Progress Lost on Quit**: the game only saved on location changes and panel closes. It now also saves when the application quits, except when the ship has been destroyed
- **Game Over Put You Back in the Lost Fight**: after the ship was destroyed, returning to the title screen kept the lost game in memory, so continuing dropped the player into the same encounter with no armor and the aliens still hostile, and closing the save panel while the ship was exploding wrote the destroyed ship to the slot. The title screen now reloads the slot from its last save (the one made when the encounter began), and a destroyed ship is never saved, copied to another slot or carried through a slot switch. A save that an older build wrote with a destroyed ship loads with 1 armor point so that it can be saved again
- **A Crash While Saving Could Wipe the Slot**: saves were written straight over the old file, and a save that could not be read was silently replaced by a new game and then overwritten. A save is now written to a temporary file and swapped in, the save it replaces is kept as `.bak`, loading falls back to that backup, and a save that can't be read is moved to `.corrupt` so nothing overwrites it
- **Debris and Missiles Used the Wrong Alien Ship Model**: combat used a ship's position in the encounter's ship list as its model slot, but the model slots are packed so that only living ships get one. After leaving and re-entering an encounter with ships already destroyed, a kill spawned its debris into another ship's slot or an empty one, and an empty slot made `Encounter.Update` throw every frame so the player could not leave. `Encounter` now keeps a ship-to-slot map behind `GetAlienShipModel()`, used for debris and for missile homing
- **Destroyed Ships Kept Flying**: a destroyed ship was still moved every frame, so its wreckage chased the player. Only vessels 1 to 4 and 20 have a debris model, so destroyed Elowan, Thrynn, Veloxi, Gazurtoid and Uhlek ships kept their normal model as well and looked alive. Destroyed ships now stay where they died, a ship with no debris model disappears with its explosion, and the encounter camera only keeps living ships in view
- **Combat Target Carried Over Between Encounters**: the selected target was never cleared, so a target picked in one encounter was still set in the next one, and firing only checked that the index was in range. A stale target could be a ship that was dead or not in the encounter yet, so the player could destroy a ship that was not on screen. The target is now cleared whenever an encounter is entered, and firing at a target that is not a living ship in the encounter clears it and says so
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
