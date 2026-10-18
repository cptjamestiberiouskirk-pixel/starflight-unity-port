"""Textured alien ship models: GLB files in, the project's FBX, maps and manifest out (Blender 5.2, background mode).

blender --background --factory-startup --python DevTools/ShipModels/import_meshy.py -- DevTools/ShipModels/meshy-thrynn.json --source <folder> [--preview <png>] [--no-export]

The race file (meshy-<race>.json) lists each vessel's GLB (a remeshed model with its baked maps, kept outside the repository in
the folder given with --source), which way its nose points, and the sources it came from. For every vessel this script
turns the model to the frame of the procedural ships (nose +Y), scales it with the size rule of ship_kit, gives the faces at
the back that face backwards an engine material, converts the models' metallic/roughness maps to the albedo and specular/smoothness
maps of SF - Standard (with an emissive map for the blue and cyan crystal), and exports the FBX and the manifest that wire.ps1
reads. See README.md in this folder.
"""

import json
import math
import os
import sys

import bpy
import numpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ship_kit  # noqa: E402

# the turn that brings a GLB's nose to the canonical +Y (the textured models face -X after the glTF import: check the review sheet,
# which shows every nose to the left)
NOSE_TURNS = {"+X": 90.0, "-X": -90.0, "+Y": 0.0, "-Y": 180.0}

# faces at the back that face backwards (their normal within about 45 degrees of -Y) are the engines' nozzles
ENGINE_NORMAL = -0.7
ENGINE_DEPTH = 0.15

# the crystal and the windows glow: blue to cyan, saturated and bright enough
GLOW_HUE = (170.0, 250.0)
GLOW_SATURATION = 0.4
GLOW_VALUE = 0.3

# what an engine material adds to its nozzles (SF_EmissiveColor)
ENGINE_GLOW = [0.9, 0.35, 0.12]

# the repository has no large file storage: the colour, normal and glow maps go in as JPEG (Unity compresses every map on
# import anyway), and the specular map, which keeps smoothness in its alpha, as a PNG at half the size of the others
JPEG_QUALITY = 92
SPECULAR_SHRINK = 2


def srgb_to_linear(c):
    return numpy.where(c <= 0.04045, c / 12.92, numpy.power((c + 0.055) / 1.055, 2.4))


def linear_to_srgb(c):
    c = numpy.clip(c, 0.0, 1.0)
    return numpy.where(c <= 0.0031308, c * 12.92, 1.055 * numpy.power(c, 1.0 / 2.4) - 0.055)


def import_glb(path, name):
    """Import a GLB as one mesh object with its transforms applied and nothing else left in the scene from it."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in imported if o.type == "MESH"]

    if not meshes:
        raise RuntimeError("no mesh in " + path)

    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world

    for obj in imported:
        if obj not in meshes:
            bpy.data.objects.remove(obj)

    bpy.ops.object.select_all(action="DESELECT")

    for obj in meshes:
        obj.select_set(True)

    bpy.context.view_layer.objects.active = meshes[0]

    if len(meshes) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = name
    obj.data.name = name

    return obj


def upstream_image(socket):
    """The image texture that feeds a shader socket, through any nodes in between (the glTF importer adds a few)."""
    stack = [socket]
    seen = set()

    while stack:
        current = stack.pop()

        for link in current.links:
            node = link.from_node

            if node.type == "TEX_IMAGE" and node.image is not None:
                return node.image

            if node.name not in seen:
                seen.add(node.name)
                stack.extend(s for s in node.inputs if s.is_linked)

    return None


def image_pixels(image):
    """An image's pixels as a float array (rows bottom first, as Blender keeps them), without the slow Python list."""
    width, height = image.size
    pixels = numpy.empty(width * height * 4, dtype=numpy.float32)
    image.pixels.foreach_get(pixels)

    return pixels.reshape(height, width, 4)


