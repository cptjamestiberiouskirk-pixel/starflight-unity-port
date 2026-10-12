# The 3D star system view

The main view of the star system location (`StarSystem`) shows the ship from a camera 1024 units above it, with the sun, the planets and the sky around it. On 2026-10-12 the project owner chose look B, "Realistic", from mock-up sheets, with all of its parts. They come in separate changes; this folder keeps the mock-ups and the tools.

The parts:
- **Planets lit by their star** (done): each planet is lit from its own star, in the star's colour, with a day side and a night side, and its atmosphere glows on the sunlit edge. The light of the star is a directional light that `StarSystem` makes in `Awake` ("Starlight"). It lights only the star system's layer (the planets and their atmospheres), shines from the middle of the star in the plane of the orbits toward the ship, and takes the colour of real stars of the star's class (`SystemDisplay.GetStarColor`, the same as the system map and the sun's shine). The planets in view are always near the ship (they are thousands of units apart), so one light serves them all. The scene's "Sunlight" still lights the ship from high above the star (its look at constraint aims it at the ship from 2048 above the star), as before, so that the ship can be seen from the camera; it no longer lights the star system's layer. The atmosphere shader takes the sun's direction from the main directional light, so it follows the starlight without a change.
- **A new sky** (done): a faint Milky Way crosses the sky under the territory's nebula, which stays as the cue to whose space the ship is in (blue for human space, green for the Elowan, red for the Uhlek, and so on). It is a cubemap (`Assets/Game Objects/Skyboxes/Milky Way/milky_way.png`, imported from its latitude and longitude layout) that the skybox shader adds by the direction of each pixel, so it turns with the sky as the ship flies, as the nebula does. Its strength is the skybox controller's `m_milkyWayStrength`: 0.035 in the Spaceflight scene, none in the Starport, which uses the same skybox material. The stars of several colours, sizes and brightnesses were already there (#232). The "Regen Starfield" debug button is gone from the main view.
- **Orbit paths**: a faint path along each planet's orbit, brighter just behind the planet, and a small blue glow at the ship's engines.

Not chosen: look C, "Cinematic" (bloom on bright edges, a soft flare from the star's direction, a richer sky).

## Files

| File | What it is |
|---|---|
| `mockup_system_view.py` | Blender 5.2 script (with numpy) that rendered the mock-ups: the planets of one star system at the game's framing, lit from their star, composited over a new sky with the orbit path and the ship from an engine capture |
| `previews/system-view-mockups.png` | The sheet the owner chose from: A the game before, B Realistic, C Cinematic, beside the gas giant (orbit 4), a liquid planet (orbit 5) and a molten planet (orbit 1) of star 156 |
| `previews/liquid-planet-realistic.png`, `liquid-planet-cinematic.png` | B and C beside the liquid planet at full size |
| `previews/starlight-before-after.png` | The planets lit by their star in the engine: before (top) and after (bottom), in star 156 and Arth's system |
| `build_sky_art.py` | Blender 5.2 script (with numpy) that makes the Milky Way: a 4096 x 2048 latitude and longitude picture worked out on directions with 3D noise (no seam, nothing pinched at the poles), the band through straight down at 28 degrees from the screen's up, with star clouds, dust lanes and a warm bulge; with a second path it also writes what the camera sees straight down |
| `SkyWiring.cs`, `wire.ps1` | The Unity side, run headless: the cubemap import, the skybox material (the Milky Way with no strength of its own), the Spaceflight scene's strength, and the removal of the Regen Starfield button |
| `previews/milky-way-art.png` | The Milky Way picture, and what the camera sees of it straight down at full strength |
| `previews/sky.png` | The new sky in the engine: beside Arth's sun and frozen planet, and beside two planets of star 156 (the sky turned back to where it starts) |
| `previews/sky-other-views.png` | Hyperspace, an encounter, orbit and the Starport with the new sky (the Starport shows none of it) |

## Pictures from the engine

The probe scenario `systemviewpictures` checks the light of the star and, with a graphics device, renders the view beside the sun, beside each planet and from high above the sun, in Arth's system and star 156:

```powershell
& "DevTools\HeadlessProbe\probe.ps1" -Scenario systemviewpictures -Graphics
```

The pictures go into `Logs/screen-pictures/system-view/`.

## Building the sky

From the project root (a worktree; the Unity project must not be open in an Editor for the second step):

```bash
blender --background --factory-startup --python DevTools/SystemView/build_sky_art.py
```

```powershell
& "DevTools\SystemView\wire.ps1"
```

## The mock-ups

```bash
blender --background --factory-startup --python DevTools/SystemView/mockup_system_view.py -- "Assets/Resources/Starflight Game Data.json" <maps dir> Logs/screen-pictures/system-view <out dir> 156 4 5 1
```

`<maps dir>` holds the planets' albedo maps as `star156_orbit<n>_planet<id>_<w>x<h>.png`, exported from the planet generator by a probe scenario that was not kept. Use absolute paths: Blender does not find relative image paths. The framing was measured from engine captures: the ship is at (640, 540) of the 1920 x 1080 screen, the camera reads as about 33.7 degrees high (the scene says 30), and a planet's sphere radius is its `GD_Planet.GetScale()`. With a vertical sensor fit, Blender's `shift_x` is in units of the image height.
