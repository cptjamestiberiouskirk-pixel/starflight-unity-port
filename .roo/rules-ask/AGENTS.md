# Project Documentation Rules (Non-Obvious Only)

- The core game logic is in `Assets/Scripts/Spaceflight/SpaceflightController.cs`.
- The game's state machine is defined by the `PD_General.Location` enum in `SpaceflightController.cs`.
- Data persistence is handled by the `JsonSaveSystem` in `Assets/Scripts/Systems/SaveSystem/`.
- The game is divided into multiple scenes, located in the `Assets/Scenes/` directory.
