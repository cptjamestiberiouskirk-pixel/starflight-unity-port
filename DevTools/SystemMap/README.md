# The console's system map

The System Map display of the ship console (`SystemDisplay`) shows the current star system from above: the star, the orbits, the planets and the ship. It used to draw them as flat coloured dots on black. On 2026-10-11 the project owner chose a realistic look from preview sheets, with the colours of real stars for the star (not the original's class colours) and the planets sized by their mass. Only the look changed: the orbits, the positions of the planets and the ship, and every value of the game data are the same.

What the map draws now:
- **Planets**: each planet's own albedo map (the one the planet generator makes when the ship enters the system) on a sphere lit from the star, with a day side and a night side, a darker limb and the planet's atmosphere on the lit rim (its colour from `GD_Planet.GetAtmosphereColor`, as the star system shows it). The images stay upright as the orbits turn, and the planets turn slowly. Until the maps are made a planet is a lit ball in the colour of its surface. The size follows the mass, 18 to 34 map units, on the curve of `GD_Planet.GetScale`.
- **Star**: a white-hot core, a corona and four short rays in the colour of real stars of its class (`SystemDisplay.GetStarColor`), with a slow shimmer. The planets take a little of that colour in their light. The sun of the 3D star system view takes the same colour (`StarSystem.Show`): its shine is nearly white in the middle and a deeper shade of that colour at the rim (`Assets/Shaders/MaybeConvert/Shine.shader`).
- **Orbits**: a thin ring on each orbit that has a planet, brighter in a trail behind its planet, so that the way the planets go round shows.
- **Ecosphere**: a faint band from half way inside the first orbit of the spectral class's ecosphere to half way outside its last.
- **Background**: a star field with a faint nebula, a different part of it for each star.
- **Ship**: a chevron with a glow and a dotted line ahead of it, turned to the ship's heading. Its engine glow pulses and grows brighter with its speed, it leaves a wake of fading segments along its last positions on the map, and every 4 seconds a sensor ping, a thin ring, grows from where it was and fades. None of it moves while the game is paused, and none of it shows in orbit.
- **Orbit trails**: the trails shimmer gently toward their planets.

The display is 512 x 512 map units (1 unit is 1 pixel at 1920 x 1080).

## Files

| File | What it is |
|---|---|
| `Assets/Shaders/System Map Planet.shader` | The planets: a sphere from the albedo map, lit from the direction `SystemDisplay` sets every frame |
| `Assets/Shaders/System Map Star.shader` | The star, drawn additively, with no texture |
| `Assets/Shaders/System Map Orbit.shader` | A ring and its trail, with no texture |
| `Assets/Shaders/System Map Ecosphere.shader` | The ecosphere band, drawn additively, with no texture |
| `build_system_map_art.py` | Blender 5.2 script that makes `System Map Star Field.png` (1024 x 1024, tiles seamlessly), `System Map Ship.png`, and the white sprites `System Map Glow.png` (the engine glow) and `System Map Wake.png` (a wake segment) in `Assets/Game Objects/Display/System Map` |
| `SystemMapWiring.cs`, `wire.ps1` | The Unity side, run headless: the import of the two textures, the four materials, and the map's objects in `Spaceflight.unity` |
| `previews/system-map.png` | The map in the game before and after, rendered by the engine: Arth's system (class G) old and new, then star 156 (class A) old and new |
| `previews/sun.png` | The sun of the star system view before and after, rendered by the engine, in the same order |
| `previews/system-map-animations.mp4`, `.png` | Four seconds of the map in the engine, 20 frames a second, as the ship flies round Arth's sun (the first ping goes out where the ship came in), and four frames of it |

## Building

From the project root (a worktree; the Unity project must not be open in an Editor for the second step):

```bash
blender --background --factory-startup --python DevTools/SystemMap/build_system_map_art.py
```

```powershell
& "DevTools\SystemMap\wire.ps1"
```

The build writes the two textures; nothing is random at build time (fixed seeds), so a rebuild gives the same pictures. `wire.ps1` sets up their import (the star field repeats, the ship is a sprite that turns about the middle of its chevron), makes the four materials (a material made for another shader is made again), puts the star field and the ecosphere behind the map, replaces each orbit's ring mesh with a ring image, gives the star, the planets and the ship marker their materials and sprite, and points `SystemDisplay.m_ringList`, `m_ecosphere` and `m_background` at the new objects. Running it again gives the same scene.

Then run the `systemmap` probe scenario. It has no graphics: look at the map in the GUI Editor as well.

## Conventions

- The project renders in Linear colour space. The planets' light is worked out in linear space, as in a lit material. The star, the rings and the ecosphere are light added on the screen: their shaders work them out in gamma space (their colour properties go back to gamma space first) and convert the result, or a glow that is faint on the screen comes out as a grey haze.
- An `Image` binds its own texture to `_MainTex`, so the planet shader reads the albedo map from `_PlanetTex`.
- The star field stops 2 map units inside the display, so that the rounded corners and the soft edge of the console's screen stay.
- The map is clipped to its display (`RectMask2D` on its root): the ship comes into a system at its edge, and the first sensor ping would spill over the console. The four shaders handle the clip rect.
- The engine glow is a sibling drawn just before the ship marker, not its child: a child is always drawn over its parent. The animations fade through `CanvasRenderer.SetAlpha`, which needs no rebuild of the image, and run on a clock of their own that stops while the game is paused.
- The textures of a planet generator are freed by `Planet` once the next planet's maps are shown. `SystemDisplay.ChangeSystem` takes the planets off their maps when a new system begins, and `ShowPlanetMaps` (called by `StarSystem.GeneratePlanets` when every planet has its maps) puts the new ones on, so the map never holds a freed texture.
