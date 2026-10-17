"""The textured terrain vehicle (Blender 5.2, background mode).

blender --background --factory-startup --python DevTools/ShipModels/import_meshy_tv.py -- DevTools/ShipModels/meshy-terrain-vehicle.json --source <folder> --old <original FBX> [--preview <png>] [--no-export]

TerrainVehicle turns and steers the six wheels through the nodes of Terrain Vehicle.fbx, which the scene's copy of the model
references by their names (the root, Suspension - FL to RR, Steering - FL to RR, Wheel 1 to 6, Laser and Radar). So the new
vehicle keeps the old file's tree: this script reads the tree from the original file (--old, a copy taken from git before the
first import, never the file it overwrites), gives the root the new body (one GLB, its wheels left off) and every Wheel node
the new wheel (a second GLB), and writes the FBX with the same names, transforms and units (metres, in the file's unit
scale: the scene overrides the root's scale).

The body is turned so that its front (its gold cowl) is at -Y as the old body's, and scaled so that its six axle hubs are as
far apart, front to back, as the old wheels. Each Suspension node then moves (the scene overrides none of their transforms) so
that its wheel is at its hub, sideways and along, and at the old wheels' height, so the wheels meet the ground where the old
ones did (the body's front hubs are lower than the others, so it rides at their average height). The wheel keeps the old
wheel's size, axle (the node's Z, which TerrainVehicle spins it about) and side (its hub face out, at the node's -Z). The
radar bar on the roof becomes the Radar node's mesh, turning on its mast as the old radar does (the scene gives that node a
rotator); the laser turret stays part of the body (nothing moves the Laser node, which is left empty).

The scene gives the root a capsule collider made for the old body (19 long, radius 4): the manifest carries the capsule for
the new size, the old one's proportions to the vehicle kept.

It also writes manifest-terrain-vehicle.json for wire.ps1: the two materials with their maps, and the capsule.
"""

import json
import math
import os
import sys

import bmesh
import bpy
import numpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import import_meshy  # noqa: E402
import ship_kit  # noqa: E402

TV_FBX = "Assets/Game Objects/Terrain Vehicle/Terrain Vehicle.fbx"
TEXTURES = "Assets/Game Objects/Terrain Vehicle/Textures"
MANIFEST = "manifest-terrain-vehicle.json"

ROOT = "Terrain Vehicle"
SIDES = ("FL", "FR", "ML", "MR", "RL", "RR")

# the wheel of each Steering node (TerrainVehicle.m_wheels is fr, fl, mr, ml, rr, rl: Wheel 1 to 6 are FR, MR, RR, FL, ML, RL)
WHEELS = {"FR": "Wheel 1", "MR": "Wheel 2", "RR": "Wheel 3", "FL": "Wheel 4", "ML": "Wheel 5", "RL": "Wheel 6"}

# the capsule collider the scene gives the root, in the root's frame of the old model (Unity's, which is the file's: Y along
# the vehicle, Z up)
OLD_CAPSULE_CENTRE = (0.0, -0.5, 5.0)
OLD_CAPSULE_HEIGHT = 19.0
OLD_CAPSULE_RADIUS = 4.0

BODY_MATERIAL = "Terrain Vehicle Meshy Body"
WHEEL_MATERIAL = "Terrain Vehicle Meshy Wheel"


def vertices_of(mesh):
    co = numpy.empty(len(mesh.vertices) * 3, dtype=numpy.float64)
    mesh.vertices.foreach_get("co", co)

    return co.reshape(-1, 3)


def transform_mesh(mesh, matrix):
    mesh.transform(matrix)
    mesh.update()


def bounds(points):
    return points.min(axis=0), points.max(axis=0)


def world_bounds(objects):
    """The bounds of these mesh objects in the world (the file's frame)."""
    points = [obj.matrix_world @ Vector(c) for obj in objects if obj.type == "MESH" and len(obj.data.vertices) for c in obj.bound_box]

    return Vector([min(p[i] for p in points) for i in range(3)]), Vector([max(p[i] for p in points) for i in range(3)])


