# Starflight: The Remaking of a Legend (Unity 6 Port)

![Unity 6](https://img.shields.io/badge/Unity-6-blue.svg)
![Status: Active Development](https://img.shields.io/badge/Status-Active%20Development-green.svg)
![Platform](https://img.shields.io/badge/Platform-PC%20%7C%20Mac%20%7C%20Linux-blue)

A modern port of the 1986 classic space exploration game *Starflight*, built with Unity 6. This project faithfully recreates the features of the CGA and EGA versions while utilizing current technology.

---

## 🚀 2026 Modernization
The project has been refactored to a clean, **root-directory architecture** to eliminate legacy "zombie" assets and streamline development.

### Key Engineering Changes
- **Engine Upgrade**: Fully migrated to Unity 6 (6000.3.13f1).
- **Dependency Management**: Removed deprecated third-party libraries (`JsonDotNet`, legacy `FbxExporters`) in favor of official Unity Registry packages.
- **UI System**: Migrated to official Unity UI & TextMeshPro packages.
- **Input Handling**: Restored and configured the EventSystem to support modern input modules.

---

## 🎮 Features

- **Deterministic Exploration**: Navigate a fixed map of **270 star systems and approximately 800 predetermined planets**, preserving the exact coordinates and data of the 1986 original.
- **Data-Driven Architecture**: Uses JSON-based definitions for game assets and player progress via the `ISaveSystem`.
- **Combat System**: Laser cannons, missile launchers, shield absorption, armor damage, and AI combat behaviors with victory/defeat detection. Alien ships have armor and shield points, any race turns hostile when fired on (the Uhlek attack on sight), aliens hold their fire while a comm link is up, and every shot uses a little Endurium.
- **Encounters in Orbit**: Home fleets, drones and a derelict wait in orbit around their planets and meet the ship when it goes into orbit there. The ship drops into the encounter and comes back at the level of the star system, as in the original.
- **Ship Systems Over Time**: Shields keep their charge and recharge slowly, and the engineer's repairs and the doctor's treatment take time, faster with higher skill.
- **Ship Destruction**: Complete death sequences with explosion effects, debris spawning, and salvage opportunities.
- **Game Over System**: Player ship destruction triggers the game over screen. Returning to the title screen goes back to the last save.
- **Terrain Scanning**: Enhanced exploration with 3D world-space labels, mineral detection, and Star Trek-style transporter effects.
- **Cargo Management**: Real-time volume tracking for minerals and elements with automatic transfer to the ship. A mineral deposit holds 1 to 5 cubic meters.
- **Original Economy in Builds**: A new game in a build starts with the original 12,000 MU (the Editor keeps 1,000,000 for testing), and the bank and the notices show stardates as the original did (`26-03-4620`).
- **Debris Scanning**: Scan destroyed vessel wreckage for salvage analysis.

---

## 🛠 Getting Started

### Prerequisites
- **Unity 6 (6000.3.13f1 or later)**: This project utilizes Unity 6 features like `Awaitable`.
- **VS Code**: Recommended Editor with the "C# Dev Kit" installed.

### Installation
1. Clone the repository:
   ```bash
   git clone [https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git)
   ```
2. Open **Unity Hub** and click **Add Project from Disk**.
3. Select the **root folder** of the repository (where this README is located).
4. Open the `Intro` scene (found in `Assets/Scenes/`) to start.

### Continuous Integration
Every pull request and push to `master` runs a headless Unity compile check (`.github/workflows/compile-check.yml`, via [GameCI](https://game.ci)). It needs repo secrets under **Settings > Secrets and variables > Actions**: `UNITY_EMAIL` and `UNITY_PASSWORD`, plus either `UNITY_SERIAL` (Pro or Student plan serial number) or `UNITY_LICENSE` (Personal plan: the contents of `Unity_lic.ulf` after activating in Unity Hub; `UnityEntitlementLicense.xml` is a different format and won't work). See the [GameCI activation guide](https://game.ci/docs/github/activation). To check a different editor before upgrading, run the workflow by hand from the **Actions** tab and enter a `unityVersion`.

### Local Checks
`DevTools/HeadlessProbe/` holds a headless compile check and a play-mode probe for Windows. The probe runs the real Spaceflight or Starport scene in batch mode with an in-memory save system and checks game behaviour through scenarios, one for every fix made since 2026-10-03. It sits outside `Assets/`, so it is not part of the build. See its [README](DevTools/HeadlessProbe/README.md).

### AI Orchestration (optional)
`Assets/Scripts/AI/` runs a [Semantic Kernel](https://github.com/microsoft/semantic-kernel) inside the game that talks to Claude through the Claude Code command line (`claude -p`), so it uses the plan you are signed in with in Claude Code instead of an API key. Each request starts a `claude` process and takes a few seconds, so it suits world events and tooling, not anything per frame, and it only works on a machine with Claude Code installed and signed in.

1. Open the project. NuGetForUnity (in `Packages/manifest.json`) restores `Microsoft.SemanticKernel.Core` and its dependencies from `Assets/packages.config` into `Assets/Packages/`, and `SemanticKernelDefineSync` then adds the `STARFLIGHT_SEMANTIC_KERNEL` define, which the `Starflight.AI` assemblies need to compile. Until then the AI code is left out and the rest of the project compiles as before. Commit `Assets/Packages/`, `Assets/packages.config` and `ProjectSettings/ProjectSettings.asset` together: a define without the DLLs fails the CI compile.
2. **Starflight Remake > AI > Add Semantic Orchestrator To Active Scene** adds a `SemanticOrchestrator` object with a `MainThreadDispatcher` and a `MetagameOrchestrator`. In play mode, **Trigger World Simulation** on the orchestrator's context menu asks Claude to simulate a border dispute (Thrynn and Elowan by default). Claude calls the `WorldState-generate_faction_event` kernel function, which decides the outcome and logs it, then narrates it.
3. The 3D crate comes from `DevTools/Blender/make_scifi_crate.py` (run it in Blender, from a shell or through the Blender MCP server; it writes `Assets/Models/SciFiCrate.fbx`). **Starflight Remake > AI > Import SciFi Crate Into Active Scene** then makes `Assets/Models/SciFiCrate.prefab` and places it at the origin. `Assets/UI/EventLogBg.svg` is a background for an event log panel.

---

## 📂 Project Structure

```text
Assets/
├── _MyCombatAssets/       # Custom combat audio, materials, and weapon VFX
├── 3rd Party/             # External plugins and legacy effect packages
├── Exported/              # Debug textures and terrain/heightmap exports
├── Fonts/                 # Project fonts
├── Game Objects/          # Prefab categories and 3D models
│   └── Aliens/            # High-fidelity alien assets (Thrynn, Spemin, etc.)
├── Music/                 # Game music and atmospheric tracks
├── Planet Generator/      # Deterministic generation data and editor-only tools
├── Resources/             # Runtime loadable assets (Required for deterministic system)
│   ├── Planets/           # Legacy binary data for ~800 fixed planets (0.bytes - 810.bytes)
│   ├── Prefabs/           # Critical managers (PlanetManager, Atmosphere)
│   └── Starflight Game Data.json # Primary project configuration
├── Scenes/                # Game scenes (Intro, Spaceflight, Starport, Persistent)
├── Scripts/               # Core game logic and system architecture
│   ├── AI/                # Optional Semantic Kernel orchestration through the claude command line
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
- **Alien ship models**: the Spemin ships, the Mechan Scout and the Mysterion are hand-made models. The Veloxi and Thrynn ships are procedural low-poly stand-ins built by the Blender scripts in `DevTools/ShipModels` (see its [README](DevTools/ShipModels/README.md)), shaped after the original's sensor pictures and sized after STRINFO. The other vessels still use a placeholder until their stand-ins are in.
- **Thrynn.fbx**: Currently ~74MB. Requires manual extraction of materials and potential polygon reduction to optimize performance.

---

## 🎯 Current Development Status (v0.9.0)

### ✅ Completed Features
- Full starport operations (Personnel, Ship Config, Trading, Banking)
- Hyperspace navigation and star system exploration
- Planetary landing and terrain vehicle exploration
- 10 alien races with unique encounter behaviors
- Encounters that guard planets from orbit; a Veloxi drone grants permission to orbit to a crew that answers its numbers the Veloxi way
- Complete combat system with lasers, missiles, shields, armor
- Ship destruction with debris spawning
- Game over and restart functionality
- Save/load system with JSON persistence

### 🔧 In Progress
- Salvage collection from debris fields
- Alien ship models for the Elowan, Uhlek, Gazurtoid, Noah 9 and the Enterprise (procedural stand-ins, one race at a time)

### 📊 Completion: ~95%

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

---

> *Original concept by the BraveArmy team. Unity 6 Port and Modernization by @cptjamestiberiouskirk-pixel.*