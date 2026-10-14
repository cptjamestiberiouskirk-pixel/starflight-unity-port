"""The player's Arth ship made with Meshy (Blender 5.2, background mode).

blender --background --factory-startup --python DevTools/ShipModels/import_meshy_arth.py -- DevTools/ShipModels/meshy-arth.json --source <folder> [--preview <png>] [--no-export]

The game shows the ship's equipment: four scripts (PlayerShip, StatusDisplay, Disembarked, ShipConfigurationPanel) switch the
16 cargo pods, the laser cannon and the missile launchers on and off, through the nodes of Arth Ship.FBX that the scenes
reference by their names. So the new ship keeps the old file's tree: this script makes that tree anew (never from the file,
which it overwrites), puts the Meshy hull (one GLB) where the old hull was, splits the Meshy sheet of parts (a second GLB: a
cargo pod, a laser turret and a missile launcher) and puts them where the hull has room for them, and writes the FBX with
the same names, the same root and the same units. The old file is in inches; Blender writes centimetres, so the parts go out in centimetres under a root of
0.01 (a round trip of the old model through these settings gives the same transforms and bounds in Unity).

It also writes manifest-arth.json for wire.ps1: the two materials with their maps, and where the engines' contrails go.
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

ARTH_FBX = "Assets/Game Objects/Ships/Arth/Arth Ship.FBX"
TEXTURES = "Assets/Game Objects/Ships/Arth/Textures"

# the old file's units: inches, under a root of 0.0254 when Blender imports it
INCH = 2.54

# the old hull, in inches in the root's frame: the nose (the cockpit) at -Y, the stern (the engine fork) at +Y
HULL_FRONT = -100.0
HULL_BACK = 34.0

# half the depth of the slabs the hull is measured in (inches)
SLAB = 2.5

# the parts' sizes against the hull's length
POD_RINGS = 4
TURRET_LENGTH = 0.105
LAUNCHER_LENGTH = 0.11


def vertices_of(mesh):
    co = numpy.empty(len(mesh.vertices) * 3, dtype=numpy.float64)
    mesh.vertices.foreach_get("co", co)

    return co.reshape(-1, 3)


def transform_mesh(mesh, matrix):
    mesh.transform(matrix)
    mesh.update()


def bounds(points):
    return points.min(axis=0), points.max(axis=0)


def turn_to_nose_minus_y(mesh, nose):
    """Turn a mesh about Z so that the direction nose (+X, -X, +Y or -Y) becomes -Y."""
    angle = {"+X": -90.0, "-X": 90.0, "+Y": 180.0, "-Y": 0.0}[nose]
    transform_mesh(mesh, Matrix.Rotation(math.radians(angle), 4, "Z"))


def hull_nose(points):
    """The hull's nose is its narrower end along its longest horizontal axis (the engine fork is the wide end)."""
    low, high = bounds(points)
    axis = 0 if ( high[0] - low[0] ) >= ( high[1] - low[1] ) else 1
    lateral = 1 - axis
    middle = ( low + high ) / 2.0
    span = high[axis] - low[axis]
    first = points[points[:, axis] < low[axis] + 0.1 * span]
    last = points[points[:, axis] > high[axis] - 0.1 * span]
    first_width = numpy.abs(first[:, lateral] - middle[lateral]).max()
    last_width = numpy.abs(last[:, lateral] - middle[lateral]).max()
    name = "XY"[axis]

    return ("-" if first_width < last_width else "+") + name


