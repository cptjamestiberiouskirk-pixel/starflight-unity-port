# Starflight Unity Port

![Unity 6](https://img.shields.io/badge/Unity-6-blue.svg)
![Status: Active Development](https://img.shields.io/badge/Status-Active%20Development-green.svg)

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

### File Structure

```text
Assets/
├── Scripts/
│   ├── Persistent/       # Core engine managers
│   ├── Systems/
│   │   └── SaveSystem/   # Save system interfaces and implementations
│   ├── Game Data/        # Static game content
│   ├── Player Data/      # Save-game state
│   ├── Spaceflight/      # Game-world interaction and combat
│   └── Panel/            # UI implementation.
```

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

## Planet Rendering Architecture

The project features a deterministic visualization system that renders planet meshes and textures at runtime. It preserves the original Starflight planet data, using procedural techniques solely for visual representation rather than random world generation.

### Key Features
- **Unity Jobs & Burst**: Utilizes Unity's Job System and Burst Compiler for high-performance terrain generation.
- **Vertex Coloring**: Implements a custom coloring system using `TerrainJob` to assign biome colors based on height and temperature.
- **Rendering**: Uses `VertexColor.shader` to correctly render mesh vertex colors, ensuring planets look as intended.

### Testing
- A test asset `Assets/TestPlanet.asset` is available to verify the procedural generation and rendering settings.

## License

This project is for educational/preservation purposes. Original *Starflight* assets are property of their respective owners.
