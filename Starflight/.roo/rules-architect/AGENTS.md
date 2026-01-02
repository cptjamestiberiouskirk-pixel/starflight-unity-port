# Project Architecture Rules (Non-Obvious Only)

- The `SpaceflightController` is the central hub of the game's architecture. All major systems are accessed through this class.
- The game's state is managed by a simple state machine in `SpaceflightController`. Avoid complex state management patterns.
- Data persistence is handled by a simple JSON-based save system. There is no need for a more complex database.
- The game is divided into a small number of scenes. Avoid creating new scenes unless absolutely necessary.