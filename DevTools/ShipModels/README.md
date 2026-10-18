# Alien ship models

The original game shows its ships only as sensor silhouettes. The project's hand-made models cover the Spemin, the Mechan Scout and the Mysterion. Every other vessel in the game data (the Thrynn, Elowan, Veloxi, Gazurtoid and Uhlek ships, the Noah 9 derelict and the Enterprise) and the player's Arth ship is a textured model (see "Textured models" below). These replace the low-poly stand-ins that Blender Python scripts (`build_<race>.py`) built between 2026-10-09 and 2026-10-14, which have been removed; `ship_kit.py`, the library they shared, still provides the size rule, the review sheet and the FBX export.

Decisions of 2026-10-09:
- **Size**: STRINFO section 7 gives each vessel's size relative to an Interstel ship (the vessel's mass divided by 500 tons; the Thrynn Transport, which STRINFO omits, comes to 1.2 by that rule). A size up to 4 is used as it is, with a floor of 0.3 so the smallest ships stay visible; above 4 it is compressed to `4 + 0.77 * log2(size / 4)`, which reproduces the existing Mysterion (124, shown at about 7.8). One size unit is the player's Arth Ship, 51.06 units long (3.404 in its FBX, scale 15).
- **Review**: each race gets a review sheet before it goes into the project, with one pull request per race.

## Files

| File | What it is |
|---|---|
| `ship_kit.py` | Shared helpers: the size rule, FBX export, the review sheet (plus the shapes the procedural stand-ins were built from: lofts, ellipsoids, plates, ring sectors, lathes, beams, spikes, lumpy blobs) |
| `manifest-<race>.json` | Written by an import: each vessel's id, length, model and debris paths, and its materials and maps |
| `ShipModelWiring.cs`, `wire.ps1` | The Unity side, run headless: materials, imports and the scene's template slots, driven by a manifest |
| `previews/<race>.png` | The review sheets: the original game's screenshot, then top, side and three-quarter views (nose to the left) |
| `import_meshy.py`, `meshy-<race>.json` | Imports a race's textured models: see "Textured models" below |
| `import_meshy_arth.py`, `meshy-arth.json` | Imports the player's Arth ship, with its equipment parts kept separate: see "The player's Arth ship" below |
| `import_meshy_tv.py`, `meshy-terrain-vehicle.json` | Imports the terrain vehicle, its six wheels kept separate: see "The terrain vehicle" below |
| `import_meshy_props.py`, `meshy-ruins.json`, `meshy-rocks.json` | Imports single-mesh ground props (the ancient ruins, the rocks and the two deposits with shapes of their own): see "Ground props" below |

## Wrecks, wiring and the probe

Once a race has been imported (see "Textured models" below), run these from the project root (a worktree; the Unity project must not be open in an Editor for the last step):

```bash
blender --background --factory-startup --python process_ship_debris.py -- -s 1986 --suffix "" --batch "Assets/Game Objects/Ships/Noah 9" "Assets/Game Objects/Ships Debris/Noah 9"
```

```powershell
& "DevTools\ShipModels\wire.ps1" -Manifest "DevTools\ShipModels\manifest-noah-9.json"
```

