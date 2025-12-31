# Starflight Unity Port - Project Context

## Overview
This project is a Unity-based port of the classic space exploration game *Starflight*. It aims to recreate the original mechanics, including space travel, planet exploration, crew management, and alien encounters, within the Unity engine.

## Architecture & Key Systems

### 1. Persistence & Central Control
The game relies on a set of singleton-like controllers that persist across scenes (managed by [`Persistent.cs`](Assets/Scripts/Persistent/Persistent.cs)).
- **[`DataController`](Assets/Scripts/Persistent/DataController.cs)**: The primary manager. It handles:
    - Loading static game data from `Starflight Game Data.json`.
    - Managing player save slots (up to 5) using binary serialization.
    - Scene transitions based on the player's current game state.
- **[`InputController`](Assets/Scripts/Persistent/InputController.cs)**: Manages player input.
- **[`PanelController`](Assets/Scripts/Persistent/PanelController.cs)**: Manages UI panels and navigation.
- **[`SoundController`](Assets/Scripts/Persistent/SoundController.cs) / [`MusicController`](Assets/Scripts/Persistent/MusicController.cs)**: Handle audio playback.

### 2. Data Structures
The game distinguishes between static world data and dynamic player state.
- **Game Data ([`GameData.cs`](Assets/Scripts/Game Data/GameData.cs))**: Read-only data representing the universe (stars, planets, races, items). Loaded at startup.
- **Player Data ([`PlayerData.cs`](Assets/Scripts/Player Data/PlayerData.cs))**: The mutable state of the player's current game (ship upgrades, crew stats, logs, bank balance). This is what gets saved and loaded.

### 3. Relationships: Managers and the Player
- The **[`DataController`](Assets/Scripts/Persistent/DataController.cs)** acts as the bridge. It holds the active `PlayerData` instance.
- Other managers (like `PanelController` or `CombatController`) query `DataController.m_instance.m_playerData` to display information or update the player's state.
- **Player-Centric Systems**:
    - **[`PD_PlayerShip`](Assets/Scripts/Player Data/PD_PlayerShip.cs)**: Tracks ship components and status.
    - **[`PD_Personnel`](Assets/Scripts/Player Data/PD_Personnel.cs)**: Tracks the roster of available crew.
    - **[`PD_CrewAssignment`](Assets/Scripts/Player Data/PD_CrewAssignment.cs)**: Tracks which crew members are in which bridge positions.

### 4. Gameplay Modules
- **Spaceflight**: Logic for navigating star systems and hyperspace.
- **Planet Exploration**: Includes procedural planet generation and terrain vehicle mechanics.
- **Starport**: A menu-driven interface for ship maintenance and crew management.
- **Combat**: Tactical ship-to-ship combat.

## Key Files for Reference
- [`DataController.cs`](Assets/Scripts/Persistent/DataController.cs): Entry point for data and scene management.
- [`GameData.cs`](Assets/Scripts/Game Data/GameData.cs): Schema for the game world.
- [`PlayerData.cs`](Assets/Scripts/Player Data/PlayerData.cs): Schema for the player's progress.
- [`Starflight Game Data.json`](Assets/Resources/Starflight Game Data.json): The actual database of the game world.

## Current Functionality
- Save/Load system is implemented.
- Scene switching between Starport and Spaceflight is operational.
- Basic UI framework (Panels) is in place.
- Procedural planet generation systems are present.