def build_nodes():
    """The old Arth Ship.FBX's tree, made anew (not read from the file, which this script overwrites): the root "Arth Ship"
    at 0.0254 (inches), the Hull, the Laser Cannon with the Laser and the Laser Base, the Missile Launchers with the left and
    the right launcher, and Pod 01 to Pod 16. Unity keeps each node's file id by its name, so the scenes' references stay."""
    scene = bpy.context.scene

    def node(name, parent, mesh):
        data = bpy.data.meshes.new(name) if mesh else None
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.parent = parent

        return obj

    root = node("Arth Ship", None, False)
    root.scale = (0.0254, 0.0254, 0.0254)
    nodes = {"Arth Ship": root, "Hull": node("Hull", root, True)}
    nodes["Laser Cannon"] = node("Laser Cannon", root, False)
    nodes["Laser"] = node("Laser", nodes["Laser Cannon"], True)

    # the old cannon had a separate base; the Meshy turret is one piece, so its base node stays as an empty node
    nodes["Laser Base"] = node("Laser Base", nodes["Laser Cannon"], False)
    nodes["Missile Launchers"] = node("Missile Launchers", root, False)
    nodes["Missile Launcher - Left"] = node("Missile Launcher - Left", nodes["Missile Launchers"], True)
    nodes["Missile Launcher - Right"] = node("Missile Launcher - Right", nodes["Missile Launchers"], True)

    for number in range(1, 17):
        nodes["Pod %02d" % number] = node("Pod %02d" % number, root, True)

    return nodes


def place_hull(hull_obj, glb_obj):
    """Put the Meshy hull where the old hull was, in the root's frame (inches), and give the old Hull node its mesh."""
    mesh = glb_obj.data
    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(-Vector(( low + high ) / 2.0)))
    turn_to_nose_minus_y(mesh, hull_nose(vertices_of(mesh)))

    points = vertices_of(mesh)
    low, high = bounds(points)
    scale = ( HULL_BACK - HULL_FRONT ) / ( high[1] - low[1] )
    transform_mesh(mesh, Matrix.Scale(scale, 4))
    points = vertices_of(mesh)
    low, high = bounds(points)
    transform_mesh(mesh, Matrix.Translation(Vector(( -( low[0] + high[0] ) / 2.0, HULL_FRONT - low[1], -( low[2] + high[2] ) / 2.0 ))))

    # the Hull node sits at the root's origin with no turn: its mesh is the hull as placed
    old = hull_obj.data
    hull_obj.data = mesh
    bpy.data.meshes.remove(old)
    bpy.data.objects.remove(glb_obj)

    return vertices_of(mesh)


class HullShape:
    """Where the hull has room for the parts: its spine, the fork, the tops and sides, in the root's frame (inches)."""

    def __init__(self, points):
        self.points = points
        # slabs of 5 inches every inch (a remeshed spine is a long plain cylinder with few vertices)
        ys = numpy.arange(HULL_FRONT, HULL_BACK + 0.5, 1.0)
        slabs = [self.slab(y, SLAB) for y in ys]
        self.half_width = numpy.array([numpy.abs(slab[:, 0]).max() if len(slab) else 0.0 for slab in slabs])
        self.ys = ys

        # the spine: the longest run of narrow slabs between the cockpit and the fork (an empty slab is not narrow)
        narrow = ( self.half_width > 0.0 ) & ( self.half_width < 0.3 * self.half_width.max() )
        best = (0, 0)
        start = None

        for i, flag in enumerate(list(narrow) + [False]):
            if flag and start is None:
                start = i
            elif not flag and start is not None:
                if i - start > best[1] - best[0]:
                    best = (start, i)
                start = None

        self.spine_front = float(ys[best[0]])
        self.spine_back = float(ys[best[1] - 1])
        spine = self.half_width[best[0]:best[1]]
        self.spine_half_width = float(numpy.median(spine))
        middles = [float(( slab[:, 2].min() + slab[:, 2].max() ) / 2.0) for slab in slabs[best[0]:best[1]] if len(slab)]
        self.spine_z = float(numpy.median(middles))

    def slab(self, y, depth=1.0):
        return self.points[numpy.abs(self.points[:, 1] - y) < depth]

    def top(self, x, y, radius=SLAB):
        near = self.points[( numpy.abs(self.points[:, 0] - x) < radius ) & ( numpy.abs(self.points[:, 1] - y) < radius )]

        return float(near[:, 2].max()) if len(near) else 0.0

    def outer_side(self, y, depth=2.0):
        """The outer edge of the fork's arm at y (positive X side) and the arm's middle height there."""
        slab = self.slab(y, depth)
        outer = slab[slab[:, 0] > 0.6 * slab[:, 0].max()]

        return float(slab[:, 0].max()), float(( outer[:, 2].min() + outer[:, 2].max() ) / 2.0)