The import writes `Assets/Game Objects/Ships/<Race>/<Vessel>.fbx` (nose along Unity +Z, final size, scale 1) and the manifest; `--no-export` renders the review sheet only. The debris script writes a damaged copy of every model under the same file name. `wire.ps1` creates an SF - Standard material for each manifest material (a copy of the Spemin hull material with the manifest's maps applied), remaps the FBX materials to them, places an inactive instance of each model under the other model templates of the Encounter location (layer Encounter), and points the vessel's slots in `m_alienShipModelTemplate` and `m_alienShipDebrisTemplate` at the instance and the debris model. Running it again replaces whatever an earlier run created; it refuses to replace a hand-made object with the same name.

Next, add the vessel lengths from the manifest to `c_shipModelLengths` in `DevTools/HeadlessProbe/ClaudeProbe.cs` and run the `shipmodels` probe scenario. It checks that every vessel gets its model and its length, that each model is longest along its nose and has its engines (a material named `... Engine`) at the back, and the size and orientation of its debris. The probe has no graphics, so look at each ship in the GUI Editor as well.

## Textured models

Since 2026-10-13 the game's models are being remade as textured models wherever that approach fits. The Thrynn ships were the first race done this way, followed by the Elowan, the Veloxi, the Gazurtoid, the Uhlek, the Noah 9 derelict and the Enterprise. For each vessel:

1. **Concept picture**: a concept picture is made from the original game's sensor picture (cropped from `previews/<race>.png` or `Research/Screenshots/Ships`) and its silhouette (`Assets/Game Objects/UI/Sensors`), and reviewed. A third reference picture of another ship of the same race keeps the materials consistent, but it also carries its layout over (the Thrynn scout ended up with the warship's four pylons twice); editing the picture of a near miss is the cheaper fix.
2. **Model**: a textured 3D model with 4K PBR maps is built from the approved picture, then remeshed to about 10,000 triangles. The remesh keeps the textures: it bakes them, including the normal map, from the full model of about 1.5 million triangles.
3. **`meshy-<race>.json`**: each vessel's id, name, STRINFO size, GLB file name, the direction its nose points after the glTF import (`-X` for the textured models so far: the review sheet shows every nose to the left) and, under `"meshy"`, the ids of the concept picture, the model and the remesh it was built from. The GLB files (about 48 MB each) stay outside the repository.
4. **`import_meshy.py`**: rotates and scales each model by the size rule, gives the rear faces that point backwards an engine material (for the probe's orientation check, and a glow on the nozzles), converts the models' metallic/roughness maps to the albedo and specular/smoothness maps of SF - Standard, creates an emissive map from the blue and cyan crystal, and writes the FBX, the maps (`Assets/Game Objects/Ships/<Race>/Textures`, 1024 pixels as JPEG, with the specular map as a 512 pixel PNG because of its alpha channel) and the manifest. Each vessel comes to about 1.4 MB of maps and about 0.6 MB of model.
5. **Wrecks and wiring**: the debris script, `wire.ps1` and the probe (see "Wrecks, wiring and the probe" above). `ShipModelWiring.cs` applies the manifest's maps to the materials.

```bash
blender --background --factory-startup --python DevTools/ShipModels/import_meshy.py -- DevTools/ShipModels/meshy-thrynn.json --source <folder with the GLB files> --preview DevTools/ShipModels/previews/thrynn.png
```

A race file and its vessels can also set:

- `"glow": {"hue": [..], "saturation": .., "value": ..}` for the emissive map (the Arth ship's blue is paint, so only its brighter missile caps glow). The hue can be a list of ranges: the Veloxi light their blue crystal as well as their red and pink engine pods with `[[170, 250], [320, 360], [0, 10]]`, since red wraps around 0;
- `"engines": false` for a ship with no engines to glow (the Noah 9 derelict, which STRINFO gives no energy; its glow is switched off with a saturation of 2);
- per vessel: `nose`, read from the preview sheet (the Veloxi scout, the Noah 9 derelict and the Enterprise came out facing +X, and the Veloxi drone, the Gazurtoid scout and the Uhlek scout lying along Y); `roll`, for a model that came out lying on its side, rotated about its length (in degrees, a right-handed turn about the nose: the Gazurtoid warship's core faced sideways, `90`); and `widthScale`, for a model that came out wider than the original, narrowed across its nose (the Uhlek scout's ring came out round, where the original's is an oval longer than it is wide, `0.85`);
- `conceptEdit`, next to the ids in a vessel's `"meshy"`, for the id of an edit of its concept picture (the Veloxi warship).

The Enterprise is the original game's easter egg, the starship from the television series. On 2026-10-14 it was decided to make it an original Interstel-style design that follows the sensor picture's layout (a disc, a neck, a rear hull and two nacelles), with no lettering, numbers or insignia, replacing the recognizable model chosen on 2026-10-09.

### The player's Arth ship

The player's ship is not a single mesh: `PlayerShip`, `StatusDisplay`, `Disembarked` and `ShipConfigurationPanel` switch its 16 cargo pods, its laser cannon and its missile launchers on and off according to the installed equipment, through the nodes of `Assets/Game Objects/Ships/Arth/Arth Ship.FBX`, which four scene copies reference by name (the flying ship, the status display, the ship parked on a planet and the Starport's Ship Configuration room). It is therefore built from two textured models, a bare hull and a sheet of parts (a cargo pod, the laser turret, a missile launcher), and `import_meshy_arth.py` (with `meshy-arth.json`) assembles them under the old file's node tree:

- it builds that tree from scratch (never from the file, which it overwrites): the root `Arth Ship`, `Hull`, `Laser Cannon` with `Laser` and `Laser Base` (now an empty node, since the turret is one piece), `Missile Launchers` with `Missile Launcher - Left` and `- Right`, and `Pod 01` to `Pod 16`. Unity keeps each node's file id by its name, so the scenes' references survive;
- the hull takes the old hull's place and length (the nose at -Y, 134 inches from the cockpit to the engine fork, 51 units in play);
- the sheet of parts is split by where its faces lie (a remesh is a thousand loose patches, one per texture island); the turret and the launchers are rotated to face forward (the turret by its barrels, found relative to the area-weighted centre of its surface; the launchers by the bright blue of their tubes), and the pods go in four rings of four around the spine (an upper and a lower pod on each side, so that from above they still form the original's two rows), since the new spine is thicker and shorter than the old one;
- the old file is in inches and Blender writes centimetres: the parts are exported in centimetres under a root scaled to 0.01 (a round trip of the old model through these settings gives the same transforms and bounds in Unity);
- the windows and the nozzles, which come out of the textured model near white, and the cockpit canopy, which comes out as dark glass, are lit in the emissive map by their position on the hull: they are the ship's only lights in the dark of hyperspace;
- it writes `manifest-arth.json` with a `playerShip` section. `ShipModelWiring.cs` then sets up the import, removes the scene copies' material overrides (they assigned the old model's eight materials by hand), moves the flying ship's two contrails to the new nozzles, and gives the wreck its own import scale (15, because `CombatController` spawns the wreck at its own scale, not at the ship's 15).

```bash
blender --background --factory-startup --python DevTools/ShipModels/import_meshy_arth.py -- DevTools/ShipModels/meshy-arth.json --source <folder with the GLB files> --preview DevTools/ShipModels/previews/arth.png
blender --background --factory-startup --python process_ship_debris.py -- -s 1986 --suffix "" --batch "Assets/Game Objects/Ships/Arth" "Assets/Game Objects/Ships Debris/Arth"
```

```powershell
& "DevTools\ShipModels\wire.ps1" -Manifest "DevTools\ShipModels\manifest-arth.json"
```

The probe scenarios `shipmodels`, `debrispictures`, `encounterviewscale`, `systemviewpictures` (the engine glow) and `shippictures` (every mesh of the ship drawn with its maps, with pictures under `-Graphics`) cover it. `Arth Ship Barrel.mat` stays, although the terrain vehicle's old barrel was the last thing that used it.

### The terrain vehicle

`TerrainVehicle` spins and steers the six wheels through the nodes of `Assets/Game Objects/Terrain Vehicle/Terrain Vehicle.fbx` (the scene's copy references them by name: the root, `Suspension - FL` to `RR`, `Steering - FL` to `RR`, `Wheel 1` to `6`, `Laser` and `Radar`, which the scene gives a rotator). So the vehicle is built from two textured models, a body made with its wheels left off and one wheel, and `import_meshy_tv.py` (with `meshy-terrain-vehicle.json`) assembles them in the old file's node tree:

- it reads that tree from a copy of the original file taken from git (`--old`), never from the file it overwrites, and keeps every node's name and transform: Unity keeps each node's file id by its name, so the scene's references survive. The old file is in metres, and the new one is written in metres in the file's own unit scale (`FBX_SCALE_UNITS`): the scene overrides the root's scale (0.5), so a unit scale on the root's transform (`FBX_SCALE_NONE`, the Arth ship's setting) would be lost and the model would come in a hundred times too small;
- the body is turned front first by its gold cowl (to -Y, as the old body) and scaled so that its six axle hubs (the outermost low geometry, in three groups a side) are as far apart, front to back, as the old wheels (9 units). Its front hubs sit lower than the other four, so it rides at their average height;
- each `Suspension` node moves (the scene overrides none of their transforms) so that its wheel is at its hub sideways and along, and at the old wheels' height: the wheels meet the ground where the old ones did. The wheel keeps the old wheel's diameter (4.227) and lies along the node's Z, which `TerrainVehicle` spins it about;
- the radar bar standing above the roof is cut out of the body into the `Radar` node's mesh, with the node at the mast's foot, so the scene's rotator turns it as it turned the old radar. The laser turret stays part of the body; nothing moves the `Laser` node, which stays as an empty node;
- the scene gives the root a capsule collider made for the old body (19 long, radius 4): the manifest carries one for the new size, with the old one's proportions to the vehicle kept, computed from those fixed old values so that a second run gives the same capsule;
- it writes `manifest-terrain-vehicle.json` with a `terrainVehicle` section. `ShipModelWiring.cs` then sets up the import, removes the scene copies' material overrides (the vehicle's own copy, and three placeholders in `Test.unity`) and sizes the vehicle's capsule.

```powershell
git show "bfd3a590:Assets/Game Objects/Terrain Vehicle/Terrain Vehicle.fbx" > old-terrain-vehicle.fbx
```

```bash
blender --background --factory-startup --python DevTools/ShipModels/import_meshy_tv.py -- DevTools/ShipModels/meshy-terrain-vehicle.json --source <folder with the GLB files> --old old-terrain-vehicle.fbx --preview DevTools/ShipModels/previews/terrain-vehicle.png
```

```powershell
& "DevTools\ShipModels\wire.ps1" -Manifest "DevTools\ShipModels\manifest-terrain-vehicle.json"
```

### Ground props

The ancient ruins, the rocks and the Endurium and mercury deposits are one mesh node each, and the game uses them through that node: the ruins' templates in `TerrainRuins` are the model files themselves, and the templates of `TerrainRocks` and `TerrainElements` are scene copies of the models whose material overrides find the mesh by its node. `import_meshy_props.py` (with `meshy-ruins.json` or `meshy-rocks.json`) replaces each prop's mesh and keeps the rest:

- it reads each original from a folder of copies taken from git (`--old`), never from the files it overwrites, and keeps the node's name and transform. The node's origin is where placing a prop sets it down and turns it, and it is off the geometry's middle in most of the old files;
- the new mesh goes into the node's frame so that it stands where the old one stood: its middle over the old middle, its bottom at the old bottom, and its size as the old one's. A `footprint` prop (the ruins) has its widest side as the old one's, and a square base turned to the axes; a `volume` prop has its bounding volume as the old one's, and its long axis turned as the old one's;
- it writes each FBX in metres in the file's own unit scale (`FBX_SCALE_UNITS`), the maps for SF - Standard (the ruins' glyph lines, a pale cyan, glow through their own `glow` settings: hues 150 to 210 from a saturation of 0.12; Endurium's crystals through hues 260 to 335), and a manifest with a `models` section. `ShipModelWiring.cs` then makes the materials and sets up each model's import with its own (the old ruins had none, and were drawn in Unity's default material). The rocks' and the deposits' materials keep their assets (`Rock N.mat`, `Endurium.mat`, `Mercury.mat`), so every scene override that points at them stays; the rocks' new maps go into `Rocks/Textures`, beside the old ones, and the scenery rocks take a colour of 0.55 over them, since the new stone is paler than the old;
- twenty of the 22 kinds of deposit are scene copies of the rocks in their element's own material, which laid the element's colour over the old rock's colour map. A rock also gets a grey copy of its new colour map (`Rock N - Grey.jpg`, brought to a brightness of 0.62 on average), and the manifest's `rockMaps` section has `ShipModelWiring.cs` give each such element material its rock's grey and normal maps, the element's colour staying over them. A deposit drawn in a rock's own material (one is, in `Rock 1.mat`) keeps that material's new maps.

```powershell
git show "bfd3a590:Assets/Game Objects/Ancient Ruins/Ancient Ruins 1.fbx" > old/"Ancient Ruins 1.fbx"
```

(and the same for the other four ruins, the nine rocks, `Endurium.fbx` and `Mercury.fbx`), then, for each manifest:

```bash
blender --background --factory-startup --python DevTools/ShipModels/import_meshy_props.py -- DevTools/ShipModels/meshy-ruins.json --source <folder with the GLB files> --old old --preview DevTools/ShipModels/previews/ancient-ruins.png
```

```powershell
& "DevTools\ShipModels\wire.ps1" -Manifest "DevTools\ShipModels\manifest-ruins.json"
```

The probe scenarios `ruinmodels` (each ruin in its own textured material with the glow, its triangles, its square of 24, a ruin placed on the ground; a picture under `-Graphics`) and `rockmodels` (each rock in its material with the new maps, its triangles and the old rock's bounding volume, every rock-shaped deposit on its rock's grey map, Endurium's and mercury's own maps and sizes, the rocks and deposits placed on planet 92) cover them.

The probe scenario `tvmodel` covers it (the materials, the wheels on their steering joints, the triangle counts, the wheels level on the ground with the body clear of it, the capsule against the model, the radar turning; a picture under `-Graphics`).

## Conventions

- A vessel is imported in a frame with the nose along +Y and up along +Z; `ship_kit.to_unity_frame` turns it into the frame the FBX export expects (nose along Blender -Y, as in the existing models).
- Material names start with the vessel's name. A material named `<Vessel> Engine` marks the engines for the probe's orientation check: these are the rear faces that point backwards, so the Enterprise's red nacelle bands, which face sideways, are not engines.