def shrink(pixels, size):
    """Average blocks of pixels down to size x size (the source maps are square, 4096 or 2048)."""
    height, width = pixels.shape[:2]
    factor = width // size

    if factor <= 1 or height != width or width % size != 0:
        return pixels

    return pixels.reshape(size, factor, size, factor, pixels.shape[2]).mean(axis=(1, 3))


def save_image(pixels, path):
    """Save as PNG or, for a .jpg path, as a JPEG of quality JPEG_QUALITY (no alpha)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    height, width = pixels.shape[:2]
    image = bpy.data.images.new(os.path.basename(path), width, height, alpha=True)
    image.pixels.foreach_set(numpy.ascontiguousarray(pixels, dtype=numpy.float32).ravel())
    image.filepath_raw = path

    if path.lower().endswith(".jpg"):
        image.file_format = "JPEG"
        image.save(quality=JPEG_QUALITY)
    else:
        image.file_format = "PNG"
        image.save()

    bpy.data.images.remove(image)


def glow_mask(base, glow=None):
    """Where the base colour (sRGB) is the crystal's blue or cyan: 1 there, 0 elsewhere, with a soft edge.

    glow: a race file's "glow" settings (hue, saturation, value) in place of the defaults above. Its hue is one range
    [from, to] or a list of them (the Veloxi's blue crystal and their red engine pods, whose red wraps round 0)."""
    glow = glow or {}
    glow_hue = glow.get("hue", GLOW_HUE)
    glow_saturation = glow.get("saturation", GLOW_SATURATION)
    glow_value = glow.get("value", GLOW_VALUE)
    hue_ranges = glow_hue if isinstance(glow_hue[0], (list, tuple)) else [glow_hue]
    r, g, b = base[..., 0], base[..., 1], base[..., 2]
    high = numpy.maximum(numpy.maximum(r, g), b)
    low = numpy.minimum(numpy.minimum(r, g), b)
    spread = numpy.maximum(high - low, 1e-6)
    saturation = numpy.where(high > 0.0, (high - low) / numpy.maximum(high, 1e-6), 0.0)

    hue = numpy.where(high == r, (g - b) / spread % 6.0, numpy.where(high == g, (b - r) / spread + 2.0, (r - g) / spread + 4.0)) * 60.0
    in_hue = numpy.zeros(hue.shape, dtype=bool)

    for hue_range in hue_ranges:
        in_hue |= (hue >= hue_range[0]) & (hue <= hue_range[1])

    return in_hue * numpy.clip((saturation - glow_saturation) / 0.2, 0.0, 1.0) * numpy.clip((high - glow_value) / 0.2, 0.0, 1.0)


# the colours of a glowing core's points of light (the Mysterion's: the many coloured pixels in the middle of its sensor picture)
CORE_COLOURS = numpy.array([[0.2, 0.4, 1.0], [0.2, 0.9, 1.0], [1.0, 0.25, 0.9], [1.0, 0.45, 0.6], [1.0, 0.2, 0.2], [1.0, 1.0, 1.0]], dtype=numpy.float32)


def core_mask(obj, size, fraction):
    """The pixels of a size x size map (rows bottom first) that the model's faces within this fraction of its half size from its middle
    use, and how many triangles those are: where a glowing core is drawn (a model whose core came out as plain grey panels)."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    co = numpy.empty(len(mesh.vertices) * 3, dtype=numpy.float64)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    middle = (co.max(axis=0) + co.min(axis=0)) / 2.0
    reach = fraction * 0.5 * float((co.max(axis=0) - co.min(axis=0)).max())
    uv = numpy.empty(len(mesh.loops) * 2, dtype=numpy.float64)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    triangle_vertices = numpy.empty(len(mesh.loop_triangles) * 3, dtype=numpy.int64)
    mesh.loop_triangles.foreach_get("vertices", triangle_vertices)
    triangle_loops = numpy.empty(len(mesh.loop_triangles) * 3, dtype=numpy.int64)
    mesh.loop_triangles.foreach_get("loops", triangle_loops)
    inside = numpy.linalg.norm(co[triangle_vertices.reshape(-1, 3)].mean(axis=1) - middle, axis=1) < reach
    mask = numpy.zeros((size, size), dtype=bool)

    for loops in triangle_loops.reshape(-1, 3)[inside]:
        # each triangle's pixels in the map, by their barycentric weights (a little over its edges, so that no seam shows)
        p = uv[loops] * size
        x0, y0 = (numpy.floor(p.min(axis=0)) - 1).astype(int)
        x1, y1 = (numpy.ceil(p.max(axis=0)) + 1).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size), min(y1, size)
        (ax, ay), (bx, by), (cx, cy) = p
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)

        if (x1 <= x0) or (y1 <= y0) or (abs(d) < 1e-9):
            continue

        xs, ys = numpy.meshgrid(numpy.arange(x0, x1) + 0.5, numpy.arange(y0, y1) + 0.5)
        w1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
        w2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
        mask[y0:y1, x0:x1] |= (w1 >= -0.05) & (w2 >= -0.05) & (1.0 - w1 - w2 >= -0.05)

    return mask, int(inside.sum())


def core_speckles(size, seed=1986):
    """Points of light of the core's colours on black, two pixels each, the same every time."""
    rng = numpy.random.default_rng(seed)
    half = size // 2
    dots = rng.random((half, half)) < 0.12
    colours = CORE_COLOURS[rng.integers(0, len(CORE_COLOURS), (half, half))] * rng.uniform(0.6, 1.0, (half, half, 1)).astype(numpy.float32)
    image = numpy.where(dots[..., None], colours, 0.0).astype(numpy.float32)

    return image.repeat(2, axis=0).repeat(2, axis=1)


def convert_maps(obj, folder, prefix, size, glow=None, core=None):
    """Write the SF - Standard maps of the model's glTF material; returns their asset paths (relative to the project). With a core mask
    (core_mask), its pixels get the core's points of light, dark in the albedo map and glowing in the emissive map."""
    material = obj.data.materials[0]
    bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    base_image = upstream_image(bsdf.inputs["Base Color"])
    mr_image = upstream_image(bsdf.inputs["Metallic"]) or upstream_image(bsdf.inputs["Roughness"])
    normal_image = upstream_image(bsdf.inputs["Normal"])

    if base_image is None:
        raise RuntimeError(obj.name + " has no base colour map")

    base = shrink(image_pixels(base_image), size)
    opaque = numpy.ones(base.shape[:2] + (1,), dtype=numpy.float32)

    # glTF keeps roughness in green and metalness in blue; without the map the surface is a rough dielectric
    if mr_image is not None:
        mr = shrink(image_pixels(mr_image), size)
        metal = mr[..., 2:3]
        rough = mr[..., 1:2]
    else:
        metal = numpy.zeros_like(opaque)
        rough = numpy.full_like(opaque, 0.8)

    base_linear = srgb_to_linear(base[..., :3])
    albedo = linear_to_srgb(base_linear * (1.0 - metal))
    specular = linear_to_srgb(0.04 * (1.0 - metal) + base_linear * metal)
    glow = glow_mask(base[..., :3], glow)[..., None]
    emissive = base[..., :3] * glow

    if core is not None:
        speckles = core_speckles(base.shape[0])[: base.shape[0], : base.shape[1]]
        albedo = numpy.where(core[..., None], 0.03 + speckles * 0.5, albedo)
        emissive = numpy.where(core[..., None], speckles, emissive)
        glow = numpy.maximum(glow, core[..., None].astype(numpy.float32))

    maps = {}
    stem = os.path.join(folder, prefix)

    def write(kind, pixels, extension):
        path = stem + " - " + kind + extension
        save_image(pixels, os.path.join(ship_kit.PROJECT_ROOT, path))
        maps[kind] = path.replace(os.sep, "/")

    write("Albedo", numpy.concatenate([albedo, opaque], axis=2), ".jpg")
    write("Specular", shrink(numpy.concatenate([specular, 1.0 - rough], axis=2), size // SPECULAR_SHRINK), ".png")
    write("Emissive", numpy.concatenate([emissive, opaque], axis=2), ".jpg")

    if normal_image is not None:
        write("Normal", shrink(image_pixels(normal_image), size), ".jpg")

    print("MAPS %s size=%d metal=%.2f glow=%.4f normal=%s" % (prefix, base.shape[0], float(metal.mean()), float(glow.mean()), normal_image is not None))

    return maps


def mark_engines(obj, length):
    """Give the backward-facing faces at the back the second material; returns how many faces that is."""
    mesh = obj.data
    back = min(v.co.y for v in mesh.vertices)
    count = 0

    for polygon in mesh.polygons:
        if polygon.normal.y <= ENGINE_NORMAL and polygon.center.y <= back + ENGINE_DEPTH * length:
            polygon.material_index = 1
            count += 1
        else:
            polygon.material_index = 0

    return count


def flat_material(name):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = (0.5, 0.5, 0.5, 1.0)

    return material


def add_preview_lights():
    scene = bpy.context.scene
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "AgX"

    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.32, 0.33, 0.35, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world

    for name, energy, rotation in (("Key", 3.5, (50.0, 0.0, 35.0)), ("Fill", 1.2, (-60.0, 0.0, -140.0))):
        light = bpy.data.lights.new(name, "SUN")
        light.energy = energy
        holder = bpy.data.objects.new(name, light)
        holder.rotation_euler = [math.radians(a) for a in rotation]
        scene.collection.objects.link(holder)


def main():
    arguments = ship_kit.script_arguments()

    if not arguments or "--source" not in arguments:
        raise SystemExit("usage: import_meshy.py -- <meshy-race.json> --source <folder with the GLB files> [--preview <png>] [--no-export]")

    race_path = os.path.abspath(arguments[0])
    source = arguments[arguments.index("--source") + 1]
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    export = "--no-export" not in arguments

    with open(race_path, encoding="utf-8") as race_file:
        race_file_data = json.load(race_file)

    race = race_file_data["race"]
    size = race_file_data.get("textureSize", 1024)
    folder = "Assets/Game Objects/Ships/%s/Textures" % race

    ship_kit.reset_scene()
    built = []
    manifest = {"race": race, "vessels": [], "materials": []}

    for vessel in race_file_data["vessels"]:
        name = vessel["name"]
        obj = import_glb(os.path.join(source, vessel["glb"]), name)
        obj.data.materials[0].name = name + " Meshy"

        # to the canonical frame: nose +Y, the middle of the bounds at the origin, the length of the size rule along the nose
        obj.data.transform(Matrix.Rotation(math.radians(NOSE_TURNS[vessel.get("nose", "+X")]), 4, "Z"))
        # a model that came out lying on its side is rolled about its length (degrees, a right-handed turn about the nose)
        obj.data.transform(Matrix.Rotation(math.radians(vessel.get("roll", 0.0)), 4, "Y"))
        # a model that came out wider than the original is narrowed across its nose to the original's proportions
        obj.data.transform(Matrix.Diagonal((vessel.get("widthScale", 1.0), 1.0, 1.0, 1.0)))
        corners = [Vector(v.co) for v in obj.data.vertices]
        low = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
        high = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
        extent = high - low
        length = ship_kit.target_length(vessel["strinfo"])
        # a round ship (a sphere, a ring, a spiral) has no length of its own: its size is across its widest side
        scale = length / (max(extent.x, extent.y, extent.z) if vessel.get("round") else extent.y)
        obj.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)) @ Matrix.Translation(-(low + high) / 2.0))
        obj.data.update()
        bpy.context.view_layer.update()

        if (extent.y < max(extent.x, extent.z)) and not vessel.get("round"):
            print("WARNING %s is not longest along its nose: %s" % (name, tuple(round(e * scale, 1) for e in extent)))

        # a core that came out as plain panels glows with points of light (the Mysterion's)
        core = None

        if export and vessel.get("core"):
            core, core_triangles = core_mask(obj, size, vessel["core"])
            print("CORE %s: %d triangles, %.1f%% of the map" % (name, core_triangles, 100.0 * float(core.mean())))

        maps = convert_maps(obj, folder, name, size, race_file_data.get("glow"), core) if export else {}

        # the preview shows the glTF material as it came; the export gets the materials the Unity side makes
        built.append((obj, vessel.get("screenshot")))
        hull = name + " Hull"
        engine = name + " Engine"
        manifest["vessels"].append({
            "id": vessel["id"],
            "name": name,
            "strinfoSize": vessel["strinfo"],
            "length": round(length, 3),
            "model": "Assets/Game Objects/Ships/%s/%s.fbx" % (race, name),
            "debris": "Assets/Game Objects/Ships Debris/%s/%s.fbx" % (race, name),
            "materials": [hull, engine],
            "meshy": vessel.get("meshy", {}),
            "replaceTemplate": bool(vessel.get("replaceTemplate", False)),
            "round": bool(vessel.get("round", False)),
        })

        for material_name, emissive in ((hull, [0.0, 0.0, 0.0]), (engine, ENGINE_GLOW)):
            manifest["materials"].append({
                "name": material_name,
                "albedo": [1.0, 1.0, 1.0],
                "specular": [1.0, 1.0, 1.0],
                "smoothness": 1.0,
                "emissive": emissive,
                "folder": "Assets/Game Objects/Ships/" + race,
                "albedoMap": maps.get("Albedo", ""),
                "specularMap": maps.get("Specular", ""),
                "normalMap": maps.get("Normal", ""),
                "emissiveMap": maps.get("Emissive", ""),
            })

        print("SHIP %s id=%d length=%.1f dimensions=%s triangles=%d" % (
            name, vessel["id"], length, tuple(round(d, 1) for d in obj.dimensions), len(obj.data.polygons)))

    if preview:
        add_preview_lights()
        ship_kit.preview_sheet(built, os.path.abspath(preview), engine="CYCLES")
        print("PREVIEW " + os.path.abspath(preview))

    if export:
        for obj, _ in built:
            name = obj.name
            gltf_material = obj.data.materials[0]
            obj.data.materials.clear()
            obj.data.materials.append(flat_material(name + " Hull"))
            obj.data.materials.append(flat_material(name + " Engine"))
            # a dead ship (the Noah 9 derelict: STRINFO gives it no energy) has no engines to glow
            engines = mark_engines(obj, ship_kit.target_length(next(v["strinfo"] for v in race_file_data["vessels"] if v["name"] == name))) if race_file_data.get("engines", True) else 0
            bpy.data.materials.remove(gltf_material)

            if engines == 0:
                obj.data.materials.pop(index=1)
                manifest_vessel = next(v for v in manifest["vessels"] if v["name"] == name)
                manifest_vessel["materials"] = manifest_vessel["materials"][:1]
                manifest["materials"] = [m for m in manifest["materials"] if m["name"] != name + " Engine"]

            print("ENGINES %s faces=%d" % (name, engines))

            ship_kit.to_unity_frame(obj, "-Y")
            ship_kit.export_fbx(obj, os.path.join(ship_kit.SHIPS_FOLDER, race, name + ".fbx"))

        manifest_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest-%s.json" % race.lower().replace(" ", "-"))

        with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
            json.dump(manifest, manifest_file, indent="\t")
            manifest_file.write("\n")

        print("MANIFEST " + manifest_path)


# import_meshy_arth.py imports this module for its helpers
if __name__ == "__main__":
    main()