def split_parts(obj):
    """Split the sheet of parts into its three items, grouped by where their faces are; returns pod, turret, launcher meshes."""
    mesh = obj.data
    centres = numpy.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)

    # the items stand apart along the sheet: the two widest gaps along its main direction separate them
    flat = centres[:, :2] - centres[:, :2].mean(axis=0)
    direction = numpy.linalg.svd(flat, full_matrices=False)[2][0]
    along = flat @ direction
    order = numpy.sort(along)
    gaps = numpy.diff(order)
    cuts = sorted(order[i] + gaps[i] / 2.0 for i in numpy.argsort(gaps)[-2:])
    labels = numpy.digitize(along, cuts)

    pieces = []

    for label in range(3):
        copy = mesh.copy()
        bm = bmesh.new()
        bm.from_mesh(copy)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if labels[f.index] != label], context="FACES")
        bm.to_mesh(copy)
        bm.free()
        points = vertices_of(copy)
        low, high = bounds(points)
        pieces.append((float(numpy.prod(high - low)), copy))
        print("PART %d faces=%d size=%s" % (label, len(copy.polygons), tuple(round(v, 3) for v in high - low)))

    # by size: the cargo pod is the biggest, the turret the smallest
    pieces.sort(key=lambda piece: piece[0])

    return pieces[2][1], pieces[0][1], pieces[1][1]


def centre_on_origin(mesh):
    low, high = bounds(vertices_of(mesh))
    transform_mesh(mesh, Matrix.Translation(-Vector(( low + high ) / 2.0)))


def blue_direction(mesh, image):
    """The mean normal of the faces whose colour is the parts' blue (the launcher's tubes): which way the tubes face."""
    if image is None:
        return None

    width, height = image.size
    pixels = import_meshy.image_pixels(image)
    uv = mesh.uv_layers.active

    if uv is None:
        return None

    total = Vector((0.0, 0.0, 0.0))

    for polygon in mesh.polygons:
        u = sum(uv.data[i].uv[0] for i in polygon.loop_indices) / polygon.loop_total
        v = sum(uv.data[i].uv[1] for i in polygon.loop_indices) / polygon.loop_total
        r, g, b = pixels[min(height - 1, max(0, int(v * height))), min(width - 1, max(0, int(u * width))), :3]

        # the caps' bright blue, not the darker blue of the side panels (measured: caps about 0.5 to 0.6 bright)
        if b > 0.45 and ( b - min(r, g) ) / b > 0.45:
            total += polygon.normal * polygon.area

    return total if total.length > 0.0 else None


def area_centre(mesh):
    """The middle of a mesh's surface, each face weighted by its area."""
    total = Vector((0.0, 0.0, 0.0))
    area = 0.0

    for polygon in mesh.polygons:
        total += polygon.center * polygon.area
        area += polygon.area

    return total / area if area > 0.0 else total


def face_forward(mesh, direction):
    """Turn a mesh about Z so that its horizontal direction faces -Y (forward)."""
    if direction is None or Vector((direction.x, direction.y)).length < 1e-6:
        return

    angle = math.atan2(direction.y, direction.x)
    transform_mesh(mesh, Matrix.Rotation(-math.pi / 2.0 - angle, 4, "Z"))


def lay_along_y(mesh):
    """Turn a mesh about Z so that its longest horizontal side runs along Y."""
    low, high = bounds(vertices_of(mesh))

    if ( high[0] - low[0] ) > ( high[1] - low[1] ):
        transform_mesh(mesh, Matrix.Rotation(math.pi / 2.0, 4, "Z"))


