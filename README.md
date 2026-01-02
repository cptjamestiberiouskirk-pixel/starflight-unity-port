# Starflight: The Remaking of a Legend (Unity 6 Port)

![Unity 6](https://img.shields.io/badge/Unity-6-blue.svg)
![Status: Active Development](https://img.shields.io/badge/Status-Active%20Development-green.svg)
![Platform](https://img.shields.io/badge/Platform-PC%20%7C%20Mac%20%7C%20Linux-blue)

A modern port of the 1986 classic space exploration game *Starflight*, built with Unity 6. This project faithfully recreates the features of the CGA and EGA versions while utilizing current technology.

---

## 🚀 2026 Modernization
The project has been refactored to a clean, **root-directory architecture** to eliminate legacy "zombie" assets and streamline development.

### Key Engineering Changes
- **Engine Upgrade**: Fully migrated to Unity 6 (6000.3.2f1).
- **Dependency Management**: Removed deprecated third-party libraries (`JsonDotNet`, legacy `FbxExporters`) in favor of official Unity Registry packages.
- **UI System**: Migrated to official Unity UI & TextMeshPro packages.
- **Input Handling**: Restored and configured the EventSystem to support modern input modules.

---

## 🎮 Features

- **Deterministic Exploration**: Navigate a fixed map of thousands of predetermined planets, preserving the exact coordinates and data of the 1986 original.
- **Data-Driven Architecture**: Uses JSON-based definitions for game assets and player progress via the `ISaveSystem`.
- **Combat System**: Full implementation of phaser/laser cannons, torpedo launchers, shield absorption models, and AI combat behaviors.
- **Terrain Scanning**: Enhanced exploration with 3D world-space labels, mineral detection, and "Star Trek-style" transporter effects.
- **Cargo Management**: Real-time volume tracking for minerals and elements with automatic transfer to the ship.

---

## 🛠 Getting Started

### Prerequisites
- **Unity 6 (6000.3.2f1 or later)**: This project utilizes Unity 6 features like `Awaitable`.
- **VS Code**: Recommended Editor with the "C# Dev Kit" installed.

### Installation
1. Clone the repository:
   ```bash
   git clone [https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git)
   ```
2. Open **Unity Hub** and click **Add Project from Disk**.
3. Select the **root folder** of the repository (where this README is located).
4. Open the `Intro` scene (found in `Assets/Scenes/`) to start.

---

## 📂 Project Structure

```text
Assets/
├── _MyCombatAssets/       # Custom combat audio, materials, and weapon VFX
├── 3rd Party/             # External plugins and legacy effect packages
├── Exported/              # Debug textures and terrain/heightmap exports
├── Fonts/                 # Global project typography
├── Game Objects/          # Prefab categories and 3D models
│   └── Aliens/            # High-fidelity alien assets (Thrynn, Spemin, etc.)
├── Music/                 # Game music and atmospheric tracks
├── Planet Generator/      # Deterministic generation data and editor-only tools
├── Resources/             # Runtime loadable assets (Required for deterministic system)
│   ├── Planets/           # Legacy binary planet data files (0.bytes - 810.bytes)
│   ├── Prefabs/           # Critical managers (PlanetManager, Atmosphere)
│   └── Starflight Game Data.json # Primary project configuration
├── Scenes/                # Game scenes (Intro, Spaceflight, Starport, Persistent)
├── Scripts/               # Core game logic and system architecture
│   ├── Game Data/         # C# Data Classes/Models (GD_Planet, GD_Star, GD_Vessel)
│   ├── Persistent/        # Global managers (DataController, SoundController)
│   └── Spaceflight/       # Combat, Navigation, and UI interaction logic
├── Shaders/               # Custom Unity 6 shaders (Atmosphere, Clouds, Skybox)
├── Shared/                # Common materials, global colors, and noise textures
├── Sounds/                # UI feedback and environmental sound effects
├── TextMesh Pro/          # TMP Font assets and style definitions
├── Tools/                 # Editor utilities and mass-generation tools
├── UI/                    # Bridge graphics and UI specific sprite assets
├── UI Toolkit/            # Unity 6 UI Toolkit themes and UXML documents
└── Unity Player/          # Build-specific assets (Icons and Splash screens)
```

---

## 🪐 Planet Rendering Architecture
The project features a **deterministic visualization system** that renders planet meshes and textures at runtime based on legacy data.

- **Preservation First**: It does NOT use random world generation. Procedural techniques (Jobs & Burst) translate original 8-bit heightmaps and biomes into modern 3D meshes.
- **Unity Jobs & Burst**: High-performance terrain generation ensures smooth planetary landings.
- **Vertex Coloring**: A custom `TerrainJob` assigns biome colors based on original fixed height and temperature data.

---

## 👽 Alien Asset Status
- **Thrynn.fbx**: Currently ~74MB. Requires manual extraction of materials and potential polygon reduction to optimize performance.

---

> *Original concept by the BraveArmy team. Unity 6 Port and Modernization by @cptjamestiberiouskirk-pixel.*