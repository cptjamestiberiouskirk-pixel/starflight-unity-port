"""Low-poly sci-fi supply crate for the Starflight Unity port.

Builds the crate (a box with recessed panels on every side and an accent strip on the two long sides), gives it a
bevel modifier and three basic materials, and exports it as FBX for Unity (Y up, 1 unit = 1 meter, the bevel applied,
the origin at the bottom centre so that the crate stands on the ground at y = 0).

Run it in either of two ways:

  From a shell (Blender 3.6 or later):
    blender --background --python DevTools/Blender/make_scifi_crate.py -- [--output PATH] [--size X Y Z]

  Through the Blender MCP server (execute_blender_code): paste the whole file and set OUTPUT_PATH below to the full
  path of Assets/Models/SciFiCrate.fbx in the Unity project, or set the SCIFI_CRATE_OUTPUT environment variable
  before Blender starts. The script only replaces the object, mesh and materials it made itself; the rest of the open
  Blender scene is left alone, apart from the selection.

Then, in Unity: Starflight Remake > AI > Import SciFi Crate Into Active Scene.
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix

# full path of the FBX to write when the script is run through the MCP server (None: work it out, see output_path)
OUTPUT_PATH = None

# the name of the object, the mesh and the prefix of the materials
NAME = "SciFiCrate"

# width (x), depth (y) and height (z) in meters
DEFAULT_SIZE = (1.2, 0.8, 0.8)


def parse_args():
    """Read the options that follow "--" on Blender's command line (none when run through the MCP server)."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    parser = argparse.ArgumentParser(
        prog="make_scifi_crate.py",
        description="Build a low-poly sci-fi supply crate in Blender and export it as FBX for Unity.",
    )
    parser.add_argument("--output", help="the FBX file to write (default: Assets/Models/SciFiCrate.fbx in the project)")
    parser.add_argument("--size", nargs=3, type=float, metavar=("X", "Y", "Z"), default=DEFAULT_SIZE,
                        help="width, depth and height in meters (default: %(default)s)")
    return parser.parse_args(argv)


def output_path(args):
    """The FBX path: --output, then OUTPUT_PATH, then SCIFI_CRATE_OUTPUT, then the project next to this script."""
    if args.output:
        return os.path.abspath(args.output)
    if OUTPUT_PATH:
        return os.path.abspath(OUTPUT_PATH)
    if os.environ.get("SCIFI_CRATE_OUTPUT"):
        return os.path.abspath(os.environ["SCIFI_CRATE_OUTPUT"])
    try:
        here = os.path.dirname(os.path.abspath(__file__))
    except NameError:
        raise RuntimeError("Set OUTPUT_PATH at the top of the script (or SCIFI_CRATE_OUTPUT) to the full path of "
                           "Assets/Models/SciFiCrate.fbx in the Unity project.")
    return os.path.normpath(os.path.join(here, "..", "..", "Assets", "Models", NAME + ".fbx"))


def set_input(node, names, value):
    """Set the first input of a node that exists under one of the names (they changed between Blender versions)."""
    for name in names:
        if name in node.inputs:
            node.inputs[name].default_value = value
            return


def make_material(suffix, color, metallic, roughness, emission_strength=0.0):
    """A Principled BSDF material; Unity's FBX importer reads its colour, metallic and roughness."""
    name = NAME + "_" + suffix
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    rgba = (color[0], color[1], color[2], 1.0)

    # the viewport colour, which is also what the FBX exporter falls back on
    material.diffuse_color = rgba
    material.metallic = metallic
    material.roughness = roughness

    # Blender 5 always uses nodes and deprecates the switch; even reading it there logs a DeprecationWarning
    if bpy.app.version < (5, 0, 0) and not material.use_nodes:
        material.use_nodes = True

    tree = material.node_tree
    bsdf = next((node for node in tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
        output = next((node for node in tree.nodes if node.type == "OUTPUT_MATERIAL"), None)
        if output is None:
            output = tree.nodes.new("ShaderNodeOutputMaterial")
        tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    set_input(bsdf, ["Base Color"], rgba)
    set_input(bsdf, ["Metallic"], metallic)
    set_input(bsdf, ["Roughness"], roughness)
    if emission_strength > 0.0:
        set_input(bsdf, ["Emission Color", "Emission"], rgba)
        set_input(bsdf, ["Emission Strength"], emission_strength)
    return material


def add_box(bm, center, size, material_index):
    """Add a box to the bmesh and give its faces a material slot."""
    matrix = Matrix.Translation(center) @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
    verts = bmesh.ops.create_cube(bm, size=1.0, matrix=matrix)["verts"]
    faces = {face for vert in verts for face in vert.link_faces}
    for face in faces:
        face.material_index = material_index
    return faces


def build_mesh(size):
    """The crate: a frame (slot 0) around recessed panels (slot 1), and accent strips (slot 2)."""
    sx, sy, sz = size
    smallest = min(sx, sy, sz)
    frame_width = smallest * 0.12
    recess = smallest * 0.04

    bm = bmesh.new()

    # the body, standing on z = 0
    body_faces = list(add_box(bm, (0.0, 0.0, sz * 0.5), (sx, sy, sz), 1))

    # inset every side: the original faces become the recessed panels, the new faces around them the frame
    frame = bmesh.ops.inset_individual(bm, faces=body_faces, thickness=frame_width, depth=-recess,
                                       use_even_offset=True)
    for face in frame["faces"]:
        face.material_index = 0

    # an accent strip across each long side, from the recessed panel to just past the frame
    strip_thickness = recess + 0.005
    strip_offset = sy * 0.5 - recess + strip_thickness * 0.5
    strip_size = (sx - frame_width * 2.0, strip_thickness, sz * 0.08)
    for side in (-1.0, 1.0):
        add_box(bm, (0.0, side * strip_offset, sz * 0.62), strip_size, 2)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(NAME)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def remove_previous():
    """Remove what an earlier run made, and nothing else."""
    old_object = bpy.data.objects.get(NAME)
    if old_object is not None:
        bpy.data.objects.remove(old_object, do_unlink=True)
    old_mesh = bpy.data.meshes.get(NAME)
    if old_mesh is not None and old_mesh.users == 0:
        bpy.data.meshes.remove(old_mesh)


def main():
    args = parse_args()
    path = output_path(args)

    remove_previous()

    mesh = build_mesh(args.size)
    mesh.materials.append(make_material("Frame", (0.18, 0.19, 0.21), 0.85, 0.35))
    mesh.materials.append(make_material("Panel", (0.30, 0.34, 0.29), 0.40, 0.60))
    mesh.materials.append(make_material("Accent", (1.00, 0.45, 0.05), 0.10, 0.45, emission_strength=1.5))

    crate = bpy.data.objects.new(NAME, mesh)
    bpy.context.scene.collection.objects.link(crate)

    # rounded edges; the exporter applies the modifier
    bevel = crate.modifiers.new("Bevel", "BEVEL")
    bevel.width = 0.012
    bevel.segments = 2
    bevel.limit_method = "ANGLE"
    bevel.angle_limit = math.radians(30.0)
    bevel.use_clamp_overlap = True

    # export only the crate
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    crate.select_set(True)
    bpy.context.view_layer.objects.active = crate

    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        add_leaf_bones=False,
        path_mode="AUTO",
    )

    polygons = len(mesh.polygons)
    print("make_scifi_crate: wrote %s (%d faces before the bevel)" % (path, polygons))
    return path


main()