def face_colours(obj):
    """Each face's centre and the base colour (sRGB) of the texel at its UV centre."""
    mesh = obj.data
    material = mesh.materials[0]
    bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    pixels = import_meshy.image_pixels(import_meshy.upstream_image(bsdf.inputs["Base Color"]))
    height, width = pixels.shape[:2]
    uv = mesh.uv_layers.active.data
    centres = numpy.empty((len(mesh.polygons), 3))
    colours = numpy.empty((len(mesh.polygons), 3))

    for i, polygon in enumerate(mesh.polygons):
        u = sum(uv[k].uv[0] for k in polygon.loop_indices) / polygon.loop_total
        v = sum(uv[k].uv[1] for k in polygon.loop_indices) / polygon.loop_total
        x = min(width - 1, max(0, int(( u % 1.0 ) * width)))
        y = min(height - 1, max(0, int(( v % 1.0 ) * height)))
        colours[i] = pixels[y, x, :3]
        centres[i] = polygon.center

    return centres, colours


def hue_saturation_value(colours):
    r, g, b = colours[:, 0], colours[:, 1], colours[:, 2]
    high = numpy.maximum(numpy.maximum(r, g), b)
    low = numpy.minimum(numpy.minimum(r, g), b)
    spread = numpy.maximum(high - low, 1e-6)
    hue = numpy.where(high == r, (g - b) / spread % 6.0, numpy.where(high == g, (b - r) / spread + 2.0, (r - g) / spread + 4.0)) * 60.0
    saturation = numpy.where(high > 0.0, (high - low) / numpy.maximum(high, 1e-6), 0.0)

    return hue, saturation, high


