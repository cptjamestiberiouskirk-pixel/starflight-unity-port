"""Shared helpers for the procedural alien ship models (Blender 5.0, run in background mode).

A race script (build_<race>.py) imports this module, describes each vessel with the Ship class,
and calls build_race(). Everything is made from code, with no randomness, so a rebuild gives the
same meshes. See README.md in this folder.
"""

import json
import math
import os
import random
import sys
import tempfile

import bmesh
import bpy
import numpy
from mathutils import Matrix, Vector

# the player's Arth Ship is 3.404 units long in its FBX and has scale 15 in Spaceflight.unity
PLAYER_LENGTH = 3.404 * 15.0

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHIPS_FOLDER = os.path.join(PROJECT_ROOT, "Assets", "Game Objects", "Ships")
DEBRIS_FOLDER = os.path.join(PROJECT_ROOT, "Assets", "Game Objects", "Ships Debris")
SCREENSHOT_FOLDER = os.path.join(PROJECT_ROOT, "Research", "Screenshots", "Ships")


def display_size(strinfo_size):
    """The size a vessel is shown at, in player ship lengths (rulings of 2026-10-09).

    STRINFO section 7 gives each vessel's size against an Interstel ship. Up to 4 times it is
    used as it is (with a floor of 0.3 so the smallest ships stay visible); above that it is
    compressed, which reproduces the existing Mysterion (124 times, shown at about 7.8 times).
    """
    if strinfo_size <= 4.0:
        return max(strinfo_size, 0.3)

    return 4.0 + 0.77 * math.log2(strinfo_size / 4.0)


def target_length(strinfo_size):
    return PLAYER_LENGTH * display_size(strinfo_size)


# ------------------------------------------------------------------------------------------ materials

class Material:
    """A material for one part of a ship. The Unity side makes a matching SF - Standard material."""

    def __init__(self, name, albedo, specular=(0.25, 0.25, 0.25), smoothness=0.45, emissive=(0.0, 0.0, 0.0)):
        self.name = name
        self.albedo = albedo
        self.specular = specular
        self.smoothness = smoothness
        self.emissive = emissive

    def blender_material(self):
        material = bpy.data.materials.get(self.name)

        if material is None:
            material = bpy.data.materials.new(self.name)

        # the workbench preview shows the albedo, or the glow for a part that glows
        glow = max(self.emissive)
        colour = self.emissive if glow > 0.0 else self.albedo
        material.diffuse_color = (colour[0], colour[1], colour[2], 1.0)
        material.roughness = 1.0 - self.smoothness

        return material

    def manifest(self):
        return {
            "name": self.name,
            "albedo": list(self.albedo),
            "specular": list(self.specular),
            "smoothness": self.smoothness,
            "emissive": list(self.emissive),
        }


# ------------------------------------------------------------------------------------------ geometry

MIRROR_X = Matrix.Scale(-1.0, 4, (1.0, 0.0, 0.0))


def translate(x, y, z):
    return Matrix.Translation((x, y, z))


def rotate(degrees, axis):
    return Matrix.Rotation(math.radians(degrees), 4, axis)


def superellipse(angle, half_width, half_height, power):
    """A point of a superellipse: power 2 is an ellipse, higher powers are boxier, lower ones pinched."""
    c = math.cos(angle)
    s = math.sin(angle)
    exponent = 2.0 / power
    x = half_width * math.copysign(abs(c) ** exponent, c)
    z = half_height * math.copysign(abs(s) ** exponent, s)

    return x, z


