# The 3D star system view

The main view of the star system location (`StarSystem`) shows the ship from a camera 1024 units above it, with the sun, the planets and the sky around it. On 2026-10-12 the project owner chose look B, "Realistic", from mock-up sheets, with all of its parts. They come in separate changes; this folder keeps the mock-ups and the tools.

The parts:
- **Planets lit by their star** (done): each planet is lit from its own star, in the star's colour, with a day side and a night side, and its atmosphere glows on the sunlit edge. The light of the star is a directional light that `StarSystem` makes in `Awake` ("Starlight"). It lights only the star system's layer (the planets and their atmospheres), shines from the middle of the star in the plane of the orbits toward the ship, and takes the colour of real stars of the star's class (`SystemDisplay.GetStarColor`, the same as the system map and the sun's shine). The planets in view are always near the ship (they are thousands of units apart), so one light serves them all. The scene's "Sunlight" still lights the ship from high above the star (its look at constraint aims it at the ship from 2048 above the star), as before, so that the ship can be seen from the camera; it no longer lights the star system's layer. The atmosphere shader takes the sun's direction from the main directional light, so it follows the starlight without a change.
- **A new sky**: a faint Milky Way band, faint nebula haze and stars of several colours in place of the plain dot field.
- **Orbit paths**: a faint path along each planet's orbit, brighter just behind the planet, and a small blue glow at the ship's engines.
- **No debug button**: the "Regen Starfield" button is taken off the main view.

Not chosen: look C, "Cinematic" (bloom on bright edges, a soft flare from the star's direction, a richer sky).

## Files

| File | What it is |
|---|---|
| `mockup_system_view.py` | Blender 5.2 script (with numpy) that rendered the mock-ups: the planets of one star system at the game's framing, lit from their star, composited over a new sky with the orbit path and the ship from an engine capture |
| `previews/system-view-mockups.png` | The sheet the owner chose from: A the game before, B Realistic, C Cinematic, beside the gas giant (orbit 4), a liquid planet (orbit 5) and a molten planet (orbit 1) of star 156 |
| `previews/liquid-planet-realistic.png`, `liquid-planet-cinematic.png` | B and C beside the liquid planet at full size |
| `previews/starlight-before-after.png` | The planets lit by their star in the engine: before (top) and after (bottom), in star 156 and Arth's system |

## Pictures from the engine

The probe scenario `systemviewpictures` checks the light of the star and, with a graphics device, renders the view beside the sun, beside each planet and from high above the sun, in Arth's system and star 156:

```powershell
& "DevTools\HeadlessProbe\probe.ps1" -Scenario systemviewpictures -Graphics
```

The pictures go into `Logs/screen-pictures/system-view/`.

## The mock-ups

```bash
blender --background --factory-startup --python DevTools/SystemView/mockup_system_view.py -- "Assets/Resources/Starflight Game Data.json" <maps dir> Logs/screen-pictures/system-view <out dir> 156 4 5 1
```

`<maps dir>` holds the planets' albedo maps as `star156_orbit<n>_planet<id>_<w>x<h>.png`, exported from the planet generator by a probe scenario that was not kept. Use absolute paths: Blender does not find relative image paths. The framing was measured from engine captures: the ship is at (640, 540) of the 1920 x 1080 screen, the camera reads as about 33.7 degrees high (the scene says 30), and a planet's sphere radius is its `GD_Planet.GetScale()`. With a vertical sensor fit, Blender's `shift_x` is in units of the image height.