def mirrored(mesh, name):
    """A copy of a mesh mirrored across X, its faces turned so that they still face out."""
    copy = mesh.copy()
    copy.name = name
    transform_mesh(copy, Matrix.Scale(-1.0, 4, (1.0, 0.0, 0.0)))
    bm = bmesh.new()
    bm.from_mesh(copy)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(copy)
    bm.free()

    return copy


def set_part(obj, mesh, place):
    """Give a node a mesh placed at place (its parent's frame), with no turn or scale of its own."""
    old = obj.data
    obj.data = mesh
    obj.location = place
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)

    if old is not None and old.users == 0:
        bpy.data.meshes.remove(old)


def scaled_copy(mesh, factor, name):
    copy = mesh.copy()
    copy.name = name
    transform_mesh(copy, Matrix.Scale(factor, 4))

    return copy


def assemble(nodes, hull, pod, turret, launcher, parts_image):
    length = HULL_BACK - HULL_FRONT

    # ---- cargo pods: the Meshy spine is thicker and shorter than the old one, so the 16 pods go in rings of four, an upper and
    # a lower pod on each side (from above still two rows along the spine, as in the original's picture), each pod's top
    # facing out; ring 1 is nearest the fork, and in each ring the upper pods come first: pod 4r+1 upper -X, 4r+2 upper +X,
    # 4r+3 lower -X, 4r+4 lower +X
    lay_along_y(pod)
    centre_on_origin(pod)
    low, high = bounds(vertices_of(pod))
    spine_length = hull.spine_back - hull.spine_front
    margin = 0.06 * spine_length
    pitch = ( spine_length - 2.0 * margin ) / POD_RINGS
    pod_scale = 0.9 * pitch / ( high[1] - low[1] )
    sized = scaled_copy(pod, pod_scale, "Pod Sized")
    left_pod = sized.copy()
    left_pod.name = "Pod Left"
    transform_mesh(left_pod, Matrix.Rotation(math.radians(-90.0), 4, "Y"))
    right_pod = sized.copy()
    right_pod.name = "Pod Right"
    transform_mesh(right_pod, Matrix.Rotation(math.radians(90.0), 4, "Y"))
    bpy.data.meshes.remove(sized)

    # turned on its side, the pod's width is its height and its height how far it stands out from the spine
    low, high = bounds(vertices_of(right_pod))
    pod_height = high[2] - low[2]
    pod_depth = high[0] - low[0]
    x = hull.spine_half_width + 0.45 * pod_depth
    z = 0.52 * pod_height

    for ring in range(POD_RINGS):
        y = hull.spine_back - margin - pitch * ( ring + 0.5 )

        for index, (side, level, mesh) in enumerate(((-1.0, 1.0, left_pod), (1.0, 1.0, right_pod), (-1.0, -1.0, left_pod), (1.0, -1.0, right_pod))):
            obj = nodes["Pod %02d" % ( 4 * ring + index + 1 )]
            set_part(obj, mesh, Vector((side * x, y, hull.spine_z + level * z)))

    print("PODS spine y=%.1f..%.1f half width=%.2f pitch=%.2f pod height=%.2f depth=%.2f" % (hull.spine_front, hull.spine_back, hull.spine_half_width, pitch, pod_height, pod_depth))

    # ---- the laser turret on top of the fork's hub, its barrels forward (-Y): the thin barrels stretch the bounds but add
    # little area, so they lie from the turret's middle by area towards the middle of its bounds (a remesh puts many
    # vertices on thin parts, so a mean of the vertices would point the other way)
    centre_on_origin(turret)
    low, high = bounds(vertices_of(turret))
    face_forward(turret, Vector(( low + high ) / 2.0) - area_centre(turret))
    centre_on_origin(turret)
    low, high = bounds(vertices_of(turret))
    middle = area_centre(turret)
    print("TURRET barrels forward=%s" % ( ( middle.y - low[1] ) > ( high[1] - middle.y ) ))
    low, high = bounds(vertices_of(turret))
    turret_scale = TURRET_LENGTH * length / max(high[0] - low[0], high[1] - low[1])
    transform_mesh(turret, Matrix.Scale(turret_scale, 4))
    low, high = bounds(vertices_of(turret))
    transform_mesh(turret, Matrix.Translation(Vector((0.0, 0.0, -low[2]))))
    hub_y = hull.spine_back + 0.12 * ( HULL_BACK - hull.spine_back )
    cannon = nodes["Laser Cannon"]
    cannon.location = Vector((0.0, hub_y, hull.top(0.0, hub_y) - 0.3))
    set_part(nodes["Laser"], turret, Vector((0.0, 0.0, 0.0)))

    # ---- the missile launchers on the outer sides of the fork's arms, their tubes forward
    centre_on_origin(launcher)
    face_forward(launcher, blue_direction(launcher, parts_image))
    centre_on_origin(launcher)
    tubes = blue_direction(launcher, parts_image)
    print("LAUNCHER tubes forward=%s direction=%s" % ( tubes is not None and tubes.y < -abs(tubes.x), None if tubes is None else tuple(round(v, 3) for v in tubes.normalized()) ))
    low, high = bounds(vertices_of(launcher))
    launcher_scale = LAUNCHER_LENGTH * length / ( high[1] - low[1] )
    left_launcher = scaled_copy(launcher, launcher_scale, "Launcher Left")
    right_launcher = mirrored(left_launcher, "Launcher Right")
    low, high = bounds(vertices_of(left_launcher))
    launcher_y = hull.spine_back + 0.4 * ( HULL_BACK - hull.spine_back )
    outer, middle = hull.outer_side(launcher_y)
    group = nodes["Missile Launchers"]
    group.location = Vector((0.0, launcher_y, 0.0))
    offset = outer + ( high[0] - low[0] ) / 2.0 * 0.9

    # the old file has the left launcher on +X
    set_part(nodes["Missile Launcher - Left"], left_launcher, Vector((offset, 0.0, middle)))
    set_part(nodes["Missile Launcher - Right"], right_launcher, Vector((-offset, 0.0, middle)))
    print("WEAPONS turret at y=%.1f, launchers at y=%.1f x=+-%.1f z=%.1f" % (hub_y, launcher_y, offset, middle))