class Ship:
    """One vessel, built in a canonical frame: the nose points along +Y, up is +Z, X is to the side.

    Parts go into one bmesh with a material index each. A part can be mirrored across X.
    """

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.materials = []

    def _material_index(self, material):
        if material not in self.materials:
            self.materials.append(material)

        return self.materials.index(material)

    def _place(self, verts, faces, material, matrix, mirror):
        """Add the faces made from verts with matrix, and a mirrored copy if asked."""
        copies = [(matrix, False)]

        if mirror:
            copies.append((MIRROR_X @ matrix, True))

        for copy_matrix, flipped in copies:
            new_verts = [self.bm.verts.new(copy_matrix @ Vector(v)) for v in verts]

            for face in faces:
                indices = list(reversed(face)) if flipped else face

                try:
                    new_face = self.bm.faces.new([new_verts[i] for i in indices])
                except ValueError:
                    # a degenerate face (two corners in the same place) is left out
                    continue

                new_face.material_index = self._material_index(material)

    def loft(self, sections, material, segments=12, power=2.0, matrix=Matrix(), mirror=False, cap_start=True, cap_end=True):
        """A body made of cross sections along Y.

        sections: (y, half_width, half_height, z_offset[, x_offset]) from the back to the front.
        A section with no width and no height is a point (a cone tip).
        """
        verts = []
        faces = []
        rings = []

        for section in sections:
            y, half_width, half_height, z_offset = section[:4]
            x_offset = section[4] if len(section) > 4 else 0.0

            if half_width <= 1e-6 and half_height <= 1e-6:
                rings.append([len(verts)])
                verts.append((x_offset, y, z_offset))
                continue

            ring = []

            for i in range(segments):
                angle = 2.0 * math.pi * i / segments
                x, z = superellipse(angle, half_width, half_height, power)
                ring.append(len(verts))
                verts.append((x_offset + x, y, z_offset + z))

            rings.append(ring)

        for a, b in zip(rings, rings[1:]):
            if len(a) == 1 and len(b) == 1:
                continue

            if len(a) == 1:
                for i in range(segments):
                    faces.append([a[0], b[(i + 1) % segments], b[i]])
            elif len(b) == 1:
                for i in range(segments):
                    faces.append([a[i], a[(i + 1) % segments], b[0]])
            else:
                for i in range(segments):
                    j = (i + 1) % segments
                    faces.append([a[i], a[j], b[j], b[i]])

        # the caps face backwards (-Y) and forwards (+Y)
        if cap_start and len(rings[0]) > 1:
            faces.append(list(rings[0]))

        if cap_end and len(rings[-1]) > 1:
            faces.append(list(reversed(rings[-1])))

        self._place(verts, faces, material, matrix, mirror)

    def ellipsoid(self, center, radii, material, segments=12, rings=8, power=2.0, matrix=Matrix(), mirror=False):
        """An ellipsoid along Y (radii are x, y, z)."""
        sections = []

        for i in range(rings + 1):
            t = -math.pi / 2.0 + math.pi * i / rings
            y = center[1] + radii[1] * math.sin(t)
            scale = math.cos(t)
            sections.append((y, radii[0] * scale, radii[2] * scale, center[2], center[0]))

        self.loft(sections, material, segments, power, matrix, mirror)

    def cylinder(self, start_y, end_y, radius, material, segments=10, x=0.0, z=0.0, end_radius=None, matrix=Matrix(), mirror=False):
        """A cylinder (or a truncated cone) along Y."""
        end_radius = radius if end_radius is None else end_radius
        sections = [(start_y, radius, radius, z, x), (end_y, end_radius, end_radius, z, x)]
        self.loft(sections, material, segments, 2.0, matrix, mirror)

    def slab(self, outline, thickness, material, z=0.0, bevel=0.0, matrix=Matrix(), mirror=False):
        """A flat plate: a polygon in the XY plane, made thick along Z.

        outline goes counterclockwise seen from above. bevel narrows the top and the bottom face
        towards the middle of the outline, which gives the plate a chamfered edge.
        """
        count = len(outline)
        cx = sum(p[0] for p in outline) / count
        cy = sum(p[1] for p in outline) / count
        half = thickness / 2.0

        def inset(point):
            if bevel <= 0.0:
                return point

            return (point[0] + (cx - point[0]) * bevel, point[1] + (cy - point[1]) * bevel)

        verts = []

        # edge ring in the middle, then the top and the bottom face
        for x, y in outline:
            verts.append((x, y, z))

        for x, y in outline:
            px, py = inset((x, y))
            verts.append((px, py, z + half))

        for x, y in outline:
            px, py = inset((x, y))
            verts.append((px, py, z - half))

        faces = [list(range(count, 2 * count)), list(reversed(range(2 * count, 3 * count)))]

        for i in range(count):
            j = (i + 1) % count
            faces.append([i, j, count + j, count + i])
            faces.append([2 * count + i, 2 * count + j, j, i])

        self._place(verts, faces, material, matrix, mirror)

    def ring_sector(self, center, radii, inner, start, end, thickness, material, segments=24, z=0.0, matrix=Matrix(), mirror=False):
        """A flat arc of an elliptical ring, made thick along Z.

        radii: (x, y) of the outer edge; inner: the inner edge as a fraction of the outer. Angles in degrees,
        0 is the nose (+Y), counting towards +X; start < end. A full ring goes from 0 to 360.
        """
        outer_points = []
        inner_points = []

        for i in range(segments + 1):
            angle = math.radians(start + (end - start) * i / segments)
            x = math.sin(angle)
            y = math.cos(angle)
            outer_points.append((center[0] + radii[0] * x, center[1] + radii[1] * y))
            inner_points.append((center[0] + radii[0] * inner * x, center[1] + radii[1] * inner * y))

        self.slab(outer_points + list(reversed(inner_points)), thickness, material, z, 0.0, matrix, mirror)

    def blob(self, center, radii, material, seed, roughness=0.15, segments=16, rings=10, matrix=Matrix(), mirror=False):
        """A lumpy ellipsoid: every vertex is pushed in or out by a repeatable random amount."""
        generator = random.Random(seed)
        verts = []
        faces = []

        # poles at the back (-Y) and the front (+Y), rings in between
        verts.append((center[0], center[1] - radii[1], center[2]))

        for ring in range(1, rings):
            t = -math.pi / 2.0 + math.pi * ring / rings
            for i in range(segments):
                angle = 2.0 * math.pi * i / segments
                scale = 1.0 + generator.uniform(-roughness, roughness)
                verts.append((center[0] + radii[0] * math.cos(t) * math.cos(angle) * scale,
                              center[1] + radii[1] * math.sin(t) * scale,
                              center[2] + radii[2] * math.cos(t) * math.sin(angle) * scale))

        verts.append((center[0], center[1] + radii[1], center[2]))
        last = len(verts) - 1

        for i in range(segments):
            j = (i + 1) % segments
            faces.append([0, 1 + j, 1 + i])
            faces.append([last, last - segments + i, last - segments + j])

        for ring in range(rings - 2):
            a = 1 + ring * segments
            b = a + segments
            for i in range(segments):
                j = (i + 1) % segments
                faces.append([a + i, a + j, b + j, b + i])

        self._place(verts, faces, material, matrix, mirror)

    def lathe(self, center, profile, material, segments=32, mirror=False):
        """A solid of revolution around a vertical axis through center. profile: (radius, z) from the bottom to the top."""
        verts = []
        faces = []
        rings = []

        for radius, z in profile:
            if radius <= 1e-6:
                rings.append([len(verts)])
                verts.append((center[0], center[1], center[2] + z))
                continue

            ring = []

            for i in range(segments):
                angle = 2.0 * math.pi * i / segments
                ring.append(len(verts))
                verts.append((center[0] + radius * math.cos(angle), center[1] + radius * math.sin(angle), center[2] + z))

            rings.append(ring)

        for a, b in zip(rings, rings[1:]):
            if len(a) == 1 and len(b) == 1:
                continue

            if len(a) == 1:
                for i in range(segments):
                    faces.append([a[0], b[(i + 1) % segments], b[i]])
            elif len(b) == 1:
                for i in range(segments):
                    faces.append([a[i], a[(i + 1) % segments], b[0]])
            else:
                for i in range(segments):
                    j = (i + 1) % segments
                    faces.append([a[i], a[j], b[j], b[i]])

        self._place(verts, faces, material, Matrix(), mirror)

    def beam(self, start, end, half_width, half_height, material, mirror=False):
        """A flat bar from start to end (half_width across, half_height up)."""
        start = Vector(start)
        direction = Vector(end) - start
        turn = Vector((0.0, 1.0, 0.0)).rotation_difference(direction.normalized()).to_matrix().to_4x4()
        matrix = Matrix.Translation(start) @ turn
        self.loft([(0.0, half_width, half_height, 0.0), (direction.length, half_width, half_height, 0.0)], material, 4, 8.0, matrix, mirror)

    def spike(self, base, direction, length, radius, material, segments=6, mirror=False):
        """A cone from base along direction."""
        direction = Vector(direction).normalized()
        turn = Vector((0.0, 1.0, 0.0)).rotation_difference(direction).to_matrix().to_4x4()
        matrix = Matrix.Translation(base) @ turn
        self.loft([(0.0, radius, radius, 0.0), (length, 0.0, 0.0, 0.0)], material, segments, 2.0, matrix, mirror)

    def box(self, center, size, material, matrix=Matrix(), mirror=False):
        hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
        x, y = center[0], center[1]
        outline = [(x - hx, y - hy), (x + hx, y - hy), (x + hx, y + hy), (x - hx, y + hy)]
        self.slab(outline, 2.0 * hz, material, center[2], 0.0, matrix, mirror)

    # -------------------------------------------------------------------------------------- finishing

    def finish(self, length, pivot=None):
        """Make the Blender object: scale it so its longest side is length units, pivot at the origin.

        pivot is a point in the canonical frame (before scaling) that becomes the origin; by default
        the middle of the bounding box. Returns the object, still in the canonical frame (nose +Y).
        """
        bm = self.bm
        # parts are separate shells on purpose: merging vertices would weld the inner caps of joined parts
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")

        low = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
        high = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
        extent = high - low
        scale = length / max(extent)
        origin = Vector(pivot) if pivot is not None else (low + high) / 2.0

        for v in bm.verts:
            v.co = (v.co - origin) * scale

        box_uvs(bm, length)

        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()

        for material in self.materials:
            mesh.materials.append(material.blender_material())

        # faceted look: smooth shading with every edge sharper than 35 degrees kept hard
        for polygon in mesh.polygons:
            polygon.use_smooth = True

        mesh.set_sharp_from_angle(angle=math.radians(35.0))

        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)

        return obj


