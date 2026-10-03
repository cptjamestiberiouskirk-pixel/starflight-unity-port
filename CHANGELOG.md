# Changelog

All notable changes to the Starflight Unity Port will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Compile Check CI**: `.github/workflows/compile-check.yml` runs a headless compile with the project's Unity version on every pull request and push to `master` using GameCI (`game-ci/unity-test-runner`, EditMode). Needs the `UNITY_EMAIL` and `UNITY_PASSWORD` repo secrets, plus `UNITY_SERIAL` (Pro/Student) or `UNITY_LICENSE` (Personal). Running it by hand (`workflow_dispatch`) takes an optional `unityVersion` to compile against a different editor

### Changed

- **Unity Editor 6000.3.13f1**: `ProjectSettings/ProjectVersion.txt` moves from 6000.3.2f1 to 6000.3.13f1 (8c4f11e4fb20). The project compiles on 6000.3.13f1 in CI with no errors and no package changes

### Fixed

- **Alien Comm Progress Lost on Load**: `PD_General.m_lastCommIds` was an `int[,]`, which `JsonUtility` does not serialize, so per-race question/answer progress was never written to the save file and reset on every load. It's now a flat `int[]` (20 races x 16 subjects) accessed through `GetLastCommId()` / `SetLastCommId()`. Older saves without the field still load (the table is re-allocated on first use) and the save version is unchanged
- **Compiler Errors from 0.9.0 Bug-Fix Pass**: `AnalysisButton` bounds checks used `.Count` on the `GD_Vessel[]` vessel list (now `.Length`), and the iteration cap added to the `TradeDepotPanel` selection-box loop left `selectionBoxOffset` not definitely assigned (CS0165; now initialized, behavior unchanged). Found by the new compile check
- **Wrong Encounter Loaded**: `Radar` keeps `PlayerData.m_encounterList` sorted by distance, but `Encounter.Show` (since b9727c5) picked the encounter by array index, so most encounters loaded another encounter's ships, stance and position and saved kills to the wrong record. Encounters are found by `m_encounterId` again through `PlayerData.FindEncounter()`
- **Alien Comms Missing from Ship's Log After Load**: `PD_ShipsLog.m_alienComms` was a `List<Entry>[]`, which `JsonUtility` does not serialize, so the entries were never saved and the Alien Comms buttons threw a NullReferenceException after loading. It's now an array of serializable `EntryList` wrappers read through `GetAlienComms()`
- **Save Locked After Deleting Assigned Crew**: deleting a personnel file left crew roles pointing at it, so Crew Assignment and the Docking Bay threw on every open and the ship could never launch. Deleting now unassigns the crewmember, and loading repairs saves already in that state
- **Trade Depot Sell Exploit**: selling didn't check the amount against the cargo hold, so any amount could be sold for money and the element volume went negative. Selling more than you have now shows an error, and `PD_ElementStorage.Remove` refuses to go below zero
- **Trade Depot Amount Parsing**: the decimal part was added as a raw integer (`5.25` became 7.5) and a lone `-` threw `FormatException`. Amounts are parsed as tenths with the invariant culture, accept a comma as the decimal point, and reject signs
- **Garbled Alien Words Dropped**: 70445da left the capitalization `if` without a body, so it guarded adding the word and most garbled words disappeared from alien messages. The capitalization block is restored
- **Missile Immunity Never Loaded**: the game data key was spelled `m_immuneToMissles`, so `GD_Vessel.m_immuneToMissiles` was always false. The key is renamed (no values changed); Gazurtoid ships and the Mysterion are immune again
- **Training Blocked Once Medicine Was Maxed**: the Train check compared only the last skill. It now compares the totals across all skills
- **Raising Shields With No Fuel**: shields could be raised with no Endurium, then every game hour fuel use threw a NullReferenceException. Raising shields now requires Endurium
- **Progress Lost on Quit**: the game only saved on location changes and panel closes. It now also saves when the application quits, except when the ship has been destroyed

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