def glow_faces(obj, maps, faces, strength, glass=None):
    """Light the bright texels of the given faces in the emissive map (the cockpit's windows, the engines' nozzles: Meshy
    paints them near white, so their colour alone cannot find them, but where they are on the hull can). With glass (a
    colour), the faces' dark grey texels glow that colour instead: the cockpit's canopy, which Meshy paints as dark glass."""
    albedo_image = bpy.data.images.load(os.path.join(ship_kit.PROJECT_ROOT, maps["Albedo"]))
    emissive_path = os.path.join(ship_kit.PROJECT_ROOT, maps["Emissive"])
    emissive_image = bpy.data.images.load(emissive_path)
    albedo = import_meshy.image_pixels(albedo_image)
    emissive = import_meshy.image_pixels(emissive_image).copy()
    height, width = albedo.shape[:2]
    inside = numpy.zeros((height, width), dtype=bool)
    uv = obj.data.uv_layers.active.data

    for polygon in faces:
        corners = [numpy.array(uv[i].uv) * (width, height) for i in polygon.loop_indices]

        for k in range(1, len(corners) - 1):
            a, b, c = corners[0], corners[k], corners[k + 1]
            low = numpy.floor(numpy.minimum(numpy.minimum(a, b), c)).astype(int).clip(0, (width - 1, height - 1))
            high = numpy.ceil(numpy.maximum(numpy.maximum(a, b), c)).astype(int).clip(0, (width - 1, height - 1))
            xs, ys = numpy.meshgrid(numpy.arange(low[0], high[0] + 1) + 0.5, numpy.arange(low[1], high[1] + 1) + 0.5)
            denominator = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])

            if abs(denominator) < 1e-9:
                continue

            u = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / denominator
            v = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / denominator
            hit = (u >= 0.0) & (v >= 0.0) & (u + v <= 1.0)
            inside[low[1]:high[1] + 1, low[0]:high[0] + 1] |= hit

    if glass is None:
        bright = inside & (albedo[..., :3].max(axis=2) > 0.7)
        emissive[bright, :3] = numpy.maximum(emissive[bright, :3], albedo[bright, :3] * strength)
    else:
        high = albedo[..., :3].max(axis=2)
        low = albedo[..., :3].min(axis=2)
        bright = inside & (high < 0.45) & ((high - low) < 0.12)
        emissive[bright, :3] = numpy.maximum(emissive[bright, :3], numpy.array(glass) * strength)
    bpy.data.images.remove(albedo_image)
    bpy.data.images.remove(emissive_image)
    import_meshy.save_image(emissive, emissive_path)

    return int(bright.sum())