def read_old_tree(path):
    """The original file's nodes: name -> (parent name, matrix_basis, matrix_parent_inverse), each old wheel's size, the old
    wheels' middles (their meshes sit off their nodes) in the root's frame, and the old vehicle's bounds in the world."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    tree = {}
    wheel_size = None
    root = next(o for o in imported if o.name == ROOT)
    centres = {}

    for obj in imported:
        tree[obj.name] = (obj.parent.name if obj.parent else None, obj.matrix_basis.copy(), obj.matrix_parent_inverse.copy(), obj.type)

        if obj.name == "Wheel 1":
            wheel_size = tuple(obj.dimensions)

    for side, name in WHEELS.items():
        low, high = world_bounds([next(o for o in imported if o.name == name)])
        centres[side] = root.matrix_world.inverted() @ ( ( low + high ) / 2.0 )

    old_bounds = world_bounds(imported)
    meshes = {o.data for o in imported if o.type == "MESH"}
    materials = {m for mesh in meshes for m in mesh.materials if m is not None}

    for obj in imported:
        bpy.data.objects.remove(obj)

    for mesh in meshes:
        bpy.data.meshes.remove(mesh)

    for material in materials:
        bpy.data.materials.remove(material)

    for name in (ROOT, "Laser", "Radar") + tuple("Suspension - " + s for s in SIDES) + tuple("Steering - " + s for s in SIDES) + tuple(WHEELS.values()):
        if name not in tree:
            raise RuntimeError("the original file has no node " + name)

    return tree, wheel_size, centres, old_bounds


def build_nodes(tree):
    """The old tree made anew: the root, the radar and the wheels with meshes (filled in later), every other node empty."""
    scene = bpy.context.scene
    nodes = {}

    def make(name):
        if name in nodes:
            return nodes[name]

        parent_name, basis, parent_inverse, kind = tree[name]
        has_mesh = ( name in (ROOT, "Radar") ) or ( name in WHEELS.values() )
        obj = bpy.data.objects.new(name, bpy.data.meshes.new(name) if has_mesh else None)
        scene.collection.objects.link(obj)

        if parent_name is not None:
            obj.parent = make(parent_name)
            obj.matrix_parent_inverse = parent_inverse

        obj.matrix_basis = basis
        nodes[name] = obj

        return obj

    for name in tree:
        make(name)

    bpy.context.view_layer.update()

    return nodes


def orient_body(obj):
    """Centre the body, lay its length along Y with its front (the gold cowl) at -Y and its bottom at z 0."""
    mesh = obj.data
    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(-Vector(( low + high ) / 2.0)))

    # the long horizontal axis goes along Y
    points = vertices_of(mesh)
    low, high = bounds(points)

    if ( high[0] - low[0] ) > ( high[1] - low[1] ):
        transform_mesh(mesh, Matrix.Rotation(math.radians(90.0), 4, "Z"))

    # the front is the end with the gold cowl
    centres, colours = face_colours(obj)
    hue, saturation, value = hue_saturation_value(colours)
    gold = ( hue > 30.0 ) & ( hue < 65.0 ) & ( saturation > 0.3 ) & ( value > 0.25 )

    if gold.sum() < 20:
        raise RuntimeError("no gold cowl found on the body (%d faces)" % gold.sum())

    gold_y = float(centres[gold, 1].mean())

    if gold_y > 0.0:
        transform_mesh(mesh, Matrix.Rotation(math.radians(180.0), 4, "Z"))

    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(Vector((-( low[0] + high[0] ) / 2.0, -( low[1] + high[1] ) / 2.0, -low[2]))))
    print("BODY gold faces=%d at y=%.3f (front at -Y)" % (gold.sum(), -abs(gold_y)))


def find_hubs(points):
    """The six axle hubs: the outermost low geometry on each side, in three groups front to back. Returns per side
    ("L" at +X, "R" at -X) the hubs front to back as (tip |x|, y, z)."""
    low, high = bounds(points)
    half_width = numpy.abs(points[:, 0]).max()
    height = high[2] - low[2]
    length = high[1] - low[1]
    hubs = {}

    for side, sign in (("L", 1.0), ("R", -1.0)):
        outer = points[( points[:, 0] * sign > 0.75 * half_width ) & ( points[:, 2] < low[2] + 0.55 * height )]

        if len(outer) < 30:
            raise RuntimeError("too few hub points on side %s (%d)" % (side, len(outer)))

        ys = numpy.sort(outer[:, 1])
        gaps = numpy.diff(ys)
        # the two widest gaps along the side split the three hubs
        cuts = numpy.sort(numpy.argsort(gaps)[-2:])
        edges = [ys[0] - 1.0, ( ys[cuts[0]] + ys[cuts[0] + 1] ) / 2.0, ( ys[cuts[1]] + ys[cuts[1] + 1] ) / 2.0, ys[-1] + 1.0]
        group = []

        for a, b in zip(edges[:-1], edges[1:]):
            part = outer[( outer[:, 1] > a ) & ( outer[:, 1] <= b )]
            group.append((float(numpy.abs(part[:, 0]).max()), float(( part[:, 1].min() + part[:, 1].max() ) / 2.0), float(( part[:, 2].min() + part[:, 2].max() ) / 2.0)))

        hubs[side] = group
        print("HUBS %s %s (gaps %.3f %.3f of a length %.3f)" % (side, [tuple(round(v, 3) for v in h) for h in group], gaps[cuts[0]], gaps[cuts[1]], length))

    return hubs


def orient_wheel(obj, diameter):
    """Centre the wheel, lay its axle along Z with its hub face at -Z, and scale it to the old wheel's diameter; returns its
    width."""
    mesh = obj.data
    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(-Vector(( low + high ) / 2.0)))
    size = high - low
    axle = int(numpy.argmin(size))

    if axle == 0:
        transform_mesh(mesh, Matrix.Rotation(math.radians(90.0), 4, "Y"))
    elif axle == 1:
        transform_mesh(mesh, Matrix.Rotation(math.radians(90.0), 4, "X"))

    # the hub face is the side whose middle is brighter (the metal hub against the dark tire's inner wall)
    centres, colours = face_colours(obj)
    radius = numpy.hypot(centres[:, 0], centres[:, 1])
    middle = radius < 0.3 * radius.max()
    brightness = colours.max(axis=1)
    front = middle & ( centres[:, 2] < 0.0 )
    back = middle & ( centres[:, 2] > 0.0 )
    front_bright = float(brightness[front].mean()) if front.any() else 0.0
    back_bright = float(brightness[back].mean()) if back.any() else 0.0

    if back_bright > front_bright:
        transform_mesh(mesh, Matrix.Rotation(math.radians(180.0), 4, "X"))

    points = vertices_of(mesh)
    low, high = bounds(points)
    scale = diameter / max(high[0] - low[0], high[1] - low[1])
    transform_mesh(mesh, Matrix.Scale(scale, 4))
    points = vertices_of(mesh)
    low, high = bounds(points)
    print("WHEEL axle=%s hub brightness %.3f against %.3f, scale %.4f, size %s" % ("XYZ"[axle], max(front_bright, back_bright), min(front_bright, back_bright), scale, tuple(round(float(v), 3) for v in high - low)))

    return float(high[2] - low[2])


def place_body(nodes, body, old_centres):
    """Scale and move the body (in the root's frame) so that its hubs are as far apart front to back as the old wheels and
    at their height on average; returns the hubs in the root's frame."""
    root = nodes[ROOT]
    points = vertices_of(body.data)
    hubs = find_hubs(points)

    old_ys = sorted({round(c.y, 3) for c in old_centres.values()})
    old_span = old_ys[-1] - old_ys[0]
    new_span = numpy.mean([hubs[s][2][1] - hubs[s][0][1] for s in ("L", "R")])
    scale = old_span / new_span
    transform_mesh(body.data, Matrix.Scale(scale, 4))

    hub_z = numpy.mean([h[2] for s in ("L", "R") for h in hubs[s]]) * scale
    hub_y = numpy.mean([( hubs[s][0][1] + hubs[s][2][1] ) / 2.0 for s in ("L", "R")]) * scale
    old_z = float(numpy.mean([c.z for c in old_centres.values()]))
    old_y = ( old_ys[0] + old_ys[-1] ) / 2.0
    transform_mesh(body.data, Matrix.Translation(Vector((0.0, old_y - hub_y, old_z - hub_z))))

    placed = {s: [(h[0] * scale, h[1] * scale + old_y - hub_y, h[2] * scale + old_z - hub_z) for h in hubs[s]] for s in ("L", "R")}

    # the root keeps the body's mesh; the body arrives in the root's frame
    old = root.data
    root.data = body.data
    bpy.data.meshes.remove(old)
    bpy.data.objects.remove(body)
    print("BODY scale %.4f, hubs %.3f apart (old wheels %.3f), lifted %.3f, length now %.3f" % (scale, new_span * scale, old_span, old_z - hub_z, float(numpy.ptp(vertices_of(root.data)[:, 1]))))

    return placed


def wheel_centre(nodes, side):
    """The wheel's middle in the root's frame (the new wheel mesh is centred on its node)."""
    root = nodes[ROOT]
    wheel = nodes[WHEELS[side]]

    return root.matrix_world.inverted() @ wheel.matrix_world.translation


def mount_wheels(nodes, wheel_mesh, placed, wheel_width, inset, old_height):
    """Give every Wheel node the wheel and move each Suspension node so that its wheel's middle is at its hub sideways and
    along (the hub's tip goes into the wheel by inset of the wheel's width) and at the old wheels' height."""
    root = nodes[ROOT]

    for side in SIDES:
        wheel = nodes[WHEELS[side]]
        old = wheel.data
        wheel.data = wheel_mesh
        bpy.data.meshes.remove(old)

    bpy.context.view_layer.update()

    rows = {"F": 0, "M": 1, "R": 2}

    for side in SIDES:
        hub = placed["L" if side[1] == "L" else "R"][rows[side[0]]]
        sign = 1.0 if side[1] == "L" else -1.0
        # sideways and along at the hub, up at the old wheels' height (they all meet the ground where the old ones did)
        target = Vector((sign * ( hub[0] + ( 0.5 - inset ) * wheel_width ), hub[1], old_height))
        current = wheel_centre(nodes, side)
        suspension = nodes["Suspension - " + side]
        # the move in the root's frame, carried into the suspension's parent frame (the root)
        suspension.matrix_world = root.matrix_world @ Matrix.Translation(target - current) @ root.matrix_world.inverted() @ suspension.matrix_world
        bpy.context.view_layer.update()
        moved = wheel_centre(nodes, side)
        print("MOUNT %s %s: wheel at %s (hub tip %.3f, hub %.3f off the wheel's middle)" % (side, WHEELS[side], tuple(round(v, 3) for v in moved), hub[0], hub[2] - old_height))


def split_radar(nodes):
    """Cut the radar bar on the roof out of the body into the Radar node's mesh, its mast on the node's origin (the node turns
    it about its Z, upright, as the old radar turned)."""
    root = nodes[ROOT]
    radar = nodes["Radar"]
    mesh = root.data
    points = vertices_of(mesh)
    top = points[:, 2].max()
    radar_y = float(points[points[:, 2] > top - 0.05, 1].mean())

    # the roof's height away from the radar; the radar is what stands above it
    away = points[numpy.abs(points[:, 1] - radar_y) > 1.5]
    roof = float(away[:, 2].max()) if len(away) else top - 0.5
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    cut = [f for f in bm.faces if min(v.co.z for v in f.verts) > roof + 0.02 and abs(f.calc_center_median().y - radar_y) < 1.5]

    if len(cut) < 10:
        print("RADAR not found above the roof (%d faces): left on the body" % len(cut))
        bm.free()

        return

    # the radar's own mesh: the cut faces, copied with their UVs
    radar_bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.active
    radar_uv = radar_bm.loops.layers.uv.new("UVMap")
    copies = {}

    for face in cut:
        verts = []

        for v in face.verts:
            if v.index not in copies:
                copies[v.index] = radar_bm.verts.new(v.co)
            verts.append(copies[v.index])

        try:
            new_face = radar_bm.faces.new(verts)
        except ValueError:
            continue

        for loop, new_loop in zip(face.loops, new_face.loops):
            new_loop[radar_uv].uv = loop[uv_layer].uv

    bmesh.ops.delete(bm, geom=cut, context="FACES")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    radar_mesh = radar.data
    radar_bm.to_mesh(radar_mesh)
    radar_bm.free()
    radar_mesh.materials.append(mesh.materials[0])
    radar_points = vertices_of(radar_mesh)
    low, high = bounds(radar_points)
    pivot = Vector(( ( low[0] + high[0] ) / 2.0, ( low[1] + high[1] ) / 2.0, roof ))
    transform_mesh(radar_mesh, Matrix.Translation(-pivot))

    # the node at the mast's foot, upright in the root's frame
    radar.parent = root
    radar.matrix_parent_inverse = Matrix.Identity(4)
    radar.matrix_basis = Matrix.Translation(pivot)
    bpy.context.view_layer.update()
    print("RADAR %d faces cut above the roof at %.3f, %.3f wide, pivot %s" % (len(cut), roof, float(high[0] - low[0]), tuple(round(v, 3) for v in pivot)))


def capsule_for(nodes, old_bounds):
    """The root's capsule collider for the new vehicle: the old one's centre moved by as much as the vehicle's middle moved,
    and its length and radius scaled by the vehicle's length and width, all in the world (the file's frame, Unity's root's)."""
    new_bounds = world_bounds(nodes.values())
    old_size = old_bounds[1] - old_bounds[0]
    new_size = new_bounds[1] - new_bounds[0]
    old_middle = ( old_bounds[0] + old_bounds[1] ) / 2.0
    new_middle = ( new_bounds[0] + new_bounds[1] ) / 2.0
    centre = [round(OLD_CAPSULE_CENTRE[0], 3), round(OLD_CAPSULE_CENTRE[1] + new_middle.y - old_middle.y, 3), round(OLD_CAPSULE_CENTRE[2] + new_middle.z - old_middle.z, 3)]
    height = round(OLD_CAPSULE_HEIGHT * new_size.y / old_size.y, 3)
    radius = round(OLD_CAPSULE_RADIUS * new_size.x / old_size.x, 3)
    print("CAPSULE old size %s, new size %s: centre %s height %.3f radius %.3f" % (tuple(round(v, 3) for v in old_size), tuple(round(v, 3) for v in new_size), centre, height, radius))

    return centre, height, radius


def export(path):
    """The FBX in metres in the file's own unit scale, as the old file was: the scene overrides the root's scale (0.5), so a
    unit scale put on the root's transform (FBX_SCALE_NONE, the Arth ship's setting) would be lost and the model would come
    in a hundred times too small."""
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"EMPTY", "MESH"},
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