def box_uvs(bm, length):
    """Box projection: each face takes the two coordinates across its main axis, in units of the ship's length."""
    uv_layer = bm.loops.layers.uv.verify()

    for face in bm.faces:
        n = face.normal
        axis = max(range(3), key=lambda i: abs(n[i]))

        for loop in face.loops:
            co = loop.vert.co
            u, v = [co[i] for i in range(3) if i != axis]
            loop[uv_layer].uv = (u / length + 0.5, v / length + 0.5)


# ------------------------------------------------------------------------------------------ export

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def to_unity_frame(obj, nose_direction):
    """Turn the object from the canonical frame (nose +Y) to the frame the FBX export expects."""
    angle = {"+Y": 0.0, "-Y": 180.0}[nose_direction]
    obj.data.transform(rotate(angle, "Z"))
    obj.data.update()


def export_fbx(obj, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )


# ------------------------------------------------------------------------------------------ preview

def _render_view(obj, path, view, resolution):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = resolution[0]
    scene.render.resolution_y = resolution[1]
    scene.render.film_transparent = True
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True

    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    center = sum(corners, Vector()) / 8.0
    size = max(obj.dimensions)
    camera_data = bpy.data.cameras.new("Preview")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = size * 1.45
    camera_data.clip_end = size * 10.0
    camera = bpy.data.objects.new("Preview", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera

    distance = size * 3.0

    # every view has the nose to the left, as the original's sensor pictures do.
    # top: looking down; side: from the left; quarter: from the front, left and above
    if view == "top":
        camera.location = center + Vector((0.0, 0.0, distance))
        camera.rotation_euler = (0.0, 0.0, math.radians(-90.0))
    elif view == "side":
        camera.location = center + Vector((-distance, 0.0, 0.0))
        camera.rotation_euler = (math.radians(90.0), 0.0, math.radians(-90.0))
    else:
        direction = Vector((-1.0, 1.1, 0.75)).normalized()
        camera.location = center + direction * distance
        camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()

    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(camera)
    bpy.data.cameras.remove(camera_data)


def _load_pixels(path):
    image = bpy.data.images.load(path)
    width, height = image.size
    pixels = numpy.array(image.pixels[:], dtype=numpy.float32).reshape(height, width, 4)
    bpy.data.images.remove(image)

    return pixels


def _fit(pixels, width, height):
    """Scale an image to fit into width x height (nearest neighbour, keeps the pixel look)."""
    source_height, source_width = pixels.shape[:2]
    scale = min(width / source_width, height / source_height)
    new_width = max(1, int(source_width * scale))
    new_height = max(1, int(source_height * scale))
    rows = (numpy.arange(new_height) / scale).astype(int).clip(0, source_height - 1)
    columns = (numpy.arange(new_width) / scale).astype(int).clip(0, source_width - 1)

    return pixels[rows][:, columns]


def preview_sheet(entries, path, tile=(420, 315)):
    """One row per vessel: the original's screenshot, then top, side and three-quarter views.

    entries: (object, screenshot file name or None). Rendered with the workbench engine.
    """
    scratch = tempfile.mkdtemp(prefix="ship-preview-")
    columns = 4
    width = tile[0] * columns
    height = tile[1] * len(entries)
    sheet = numpy.zeros((height, width, 4), dtype=numpy.float32)
    sheet[:, :] = (0.09, 0.09, 0.11, 1.0)

    for row, (obj, screenshot) in enumerate(entries):
        # hide every other ship while this one is rendered
        for other, _ in entries:
            other.hide_render = other is not obj

        tiles = []

        if screenshot:
            tiles.append(_load_pixels(os.path.join(SCREENSHOT_FOLDER, screenshot)))
        else:
            tiles.append(None)

        for view in ("top", "side", "quarter"):
            tile_path = os.path.join(scratch, "%s-%s.png" % (obj.name, view))
            _render_view(obj, tile_path, view, tile)
            tiles.append(_load_pixels(tile_path))

        # blender images are stored bottom row first
        top = height - (row + 1) * tile[1]

        for column, pixels in enumerate(tiles):
            if pixels is None:
                continue

            fitted = _fit(pixels, tile[0], tile[1])
            h, w = fitted.shape[:2]
            y0 = top + (tile[1] - h) // 2
            x0 = column * tile[0] + (tile[0] - w) // 2
            region = sheet[y0:y0 + h, x0:x0 + w]
            alpha = fitted[:, :, 3:4]
            region[:, :, :3] = fitted[:, :, :3] * alpha + region[:, :, :3] * (1.0 - alpha)

    for obj, _ in entries:
        obj.hide_render = False

    image = bpy.data.images.new("Sheet", width, height, alpha=True)
    image.pixels.foreach_set(sheet.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


# ------------------------------------------------------------------------------------------ race build

def script_arguments():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def build_race(race, vessels, nose_direction="-Y"):
    """Build every vessel of a race, export the models and write the manifest for the Unity side.

    vessels: dicts with id, name, strinfo (size), screenshot, build (function returning a Ship), pivot (optional).
    Arguments after --: --preview <png> renders a review sheet; --no-export skips the FBX files.
    """
    arguments = script_arguments()
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    export = "--no-export" not in arguments

    reset_scene()
    built = []
    manifest = {"race": race, "vessels": [], "materials": []}
    material_names = set()

    for vessel in vessels:
        ship = vessel["build"]()
        length = target_length(vessel["strinfo"])
        obj = ship.finish(length, vessel.get("pivot"))
        built.append((obj, vessel.get("screenshot")))

        fbx_name = vessel["name"] + ".fbx"
        manifest["vessels"].append({
            "id": vessel["id"],
            "name": vessel["name"],
            "strinfoSize": vessel["strinfo"],
            "length": round(length, 3),
            "model": "Assets/Game Objects/Ships/%s/%s" % (race, fbx_name),
            "debris": "Assets/Game Objects/Ships Debris/%s/%s" % (race, fbx_name),
            "materials": [m.name for m in ship.materials],
        })

        for material in ship.materials:
            if material.name not in material_names:
                material_names.add(material.name)
                entry = material.manifest()
                entry["folder"] = "Assets/Game Objects/Ships/" + race
                manifest["materials"].append(entry)

        print("SHIP %s id=%d length=%.1f dimensions=%s triangles=%d" % (
            vessel["name"], vessel["id"], length, tuple(round(d, 1) for d in obj.dimensions), len(obj.data.polygons)))

    if preview:
        preview_sheet(built, os.path.abspath(preview))
        print("PREVIEW " + os.path.abspath(preview))

    if export:
        for obj, _ in built:
            to_unity_frame(obj, nose_direction)
            export_fbx(obj, os.path.join(SHIPS_FOLDER, race, obj.name + ".fbx"))

        manifest_path = os.path.join(os.path.dirname(__file__), "manifest-%s.json" % race.lower().replace(" ", "-"))

        with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
            json.dump(manifest, manifest_file, indent="\t")
            manifest_file.write("\n")

        print("MANIFEST " + manifest_path)
