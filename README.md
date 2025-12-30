# Starflight: The Remaking of a Legend

![Unity Version](https://img.shields.io/badge/Unity-6000.3.2f1-black?style=flat&logo=unity)
![Status](https://img.shields.io/badge/Status-Ported_&_Playable-success)
![Platform](https://img.shields.io/badge/Platform-PC%20%7C%20Mac%20%7C%20Linux-blue)

This project's goal is to remaster **Starflight**, taking inspiration from the classic CGA and EGA versions of the game. The finished game aims to faithfully recreate the features of those versions within a modern engine, preserving the original spirit while utilizing current technology.

The "Research" folder contains information about the original CGA and EGA versions of the game and serves as the primary reference for this recreation.

---

## 🚀 2025 Unity 6 Port
**Current Status:** *Ported & Functional*

This project has been modernized and ported from Unity 2019.4 to **Unity 6 (6000.3.2f1)** by **[@cptjamestiberiouskirk-pixel](https://github.com/cptjamestiberiouskirk-pixel)**.

The codebase has been refactored to utilize modern Unity architecture, removing dependency on legacy "zombie" assets in favor of official Unity Registry packages.

### Key Engineering Changes
* **Engine Upgrade:** Migrated fully to Unity 6 (6000.3.2f1).
* **Dependency Management:** Removed deprecated third-party libraries (`JsonDotNet`, legacy `FbxExporters`).
* **UI System:** Migrated from legacy TextMesh Pro plugins to the official **Unity UI & TextMeshPro** packages.
* **Input Handling:** Restored and configured the EventSystem to support modern input modules.
* **Stability:** Resolved persistent layout crashes ("Texture" layout corruption) and cleared deprecated Editor preferences.

---

## 🛠 Getting Started

We are using **Unity 6** with a clean dependency list managed via the Unity Package Manager. No external manual downloads are required.

### Prerequisites
1.  Download **Unity Hub** from the [Unity Website](https://unity.com/).
2.  Install Unity Editor version **6000.3.2f1** (or newer).

### Installation
1.  Clone this repository:
    ```bash
    git clone [https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git)
    ```
2.  Open **Unity Hub** and click **Add Project from Disk**.
3.  Select the `Starflight` folder.
4.  Open the project.
    * *Note: The first launch may take time as Unity re-imports the Asset Database.*

### Playing the Game
1.  Open the **Project** window in Unity.
2.  Navigate to `Assets/Scenes`.
3.  Open the **`Intro`** scene.
4.  Press **Play**.

---

## 🤝 Contributions

Contributions are welcome! If you find bugs related to the Unity 6 migration (such as UI artifacts or shader glitches), please feel free to open an Issue or a Pull Request.

### Known Issues
* **Solar System View:** Minor text artifacting (z-fighting) on the status display.

---

> *Original concept and initial development by the BraveArmy team.*
>
> *Unity 6 Port and Modernization by [@cptjamestiberiouskirk-pixel](https://github.com/cptjamestiberiouskirk-pixel).*
