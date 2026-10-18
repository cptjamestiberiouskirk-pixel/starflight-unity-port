"""Textured single-mesh ground props: the ancient ruins, the rocks and the two deposits with shapes of their own (Blender 5.2,
background mode).

blender --background --factory-startup --python DevTools/ShipModels/import_meshy_props.py -- DevTools/ShipModels/meshy-<props>.json --source <folder with the GLB files> --old <folder with the original FBX files> [--preview <png>] [--no-export]

Each prop's FBX is one mesh node, and the game uses it through that node: the ruins' templates are the model files themselves,
and the scene's rock and deposit templates are copies of the models whose material overrides find the mesh by its node (Unity
keeps the node's file id by its name). So each new prop keeps its old file's node name and transform: the node's origin is
where placing a prop sets it down and turns it, and it is off the geometry's middle in most of the old files (the rocks were
exported side by side from one scene). The new mesh goes into the node's frame so that it stands where the old one stood, its
middle over the old middle and its bottom at the old bottom, scaled to the old size:

- "footprint" (the ruins, the mercury pool): its widest horizontal side as the old one's, a square base turned to the axes;
- "volume" (the rocks, the Endurium crystals): its bounding volume as the old one's, its long axis turned as the old one's.

The originals are read from a folder of copies taken from git before the first import (--old), never from the files this
script overwrites. The FBX is written in metres in the file's own unit scale, as the terrain vehicle's is.

The maps are converted for SF - Standard (import_meshy.convert_maps); a rock also gets a grey copy of its colour map, which
the deposits made in its shape take, in their element's colour. It writes manifest-<props>.json for wire.ps1: the materials
with their maps, the models whose import takes them, and the rocks' grey and normal maps for the deposits.
"""

import json
import math
import os
import sys

import bpy
import numpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import import_meshy  # noqa: E402
import ship_kit  # noqa: E402

# no glow (the rocks, the mercury, the wheels)
NO_GLOW = {"hue": [0.0, 0.0], "saturation": 2.0, "value": 2.0}

# the brightness the grey copy of a rock's colour map is brought to on average, so that an element's colour over it reads
GREY_MEAN = 0.62


def vertices_of(mesh):
    co = numpy.empty(len(mesh.vertices) * 3, dtype=numpy.float64)
    mesh.vertices.foreach_get("co", co)

    return co.reshape(-1, 3)


def transform_mesh(mesh, matrix):
    mesh.transform(matrix)
    mesh.update()


def bounds(points):
    return points.min(axis=0), points.max(axis=0)


def read_old(path):
    """The original file's one mesh node: its name, its world matrix and its geometry in the world."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in imported if o.type == "MESH"]

    if len(meshes) != 1:
        raise RuntimeError("%s has %d mesh nodes, not one" % (path, len(meshes)))

    node = meshes[0]
    matrix = node.matrix_world.copy()
    points = numpy.array([tuple(matrix @ v.co) for v in node.data.vertices])
    name = node.name
    data = node.data
    materials = [m for m in data.materials if m is not None]

    for obj in imported:
        bpy.data.objects.remove(obj)

    bpy.data.meshes.remove(data)

    for material in materials:
        if material.users == 0:
            bpy.data.materials.remove(material)

    return name, matrix, points


def main_angle(points):
    """The direction of a shape's long horizontal axis (radians), from its spread in X and Y."""
    xy = points[:, :2] - points[:, :2].mean(axis=0)
    values, vectors = numpy.linalg.eigh(numpy.cov(xy.T))
    main = vectors[:, int(numpy.argmax(values))]

    return math.atan2(main[1], main[0])


def square_angle(points):
    """The turn about Z that gives a shape its smallest footprint (a square base's edges along the axes)."""
    xy = points[:, :2] - points[:, :2].mean(axis=0)
    best = (float("inf"), 0.0)

    for degrees in numpy.arange(0.0, 90.0, 0.5):
        a = math.radians(degrees)
        c, s = math.cos(a), math.sin(a)
        turned = xy @ numpy.array([[c, s], [-s, c]])
        area = numpy.ptp(turned[:, 0]) * numpy.ptp(turned[:, 1])
        best = min(best, (area, a))

    return best[1]


