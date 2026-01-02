# Project Debug Rules (Non-Obvious Only)

- The game's state can be monitored by inspecting the `SpaceflightController` in the Unity Editor.
- Save files are located in `Application.persistentDataPath`. Check these files to debug data persistence issues.
- The game uses a custom scene loading system. Use the `DataController` to manage scene transitions.