def render_preview(nodes, path):
    """Three views of the assembled vehicle (front three-quarter, side, top), lit for review."""
    scene = bpy.context.scene
    import_meshy.add_preview_lights()
    scene.render.engine = "CYCLES"
    scene.render.resolution_x = 700
    scene.render.resolution_y = 520
    low, high = world_bounds(nodes.values())
    centre = ( low + high ) / 2.0
    size = ( high - low ).length
    camera = bpy.data.objects.new("Preview Camera", bpy.data.cameras.new("Preview Camera"))
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = size * 0.9
    scene.collection.objects.link(camera)
    scene.camera = camera
    tiles = []

    for name, direction in (("front", (0.9, -1.0, 0.75)), ("side", (1.0, 0.0, 0.08)), ("top", (0.0, 0.0001, 1.0))):
        d = Vector(direction).normalized()
        camera.location = centre + d * size * 2.0
        camera.rotation_euler = ( centre - camera.location ).to_track_quat("-Z", "Y").to_euler()
        tile = path + "." + name + ".png"
        scene.render.filepath = tile
        bpy.ops.render.render(write_still=True)
        tiles.append(tile)

    images = [bpy.data.images.load(t) for t in tiles]
    sheet = numpy.concatenate([import_meshy.image_pixels(i) for i in images], axis=1)
    import_meshy.save_image(sheet, path)

    for image, tile in zip(images, tiles):
        bpy.data.images.remove(image)
        os.remove(tile)

    bpy.data.objects.remove(camera)


