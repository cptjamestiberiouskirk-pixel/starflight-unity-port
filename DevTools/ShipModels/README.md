# Alien ship models

The original game's ship pictures exist only as sensor silhouettes, and the project's hand-made models cover the Spemin, the Mechan Scout and the Mysterion. The Thrynn ships are textured models made with Meshy (see "Meshy models" below). The other vessels of the game data are built here by Blender Python scripts: low-poly stand-ins shaped after the sensor pictures in `Research/Screenshots/Ships`, not hand-made art. Nothing is random at build time (the few lumps and spines come from fixed seeds), so a rebuild gives the same meshes.

Rulings of the project owner (2026-10-09):
- **Size**: STRINFO section 7 gives each vessel's size against an Interstel ship (it is the vessel's mass over 500 tons; the Thrynn Transport, missing from STRINFO, is 1.2 by that rule). A size up to 4 is used as it is, with a floor of 0.3 so that the smallest ships stay visible; above 4 it is compressed to `4 + 0.77 * log2(size / 4)`, which reproduces the existing Mysterion (124, shown at about 7.8). One size is the player's Arth Ship, 51.06 units long (3.404 in its FBX, scale 15).
- One script per race, a review sheet for each before it goes into the project, one pull request per race.

## Files

| File | What it is |
|---|---|
| `ship_kit.py` | Shared helpers: shapes (lofts, ellipsoids, plates, ring sectors, lathes, beams, spikes, lumpy blobs), materials, the size rule, FBX export, the review sheet |
| `build_<race>.py` | One race: its materials and one function per vessel, in the units of its sensor picture |
| `manifest-<race>.json` | Written by a build: each vessel's id, length, model and debris paths, and its materials |
| `ShipModelWiring.cs`, `wire.ps1` | The Unity side, run headless: materials, imports and the scene's template slots, from a manifest |
| `previews/<race>.png` | The review sheets: the original's screenshot, then top, side and three-quarter views (nose to the left) |
| `import_meshy.py`, `meshy-<race>.json` | A race made with Meshy instead of a build script: see "Meshy models" below |

## Building a race

From the project root (a worktree; the Unity project must not be open in an Editor for the last step):

```bash
blender --background --factory-startup --python DevTools/ShipModels/build_veloxi.py -- --preview DevTools/ShipModels/previews/veloxi.png
blender --background --factory-startup --python process_ship_debris.py -- -s 1986 --suffix "" --batch "Assets/Game Objects/Ships/Veloxi" "Assets/Game Objects/Ships Debris/Veloxi"
```

```powershell
& "DevTools\ShipModels\wire.ps1" -Manifest "DevTools\ShipModels\manifest-veloxi.json"
```

The build writes `Assets/Game Objects/Ships/<Race>/<Vessel>.fbx` (nose along Unity +Z, final size, scale 1) and the manifest; `--no-export` renders the sheet only. The debris script writes a damaged copy of every model with the same file name. `wire.ps1` makes an SF - Standard material per manifest material (a copy of the Spemin hull material with the maps switched off), remaps the FBX materials to them, puts an inactive instance of each model under the other model templates of the Encounter location (layer Encounter), and points the vessel's slots of `m_alienShipModelTemplate` and `m_alienShipDebrisTemplate` at the instance and the debris model. Running it again replaces what an earlier run made; it refuses to replace a hand-made object of the same name.

Then add the vessel lengths of the manifest to `c_shipModelLengths` in `DevTools/HeadlessProbe/ClaudeProbe.cs` and run the `shipmodels` probe scenario: it checks that every vessel gets its model, its length, that the model is longest along its nose and has its engines (a material named `... Engine`) at the back, and the size and orientation of its debris. It has no graphics: look at each ship in the GUI Editor as well.

## Meshy models

The owner's direction of 2026-10-13 is to remake the game's models with Meshy (meshy.ai, a paid plan: its API creates no task for a free account) wherever it fits. The Thrynn ships are the first race done this way. For each vessel:

1. **Concept picture**: Meshy image-to-image (`nano-banana-pro`, 9 credits) from the original's sensor picture (cropped from `previews/<race>.png` or `Research/Screenshots/Ships`) and its silhouette (`Assets/Game Objects/UI/Sensors`), for the owner to approve. A third reference picture of another ship of the race keeps the materials alike, but it also brings its layout along (the Thrynn scout kept the warship's four pylons twice); an image edit of a near miss is the cheaper fix.
2. **Model**: image-to-3d from the approved picture (standard, textured, 4K PBR maps, 30 credits), then a remesh to about 10,000 triangles (5 credits). The remesh keeps the textures: it bakes them, the normal map too, from the full model of about 1.5 million triangles.
3. **`meshy-<race>.json`**: each vessel's id, name, STRINFO size, GLB file name, which way its nose points after the glTF import (`-X` for Meshy's models so far: the review sheet shows every nose to the left) and the Meshy tasks it came from. The GLB files (about 48 MB each) stay outside the repository; a task's files can be downloaded again with its id.
4. **`import_meshy.py`**: turns and scales each model as the build scripts do, gives the faces at the back that face backwards an engine material (for the probe's orientation check, and a glow on the nozzles), converts Meshy's metallic/roughness maps to the albedo and specular/smoothness maps of SF - Standard, makes an emissive map of the blue and cyan crystal, and writes the FBX, the maps (`Assets/Game Objects/Ships/<Race>/Textures`, 1024 pixels: JPEG, and the specular map as a PNG at 512 because of its alpha) and the manifest. Maps of about 1.4 MB and a model of about 0.6 MB a vessel.
5. The debris script, `wire.ps1` and the probe as above. `ShipModelWiring.cs` puts the manifest's maps on the materials.

```bash
blender --background --factory-startup --python DevTools/ShipModels/import_meshy.py -- DevTools/ShipModels/meshy-thrynn.json --source <folder with the GLB files> --preview DevTools/ShipModels/previews/thrynn.png
```

## Conventions

- A vessel is built in a frame with the nose along +Y and up along +Z; `build_race` turns it to the frame the FBX export expects (nose along Blender -Y, as in the existing models).
- Material names start with the race. A material named `<Race> Engine` marks the engines for the probe's orientation check; red caps at the front of a nacelle are not engines (the Enterprise calls them `Bussard`).
