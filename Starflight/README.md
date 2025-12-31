# Starflight Unity Port

A modern port of the classic space exploration game *Starflight*, built with Unity 6.

## Features

- **Classic Exploration**: Explore a vast galaxy with thousands of planets.
- **Data-Driven Architecture**: Uses JSON-based definitions for game assets and player progress.
- **Modern Save System**: Robust save/load functionality using the `ISaveSystem` interface.
    - **JSON Serialization**: Player progress is saved in human-readable JSON files, replacing old binary formats.
    - **Flexible Storage**: Decoupled storage logic allows for easy extension (e.g., Cloud Saves).
- **Procedural Planets**: Real-time generation of planet maps and terrain.
- **Combat & Navigation**: Full implementation of spaceflight mechanics, including combat, hyperspace, and planetary landings.

## Getting Started

### Prerequisites

- **Unity 6 (6000.0.x or later)**: This project uses Unity 6 features like `Awaitable`.
- **Git LFS**: Ensure Git Large File Storage is installed for handling large assets.

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/username/starflight-unity-port.git
   ```
2. Open the project in Unity Hub.
3. Ensure you are using the correct Unity version (Unity 6).
4. Open the `Main` scene (found in `Assets/Scenes/`) to start.

## Development

### Architecture

The project follows a singleton manager pattern for core systems:
- `DataController`: Orchestrates save/load and global game state.
- `PanelController`: Manages the UI stack and menu transitions.
- `CombatController`: Handles space combat and object pooling.

### Save System

The save system is located in `Assets/Scripts/Systems/SaveSystem/`. It utilizes the `ISaveSystem` interface:

```csharp
public interface ISaveSystem
{
    void Save(string fileName, object data);
    T Load<T>(string fileName);
    bool SaveExists(string fileName);
    void DeleteSave(string fileName);
}
```

The default implementation is `JsonSaveSystem`, which uses `Newtonsoft.Json` for serialization.

## License

This project is for educational/preservation purposes. Original *Starflight* assets are property of their respective owners.
