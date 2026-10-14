# Procedural lifeform models

The lifeforms on planet surfaces have one model per body plan from the 1984 design notes (`Research/Notes/planet.txt`, icons 8 to 24), and two for each plant size (the notes have a type A and a type B of each). Blender Python scripts build them rather than hand modelling: nothing is random at build time (the noise and the scattering use fixed seeds), so a rebuild produces the same models. No scene references the models directly: the lifeform code picks the model of a species by its body plan, scales it by the species size and tints it with the species colour (see "In the game" below).

## Files

| File | What it is |
|---|---|
| `lifeform_kit.py` | Shared helpers: signed distance shapes and surface nets for the bodies, explicit parts (eyes, claws, tubes, leaves, parasol leaves), repeatable noise, the bake and the FBX export |
| `build_lifeforms.py` | One function per model: its body, its parts, its skin relief and its paint |
| `render_previews.py` | The review renders (Cycles), made from the exported FBX files and their maps, exactly as the game receives them |
| `manifest-lifeforms.json` | Written by a build: each model's file, body plan, triangle count, size, materials and maps |
| `previews/` | `contact-sheet.png` (every model, each with a different tint) and `closeup-<model>.png` (six of them) |

## Building

From the project root, with Blender 5.2 (`--factory-startup` keeps user add-ons and preferences out):

```bash
blender --background --factory-startup --python DevTools/LifeformModels/build_lifeforms.py
blender --background --factory-startup --python DevTools/LifeformModels/render_previews.py -- --sheet DevTools/LifeformModels/previews/contact-sheet.png --closeups DevTools/LifeformModels/previews
```

A build writes `Assets/Game Objects/Lifeforms/<Model>.fbx` with its maps, and the manifest. `--plan Snakelike,LeavedLargeA` builds only those models; `--out <folder>` writes somewhere else (to try out a change without touching the project), and `render_previews.py -- --source <folder>` renders from there. A model takes 15 to 35 seconds (mostly the bake, which runs on the GPU when Cycles finds a HIP device and on the CPU otherwise), the full set about six minutes, and the previews about three. `--closeup-plans` selects the close-ups (six by default); `--cell` and `--samples` set the size and quality of the contact sheet's pictures.

Unity creates the `.meta` files the first time it imports new files (run the headless compile check once); the committed ones import the normal maps as normal maps. A rebuild that keeps the file names keeps them.

## How a model is made

1. **Body.** A signed distance field: smooth unions of spheres, ellipsoids and round cones (a limb is a chain of round cones), smooth cuts (mouths, sockets) and a flat sole where the lifeform rests on the ground. It is sampled on a grid of 4 thousandths of the model's size and turned into a dense quad mesh by surface nets; the vertices are relaxed and projected back onto the surface. Each vertex knows the region (tag) it belongs to, so the relief and the paint can differ on legs, plates, crests and so on.
2. **Skin.** The dense mesh is displaced along its normals by a relief (scales, wrinkles, warts, pores, chitin plates, or bark over large lumps) and painted per vertex: albedo (a light ground, a darker pattern on the back that fades onto the flanks, a pale belly, dirt in the crevices), smoothness and specular. Bilateral lifeforms are mirrored, so their patterns are too.
3. **Parts.** Eyes, claws, horns, spines, mandibles, antennae and stems are built in code twice, at a high and at a low resolution, each with its own paint. Leaves (long ones and round parasols) are double-sided sheets: the high copy is the low one subdivided, keeping the low one's normals, so it lies exactly on it.
4. **Low mesh.** The body without the relief is decimated (mirrored for bilateral plans), joined with the low parts, triangulated, scaled and unwrapped (Smart UV Project; a copy of the UVs becomes the second UV set, from which the SF shaders read occlusion).
5. **Bake** (Cycles, 1024 x 1024): the body, the thick parts and the leaves each bake from their own high meshes, so a claw never picks up the skin it grows from; leaves bake with a tiny ray offset, since they lie close to each other. The bake produces albedo, specular and smoothness, emission where there is any, a tangent space normal map (MikkTSpace, OpenGL green up, as Unity expects), and ambient occlusion of the low mesh standing on a ground plane (512 x 512; leaves are flat shaded for it, so that a smooth normal does not dip into the folded leaf).

## Conventions