def main():
    arguments = ship_kit.script_arguments()

    if not arguments or "--source" not in arguments or "--old" not in arguments:
        raise SystemExit("usage: import_meshy_tv.py -- <meshy-terrain-vehicle.json> --source <folder with the GLB files> --old <original FBX> [--preview <png>] [--no-export]")

    with open(os.path.abspath(arguments[0]), encoding="utf-8") as config_file:
        config = json.load(config_file)

    source = arguments[arguments.index("--source") + 1]
    old_path = os.path.abspath(arguments[arguments.index("--old") + 1])
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    do_export = "--no-export" not in arguments
    size = config.get("textureSize", 1024)

    if os.path.normcase(old_path) == os.path.normcase(os.path.join(ship_kit.PROJECT_ROOT, TV_FBX)):
        raise SystemExit("--old must be a copy of the original file, not the file this script writes")

    ship_kit.reset_scene()
    tree, old_wheel_size, old_centres, old_bounds = read_old_tree(old_path)
    nodes = build_nodes(tree)
    print("OLD wheel %s, middles %s, bounds %s to %s" % (tuple(round(v, 3) for v in old_wheel_size), {s: tuple(round(v, 3) for v in c) for s, c in old_centres.items()}, tuple(round(v, 3) for v in old_bounds[0]), tuple(round(v, 3) for v in old_bounds[1])))

    body = import_meshy.import_glb(os.path.join(source, config["body"]["glb"]), "Body GLB")
    body.data.materials[0].name = BODY_MATERIAL
    orient_body(body)
    placed = place_body(nodes, body, old_centres)

    wheel = import_meshy.import_glb(os.path.join(source, config["wheel"]["glb"]), "Wheel GLB")
    wheel.data.materials[0].name = WHEEL_MATERIAL
    wheel_width = orient_wheel(wheel, max(old_wheel_size[0], old_wheel_size[1]))
    wheel_mesh = wheel.data
    bpy.data.objects.remove(wheel)
    wheel_mesh.name = "Wheel"
    mount_wheels(nodes, wheel_mesh, placed, wheel_width, config.get("wheelInset", 0.35), float(numpy.mean([c.z for c in old_centres.values()])))
    split_radar(nodes)
    capsule = capsule_for(nodes, old_bounds)

    print("TRIANGLES body=%d radar=%d wheel=%d" % (sum(len(p.vertices) - 2 for p in nodes[ROOT].data.polygons), sum(len(p.vertices) - 2 for p in nodes["Radar"].data.polygons), sum(len(p.vertices) - 2 for p in wheel_mesh.polygons)))

    maps = {}

    if do_export:
        maps[BODY_MATERIAL] = import_meshy.convert_maps(nodes[ROOT], TEXTURES, "Terrain Vehicle Body", size, config.get("glow"))
        # (nothing on the wheel glows)
        maps[WHEEL_MATERIAL] = import_meshy.convert_maps(nodes["Wheel 1"], TEXTURES, "Terrain Vehicle Wheel", size, {"hue": [0.0, 0.0], "saturation": 2.0, "value": 2.0})

    if preview:
        render_preview(nodes, os.path.abspath(preview))
        print("PREVIEW " + os.path.abspath(preview))

    if not do_export:
        return

    export(os.path.join(ship_kit.PROJECT_ROOT, TV_FBX))

    manifest = {
        "race": "Terrain Vehicle",
        "vessels": [],
        "materials": [],
        "terrainVehicle": {
            "model": TV_FBX,
            "materials": list(maps.keys()),
            "colliderCentre": capsule[0],
            "colliderHeight": capsule[1],
            "colliderRadius": capsule[2],
            "meshy": {"body": config["body"].get("meshy", {}), "wheel": config["wheel"].get("meshy", {})},
        },
    }

    for name, paths in maps.items():
        manifest["materials"].append({
            "name": name,
            "albedo": [1.0, 1.0, 1.0],
            "specular": [1.0, 1.0, 1.0],
            "smoothness": 1.0,
            "emissive": [0.0, 0.0, 0.0],
            "folder": "Assets/Game Objects/Terrain Vehicle",
            "albedoMap": paths.get("Albedo", ""),
            "specularMap": paths.get("Specular", ""),
            "normalMap": paths.get("Normal", ""),
            "emissiveMap": paths.get("Emissive", ""),
        })

    manifest_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), MANIFEST)

    with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
        json.dump(manifest, manifest_file, indent="\t")
        manifest_file.write("\n")

    print("MANIFEST " + manifest_path)


main()
