# Changelog

All notable changes to the Starflight Unity Port will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
