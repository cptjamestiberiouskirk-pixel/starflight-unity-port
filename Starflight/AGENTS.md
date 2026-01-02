# AGENTS.md

This file provides guidance to agents when working with code in this repository.

## Stack

- **Engine**: Unity 6000.3.2f1
- **Language**: C#

## Build/Run

- Use the Unity Editor to build and run the project.
- There are no custom build scripts.

## Code Style

- **Naming**: `camelCase` for private fields (with an `m_` prefix), `PascalCase` for public fields, methods, and classes.
- **Braces**: Opening braces on a new line.
- **Spacing**: Consistent use of spaces around operators.

## Architecture

- **Singleton Pattern**: The `SpaceflightController` class uses a static instance (`m_instance`) for global access.
- **State Machine**: The game's flow is managed by a state machine in `SpaceflightController`, using the `PD_General.Location` enum.
- **Data Persistence**: Game data is saved and loaded using the `JsonSaveSystem` class, which serializes data to JSON files in `Application.persistentDataPath`.
- **Scene Management**: The game is divided into multiple scenes, managed by the `SceneManager`.

## Testing

- There is no formal testing setup in this project. All new code should be manually tested.