def fit(mesh, old_points, mode):
    """Turn, scale and move the mesh (in the world) so that it stands where the old one stood, at its size by mode."""
    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(-Vector(( low + high ) / 2.0)))
    points = vertices_of(mesh)

    if mode == "footprint" and square_angle(old_points) == 0.0:
        # a square base: its edges along the axes, as the old one's
        transform_mesh(mesh, Matrix.Rotation(square_angle(points), 4, "Z"))
    else:
        # the long axis as the old one's
        transform_mesh(mesh, Matrix.Rotation(main_angle(old_points) - main_angle(points), 4, "Z"))

    points = vertices_of(mesh)
    low, high = bounds(points)
    old_low, old_high = bounds(old_points)
    size = high - low
    old_size = old_high - old_low

    if mode == "footprint":
        scale = max(old_size[0], old_size[1]) / max(size[0], size[1])
    else:
        scale = ( numpy.prod(old_size) / numpy.prod(size) ) ** ( 1.0 / 3.0 )

    transform_mesh(mesh, Matrix.Scale(scale, 4))
    points = vertices_of(mesh)
    low, high = bounds(points)
    target = Vector(( ( old_low[0] + old_high[0] ) / 2.0, ( old_low[1] + old_high[1] ) / 2.0, old_low[2] ))
    transform_mesh(mesh, Matrix.Translation(target - Vector(( ( low[0] + high[0] ) / 2.0, ( low[1] + high[1] ) / 2.0, low[2] ))))
    points = vertices_of(mesh)
    low, high = bounds(points)

    return scale, old_size, high - low


def grey_map(albedo_path, out_path):
    """A grey copy of a colour map, brought to GREY_MEAN on average (the deposits in a rock's shape take their element's
    colour over it)."""
    image = bpy.data.images.load(albedo_path)
    pixels = import_meshy.image_pixels(image)
    bpy.data.images.remove(image)
    linear = import_meshy.srgb_to_linear(pixels[..., :3])
    luminance = linear @ numpy.array([0.2126, 0.7152, 0.0722])
    grey = import_meshy.linear_to_srgb(luminance)
    grey = numpy.clip(grey * ( GREY_MEAN / max(float(grey.mean()), 1e-3) ), 0.0, 1.0)
    out = numpy.concatenate([numpy.repeat(grey[..., None], 3, axis=2), numpy.ones(grey.shape + (1,), dtype=numpy.float32)], axis=2)
    import_meshy.save_image(out, out_path)