- **Frame.** Built with the head along +Y and up along +Z, then rotated so the head points along Blender -Y before the export: in Unity the head is along **+Z** and up is +Y, as for the ship models in `DevTools/ShipModels`. Radial and amorphous lifeforms have a front too (the direction their eyes look in the previews).
- **Size.** Every model fits a 1 unit cube: its largest side is 1. Most stand about 0.5 to 1 tall; the snake is long and low. The manifest gives each model's width, height and length (Unity X, Y and Z). Animals stand centred over the origin; plants have their base at the origin. Scale 1, transforms applied.
- **Polygons.** 1,300 to 4,000 triangles per model (the manifest has each count), with the detail in the normal map. The plants' leaves are double-sided geometry, so plant materials need no culling change.
- **Materials and tint.** Every model has up to two materials, in this order: `<Model> Skin`, the part to tint with the species colour (multiply the albedo by it, for example through `SF_AlbedoColor`), and `<Model> Detail`, which is never tinted (eyes, claws, horns, spines, bark, seed pods). Both use the same maps. The skin's albedo is a near-white ground with the pattern in darker greys, so any tint gives a two-tone animal; the detail parts carry their own colours. On animals the body is the skin; on plants the leaves (and soft stems, and the succulent trunk of LeavedMediumB) are the skin and the wood is the detail.
- **Maps**, next to each FBX: `<Model> - Albedo.png` (sRGB), `<Model> - Specular.png` (RGB is the specular colour, sRGB coded, A the smoothness: the layout of `SF_SpecularMap`), `<Model> - Normal.png` (to be imported as a normal map), `<Model> - Occlusion.png` (UV2, `SF_OcclusionMap`), and `<Model> - Emissive.png` for the models with glowing eyes or pods (`SF_EmissiveMap`).

## In the game

After the models are built or rebuilt, run `& "DevTools\LifeformModels\wire.ps1"` from the project root (the project must not be open in an Editor). It copies `LifeformModelWiring.cs` into Assets/ and runs Unity in batch mode: for each model in `manifest-lifeforms.json` it creates (or updates) `Materials/<Model> Skin.mat` and `<Model> Detail.mat` (SF - Standard with the model's maps, keywords set by the material Inspector's own logic), remaps the FBX's materials to them, and saves `Assets/Resources/Lifeforms/<Model>.prefab` (the model under a root of its own). `TerrainLifeforms` loads those prefabs. `probe.ps1 -Scenario lifeformpictures -Graphics` renders the models on a planet into `Logs/lifeform-pictures/`.

## The models

| Model | Body plan (notes icon) | What it is |
|---|---|---|
| Snakelike | 8, bilateral, no legs, snakelike | A segmented serpent reared like a cobra: hood, four eyes, mandibles, dorsal spines |
| BilateralNoLegs | 9, no legs, not snakelike | A great slug: humped mantle on a rippled foot, eyes on stalks, feelers |
| BilateralOneLeg | 10, one leg | A hopper on one heavy leg with a counterweight tail, hooked beak and crest |
| BilateralTwoLegs | 11, two legs | A striding theropod-like biped with four eyes and a crest |
| BilateralFourLegs | 12, three or four legs | A heavy six-eyed grazer with horns and a ridge of back plates |
| BilateralManyLegs | 13, more than four legs | An eight-legged arthropod: segmented carapace, mandibles, antennae |
| RadialNoLegs | 14, radial, no legs | A ribbed urchin dome with spines, a ring of glowing eyes and a feeding crown |
| RadialFewLegs | 15, radial, one or two legs | A walking sphere ringed with eyes on two jointed legs |
| RadialManyLegs | 16, radial, more legs | A seven-legged walker with arched tentacle legs and a crown of eyes |
| AmorphousNoLegs | 17, amorphous, no legs | A lopsided heap of glistening protoplasm with sunken eyes |
| AmorphousLegs | 18, amorphous, legs | A blob on uneven pseudopods, one raised, with eyes on stalks |
| LeavedSmallA | 19, small plant A | A rosette of broad spotted leaves around a glowing seed bulb |
| LeavedSmallB | 20, small plant B | Parasol sprouts: round cupped leaves on slender stems from a root bulb |
| LeavedMediumA | 21, medium plant A | A woody shrub with whorls of glossy leaves |
| LeavedMediumB | 22, medium plant B | A bottle plant: ribbed succulent trunk, drooping strap leaves, glowing pod |
| LeavedLargeA | 23, large plant A | An umbrella tree with a buttressed trunk and a flat crown |
| LeavedLargeB | 24, large plant B | A palm-like tree with feathered fronds and glowing fruit |

The flying and floating icons (25, 26) are not body plans: the game shows any lifeform that way while it flies or floats.
