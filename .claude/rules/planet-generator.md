---
paths:
  - "Assets/Scripts/Planet Generator/*.cs"
  - "Assets/Scripts/Spaceflight/Components/Planet.cs"
  - "Assets/Planet Generator/**"
  - "Assets/Resources/Planets/**"
---

# Planet generator

- The planet files are fixed data from the original game. `Starflight Remake/Planet Generator` (menu) regenerates `Assets/Resources/Planets/{planetId}.bytes` from the images in `Assets/Planet Generator/Data/`.
- `Process()` runs on the main thread and never waits for the task: it looks at `IsCompleted` each frame. Only the main thread changes the step and the abort flag; the task only writes the progress and its own buffers.
- A planet file that cannot be read (`ReadPlanetData` throws) fails the task, and `Process()` turns that into an abort of that one planet with an error in the log. An aborted planet has no maps and no elevation data: check `Planet.HasMaps()` before using its generator.
- Never let an exception out of `Process()`: `SpaceflightController.Update` calls it first thing every frame, so the game would stay paused for good.