def contrails(hull):
    """The middles of the two engine nozzles at the stern, in Unity's frame of the model (metres; Unity's X is Blender's -X),
    as one flat list x, y, z, x, y, z (Unity's JsonUtility reads no nested lists)."""
    stern = hull.points[hull.points[:, 1] > HULL_BACK - 2.0]
    places = []

    for side in (-1.0, 1.0):
        nozzle = stern[stern[:, 0] * side > 0.0]
        centre = nozzle.mean(axis=0)
        places.extend([round(float(-centre[0] * 0.0254), 3), round(HULL_BACK * 0.0254, 3), round(float(centre[2] * 0.0254), 3)])

    return places


def to_centimetres(root):
    """The old file's inches as Blender's centimetres: every part's place and mesh times 2.54, the root at 0.01."""
    root.scale = (0.01, 0.01, 0.01)

    for obj in bpy.data.objects:
        if obj is root or obj.type not in ("MESH", "EMPTY"):
            continue

        obj.location = obj.location * INCH

    for mesh in {obj.data for obj in bpy.data.objects if obj.type == "MESH"}:
        transform_mesh(mesh, Matrix.Scale(INCH, 4))


def export(path):
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"EMPTY", "MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        use_space_transform=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )


def main():
    arguments = ship_kit.script_arguments()

    if not arguments or "--source" not in arguments:
        raise SystemExit("usage: import_meshy_arth.py -- <meshy-arth.json> --source <folder with the GLB files> [--preview <png>] [--no-export]")

    with open(os.path.abspath(arguments[0]), encoding="utf-8") as config_file:
        config = json.load(config_file)

    source = arguments[arguments.index("--source") + 1]
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    do_export = "--no-export" not in arguments
    size = config.get("textureSize", 1024)

    ship_kit.reset_scene()
    nodes = build_nodes()
    root = nodes["Arth Ship"]

    hull_glb = import_meshy.import_glb(os.path.join(source, config["hull"]["glb"]), "Hull GLB")
    hull_glb.data.materials[0].name = "Arth Ship Meshy Hull"
    hull = HullShape(place_hull(nodes["Hull"], hull_glb))

    parts_glb = import_meshy.import_glb(os.path.join(source, config["parts"]["glb"]), "Parts GLB")
    parts_material = parts_glb.data.materials[0]
    parts_material.name = "Arth Ship Meshy Parts"
    bsdf = next(n for n in parts_material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    parts_image = import_meshy.upstream_image(bsdf.inputs["Base Color"])
    pod, turret, launcher = split_parts(parts_glb)
    bpy.data.objects.remove(parts_glb)

    # the parts were made in metres; the hull's frame is in inches
    for mesh in (pod, turret, launcher):
        transform_mesh(mesh, Matrix.Scale(1.0 / 0.0254, 4))

    assemble(nodes, hull, pod, turret, launcher, parts_image)

    # the hull's and the parts' maps, converted for SF - Standard
    maps = {}

    if do_export:
        maps["Arth Ship Meshy Hull"] = import_meshy.convert_maps(nodes["Hull"], TEXTURES, "Arth Ship Hull", size, config.get("glow"))
        maps["Arth Ship Meshy Parts"] = import_meshy.convert_maps(nodes["Pod 01"], TEXTURES, "Arth Ship Parts", size, config.get("glow"))

        # the ship's lights, as the old model's window glow: the cockpit's upright faces (its band of windows) and the faces at
        # the stern that face backwards (the nozzles)
        hull_mesh = nodes["Hull"].data
        windows = [p for p in hull_mesh.polygons if p.center.y < hull.spine_front and abs(p.normal.z) < 0.4]
        nozzles = [p for p in hull_mesh.polygons if p.center.y > HULL_BACK - 3.0 and p.normal.y > 0.6]
        canopy = [p for p in hull_mesh.polygons if p.center.y < hull.spine_front and p.normal.z > 0.5]
        lit = glow_faces(nodes["Hull"], maps["Arth Ship Meshy Hull"], windows, 0.9)
        lit += glow_faces(nodes["Hull"], maps["Arth Ship Meshy Hull"], nozzles, 0.7)
        lit += glow_faces(nodes["Hull"], maps["Arth Ship Meshy Hull"], canopy, 0.8, glass=(0.45, 0.75, 1.0))
        print("LIGHTS windows=%d nozzles=%d canopy=%d texels=%d" % (len(windows), len(nozzles), len(canopy), lit))

    if preview:
        # the review sheet shows the nose to the left (+Y): turn the whole ship for it, and back
        root.rotation_euler = (0.0, 0.0, math.pi)
        bpy.context.view_layer.update()
        import_meshy.add_preview_lights()
        ship_kit.preview_sheet([(nodes["Hull"], os.path.join("..", "Ship Configuration", "starflt_077.png"))], os.path.abspath(preview), tile=(800, 600), engine="CYCLES")
        root.rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()
        print("PREVIEW " + os.path.abspath(preview))

    if not do_export:
        return

    places = contrails(hull)
    to_centimetres(root)
    export(os.path.join(ship_kit.PROJECT_ROOT, ARTH_FBX))

    manifest = {
        "race": "Arth",
        "vessels": [],
        "materials": [],
        "playerShip": {
            "model": ARTH_FBX,
            "debris": "Assets/Game Objects/Ships Debris/Arth/Arth Ship.FBX",
            "materials": list(maps.keys()),
            "contrails": places,
            # CombatController spawns the wreck at its own scale, not the ship's 15: this import scale makes it the ship's
            # 51 units long (the debris script writes the wreck of this file 0.034 long at a scale of 0.01)
            "debrisScale": 15.0,
            "meshy": {"hull": config["hull"].get("meshy", {}), "parts": config["parts"].get("meshy", {})},
        },
    }

    for name, paths in maps.items():
        manifest["materials"].append({
            "name": name,
            "albedo": [1.0, 1.0, 1.0],
            "specular": [1.0, 1.0, 1.0],
            "smoothness": 1.0,
            "emissive": [0.0, 0.0, 0.0],
            "folder": "Assets/Game Objects/Ships/Arth",
            "albedoMap": paths.get("Albedo", ""),
            "specularMap": paths.get("Specular", ""),
            "normalMap": paths.get("Normal", ""),
            "emissiveMap": paths.get("Emissive", ""),
        })

    manifest_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest-arth.json")

    with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
        json.dump(manifest, manifest_file, indent="\t")
        manifest_file.write("\n")

    print("CONTRAILS %s" % places)
    print("MANIFEST " + manifest_path)


main()
