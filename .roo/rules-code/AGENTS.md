# Project Coding Rules (Non-Obvious Only)

- Game state is managed by the `SpaceflightController` singleton. Access it via `SpaceflightController.m_instance`.
- Use the `JsonSaveSystem` for all data persistence. Do not use `PlayerPrefs` or other save mechanisms.
- The game's flow is controlled by a state machine in `SpaceflightController`. Use the `SwitchLocation` method to change game states.
- All new scripts should be placed in the appropriate subdirectory of `Assets/Scripts/`.
