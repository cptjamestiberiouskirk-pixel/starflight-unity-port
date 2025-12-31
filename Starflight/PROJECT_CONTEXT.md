# Starflight Unity Port - Project Context

## Core Architectural Patterns
- **Singleton Managers**: Most persistent systems use a static `m_instance` reference (e.g., `DataController`, `PanelController`, `CombatController`, `SoundController`).
- **Persistent Root**: A `Persistent` component ensures core managers survive scene changes via `DontDestroyOnLoad`.
- **Data-Driven Design**: 
    - `GameData`: Immutable data loaded from JSON (Resources) containing static game definitions (Vessels, Planets, etc.).
    - `PlayerData`: Serializable state representing the player's progress.
- **Save System (Abstraction)**:
    - Uses an `ISaveSystem` interface to decouple storage logic from `DataController`.
    - `JsonSaveSystem` implementation stores player progress as human-readable JSON files.
    - **Interface Definition**:
        - `void Save(string fileName, object data)`: Serializes data to a file.
        - `T Load<T>(string fileName)`: Deserializes data from a file.
        - `bool SaveExists(string fileName)`: Checks if a save file exists.
        - `void DeleteSave(string fileName)`: Removes a save file.
- **Command/Panel UI Pattern**: UI is organized into "Panels" managed by a `PanelController`. Panels use `Animator` components for transitions and `Tick()` for logic.
- **Pooling**: `CombatController` implements object pooling for projectiles and hit effects to minimize runtime allocations.

## Major Systems
- **Persistence & Save/Load**: `DataController` manages multiple save slots (JSON format), versioning, and scene transitions based on player location.
- **Spaceflight**: 
    - **Navigation**: Different locations (StarSystem, Hyperspace, InOrbit, Planetside) handled as states within the "Spaceflight" scene.
    - **Planet Generation**: Procedural generation of planet textures (Albedo, Specular, Normal, WaterMask) using a dedicated `PlanetGenerator` class.
- **Combat**: Managed by `CombatController`, handling cooldowns, ranges, damage calculations (Shields vs. Armor), and visual effects.
- **UI System**: Abstract `Panel` class provides a foundation for complex menus (Inventory, Banking, Personnel).

## Known "Code Smells" & TODOs
- **TODOs found in comments**:
    - `CombatController.cs:496`: `// TODO: handle game over state` - Ship destruction logic is incomplete.
- **Tight Coupling**: Many classes depend directly on `DataController.m_instance.m_playerData`, making it difficult to test systems in isolation.
- **Procedural Map Generation**: `Planet.cs` processes map generation in the main thread (or via a custom `Process()` loop), which might cause frame spikes if not carefully managed.

## File Structure Highlights
- `Assets/Scripts/Persistent/`: Core engine managers.
- `Assets/Scripts/Systems/SaveSystem/`: Save system interfaces and implementations.
- `Assets/Scripts/Game Data/`: Data structures for static game content.
- `Assets/Scripts/Player Data/`: Data structures for save-game state.
- `Assets/Scripts/Spaceflight/`: Game-world interaction and combat.
- `Assets/Scripts/Panel/`: UI implementation.