def export(obj, path):
    """One prop's FBX, in metres in the file's own unit scale (as the terrain vehicle's)."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )


def render_preview(objects, path):
    """Each prop on its own, three-quarter from above, side by side."""
    scene = bpy.context.scene
    import_meshy.add_preview_lights()
    scene.render.engine = "CYCLES"
    scene.render.resolution_x = 420
    scene.render.resolution_y = 420
    camera = bpy.data.objects.new("Preview Camera", bpy.data.cameras.new("Preview Camera"))
    camera.data.type = "ORTHO"
    scene.collection.objects.link(camera)
    scene.camera = camera
    tiles = []

    for obj in objects:
        for other in objects:
            other.hide_render = other is not obj

        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        low = Vector([min(c[i] for c in corners) for i in range(3)])
        high = Vector([max(c[i] for c in corners) for i in range(3)])
        centre = ( low + high ) / 2.0
        size = ( high - low ).length
        camera.data.ortho_scale = size * 1.05
        d = Vector((0.9, -1.0, 0.8)).normalized()
        camera.location = centre + d * size * 2.0
        camera.rotation_euler = ( centre - camera.location ).to_track_quat("-Z", "Y").to_euler()
        tile = path + "." + obj.name + ".png"
        scene.render.filepath = tile
        bpy.ops.render.render(write_still=True)
        tiles.append(tile)

    images = [bpy.data.images.load(t) for t in tiles]
    sheet = numpy.concatenate([import_meshy.image_pixels(i) for i in images], axis=1)
    import_meshy.save_image(sheet, path)

    for image, tile in zip(images, tiles):
        bpy.data.images.remove(image)
        os.remove(tile)

    for obj in objects:
        obj.hide_render = False

    bpy.data.objects.remove(camera)


def main():
    arguments = ship_kit.script_arguments()

    if not arguments or "--source" not in arguments or "--old" not in arguments:
        raise SystemExit("usage: import_meshy_props.py -- <meshy-<props>.json> --source <folder with the GLB files> --old <folder with the original FBX files> [--preview <png>] [--no-export]")

    config_path = os.path.abspath(arguments[0])

    with open(config_path, encoding="utf-8") as config_file:
        config = json.load(config_file)

    source = arguments[arguments.index("--source") + 1]
    old_folder = os.path.abspath(arguments[arguments.index("--old") + 1])
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    do_export = "--no-export" not in arguments
    size = config.get("textureSize", 1024)

    if os.path.normcase(old_folder).startswith(os.path.normcase(os.path.join(ship_kit.PROJECT_ROOT, "Assets"))):
        raise SystemExit("--old must be a folder of copies of the original files, not the project's own")

    ship_kit.reset_scene()
    objects = []
    manifest = {"race": config.get("name", "props"), "vessels": [], "materials": [], "models": [], "rockMaps": []}

    for prop in config["props"]:
        name, matrix, old_points = read_old(os.path.join(old_folder, os.path.basename(prop["fbx"])))
        glb = import_meshy.import_glb(os.path.join(source, prop["glb"]), prop["material"] + " GLB")
        glb.data.materials[0].name = prop["material"]
        scale, old_size, new_size = fit(glb.data, old_points, prop.get("fit", "volume"))

        # the node as the old one, the mesh in its frame
        mesh = glb.data
        bpy.data.objects.remove(glb)
        transform_mesh(mesh, matrix.inverted())
        mesh.name = name
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.matrix_world = matrix
        objects.append(obj)
        triangles = sum(len(p.vertices) - 2 for p in mesh.polygons)
        print("PROP %s (node %s): scale %.4f, old size %s, new size %s, %d triangles" % (prop["material"], name, scale, tuple(round(float(v), 3) for v in old_size), tuple(round(float(v), 3) for v in new_size), triangles))

        if not do_export:
            continue

        maps = import_meshy.convert_maps(obj, prop["textures"], prop["material"], size, prop.get("glow", NO_GLOW))

        if prop.get("grey"):
            grey_path = prop["textures"] + "/" + prop["material"] + " - Grey.jpg"
            grey_map(os.path.join(ship_kit.PROJECT_ROOT, maps["Albedo"]), os.path.join(ship_kit.PROJECT_ROOT, grey_path))
            manifest["rockMaps"].append({"model": prop["fbx"], "grey": grey_path, "normal": maps.get("Normal", "")})

        export(obj, os.path.join(ship_kit.PROJECT_ROOT, prop["fbx"]))

        albedo = prop.get("albedo", [1.0, 1.0, 1.0])
        manifest["materials"].append({
            "name": prop["material"],
            "albedo": albedo,
            "specular": [1.0, 1.0, 1.0],
            "smoothness": 1.0,
            "emissive": [0.0, 0.0, 0.0],
            "folder": prop["materialFolder"],
            "albedoMap": maps.get("Albedo", ""),
            "specularMap": maps.get("Specular", ""),
            "normalMap": maps.get("Normal", ""),
            "emissiveMap": maps.get("Emissive", ""),
        })
        manifest["models"].append({"model": prop["fbx"], "materials": [prop["material"]], "meshy": prop.get("meshy", {})})

    if preview:
        render_preview(objects, os.path.abspath(preview))
        print("PREVIEW " + os.path.abspath(preview))

    if not do_export:
        return

    stem = os.path.splitext(os.path.basename(config_path))[0].replace("meshy-", "")
    manifest_path = os.path.join(os.path.dirname(config_path), "manifest-" + stem + ".json")

    with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
        json.dump(manifest, manifest_file, indent="\t")
        manifest_file.write("\n")

    print("MANIFEST " + manifest_path)


main()
