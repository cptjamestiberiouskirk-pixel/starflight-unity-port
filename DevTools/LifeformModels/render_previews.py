"""Review renders of the lifeform models, from the exported FBX files and their baked maps (Cycles).

blender --background --factory-startup --python DevTools/LifeformModels/render_previews.py -- [--source <folder>] [--sheet <png>] [--closeups <folder>] [--plan <names>] [--cell <pixels>] [--samples <n>]

--sheet renders the contact sheet: every model in a grid, each tinted a different colour. --closeups renders
a larger picture of each model named with --closeup-plans (default: four of them) into that folder.
The skin material is multiplied by the tint, as SF_AlbedoColor does in the game; the detail material is not.
"""

import json
import math
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

from lifeform_kit import LIFEFORMS_FOLDER, log, script_arguments, use_gpu  # noqa: E402

# a tint per model (sRGB), as a species colour would be
TINTS = {
    "Snakelike": (0.86, 0.32, 0.20),
    "BilateralNoLegs": (0.58, 0.38, 0.78),
    "BilateralOneLeg": (0.95, 0.66, 0.22),
    "BilateralTwoLegs": (0.33, 0.66, 0.36),
    "BilateralFourLegs": (0.74, 0.58, 0.42),
    "BilateralManyLegs": (0.25, 0.48, 0.85),
    "RadialNoLegs": (0.88, 0.28, 0.56),
    "RadialFewLegs": (0.38, 0.76, 0.72),
    "RadialManyLegs": (0.86, 0.44, 0.32),
    "AmorphousNoLegs": (0.58, 0.82, 0.30),
    "AmorphousLegs": (0.50, 0.34, 0.72),
    "LeavedSmallA": (0.40, 0.74, 0.30),
    "LeavedSmallB": (0.84, 0.32, 0.46),
    "LeavedMediumA": (0.30, 0.60, 0.70),
    "LeavedMediumB": (0.90, 0.62, 0.26),
    "LeavedLargeA": (0.60, 0.34, 0.70),
    "LeavedLargeB": (0.42, 0.72, 0.42),
}

ORDER = list(TINTS.keys())
STUDIO_LIGHTS = os.path.join(os.path.dirname(bpy.app.binary_path), "%d.%d" % bpy.app.version[:2], "datafiles", "studiolights", "world")


def linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def tint_of(name):
    r, g, b = TINTS.get(name, (0.8, 0.8, 0.8))

    return (linear(r), linear(g), linear(b), 1.0)


# ------------------------------------------------------------------------------------------ scene


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    use_gpu(scene)
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False

    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass

    world = bpy.data.worlds.new("Studio")
    scene.world = world
    world.use_nodes = True
    nodes = world.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputWorld")
    background = nodes.new("ShaderNodeBackground")
    environment = nodes.new("ShaderNodeTexEnvironment")
    environment.image = bpy.data.images.load(os.path.join(STUDIO_LIGHTS, "courtyard.exr"))
    background.inputs["Strength"].default_value = 0.55
    world.node_tree.links.new(environment.outputs["Color"], background.inputs["Color"])
    world.node_tree.links.new(background.outputs["Background"], output.inputs["Surface"])

    # a studio sweep: a floor that curves up into a wall far behind the model
    verts = []
    faces = []
    profile = []

    for i in range(24):
        t = i / 23.0
        angle = t * math.pi / 2.0
        profile.append((3.0 + 2.5 * math.sin(angle), 2.5 - 2.5 * math.cos(angle)))

    profile = [(-8.0, 0.0)] + profile + [(5.5, 8.0)]

    for x in (-10.0, 10.0):
        for y, z in profile:
            verts.append((x, -y, z))

    count = len(profile)

    for i in range(count - 1):
        faces.append([i, i + 1, count + i + 1, count + i])

    mesh = bpy.data.meshes.new("Sweep")
    mesh.from_pydata(verts, [], faces)

    for polygon in mesh.polygons:
        polygon.use_smooth = True

    sweep = bpy.data.objects.new("Sweep", mesh)
    scene.collection.objects.link(sweep)
    material = bpy.data.materials.new("Ground")
    material.use_nodes = True
    tree = material.node_tree
    bsdf = tree.nodes["Principled BSDF"]
    noise = tree.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 18.0
    ramp = tree.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.16, 0.16, 0.155, 1.0)
    ramp.color_ramp.elements[1].color = (0.22, 0.215, 0.205, 1.0)
    tree.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    tree.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    mesh.materials.append(material)

    # key light from the front left and above, a cool rim from behind, a warm fill from the right
    add_light("Key", "AREA", (-1.6, 2.2, 2.6), 260.0, 1.6, (1.0, 0.96, 0.9))
    add_light("Rim", "AREA", (1.4, -2.2, 1.8), 160.0, 1.2, (0.75, 0.85, 1.0))
    add_light("Fill", "AREA", (2.4, 1.4, 0.8), 45.0, 2.5, (1.0, 0.9, 0.8))

    return scene


def add_light(name, kind, location, power, size, colour):
    data = bpy.data.lights.new(name, kind)
    data.energy = power
    data.color = colour

    if kind == "AREA":
        data.size = size

    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    direction = Vector((0.0, 0.0, 0.35)) - Vector(location)
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    return obj


def image_node(tree, path, colour_space):
    node = tree.nodes.new("ShaderNodeTexImage")
    node.image = bpy.data.images.load(path, check_existing=True)
    node.image.colorspace_settings.name = colour_space

    return node


def preview_material(name, folder, model, tinted):
    """The look the game material gives: albedo times the tint (skin only), specular map, normal map, occlusion."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    base = os.path.join(folder, model)
    albedo = image_node(tree, base + " - Albedo.png", "sRGB")
    occlusion = image_node(tree, base + " - Occlusion.png", "Non-Color")
    specular = image_node(tree, base + " - Specular.png", "Non-Color")
    normal = image_node(tree, base + " - Normal.png", "Non-Color")

    tint = tree.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1.0 if tinted else 0.0
    tree.links.new(albedo.outputs["Color"], tint.inputs["A"])
    tint.inputs["B"].default_value = tint_of(model)

    # occlusion darkens the albedo (as the shader's Albedo Occlusion does), softened a little
    shade = tree.nodes.new("ShaderNodeMix")
    shade.data_type = "RGBA"
    shade.blend_type = "MULTIPLY"
    shade.inputs["Factor"].default_value = 0.8
    tree.links.new(tint.outputs["Result"], shade.inputs["A"])
    tree.links.new(occlusion.outputs["Color"], shade.inputs["B"])
    tree.links.new(shade.outputs["Result"], bsdf.inputs["Base Color"])

    rough = tree.nodes.new("ShaderNodeMath")
    rough.operation = "SUBTRACT"
    rough.inputs[0].default_value = 1.0
    tree.links.new(specular.outputs["Alpha"], rough.inputs[1])
    tree.links.new(rough.outputs["Value"], bsdf.inputs["Roughness"])

    # the specular map holds the sRGB coded F0; the principled level 0.5 is an F0 of 0.04
    power = tree.nodes.new("ShaderNodeMath")
    power.operation = "POWER"
    power.inputs[1].default_value = 2.2
    tree.links.new(specular.outputs["Color"], power.inputs[0])
    level = tree.nodes.new("ShaderNodeMath")
    level.operation = "MULTIPLY"
    level.inputs[1].default_value = 12.5
    level.use_clamp = True
    tree.links.new(power.outputs["Value"], level.inputs[0])
    tree.links.new(level.outputs["Value"], bsdf.inputs["Specular IOR Level"])

    normal_map = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])

    if os.path.exists(base + " - Emissive.png"):
        emissive = image_node(tree, base + " - Emissive.png", "sRGB")
        tree.links.new(emissive.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 3.0

    return material


def load_model(folder, model):
    bpy.ops.import_scene.fbx(filepath=os.path.join(folder, model + ".fbx"))
    objects = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    obj = objects[0]

    # keep the importer's axis conversion, then turn the head from -Y (Unity +Z) back to +Y, towards the camera
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.rotation_euler = (0.0, 0.0, math.radians(180.0))
    bpy.context.view_layer.update()
    low, high = bounds(obj)
    log("%s imported: %.3f x %.3f x %.3f, lowest z %.4f" % (model, high.x - low.x, high.y - low.y, high.z - low.z, low.z))

    for slot in obj.material_slots:
        tinted = slot.material is not None and slot.material.name.endswith("Skin")
        slot.material = preview_material("%s preview %s" % (model, "skin" if tinted else "detail"), folder, model, tinted)

    return obj


def bounds(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    high = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))

    return low, high


def add_camera(obj, direction, lens=50.0, fill=0.86, aspect=1.0, lift=0.0):
    low, high = bounds(obj)
    center = (low + high) / 2.0
    center.z += lift
    radius = (high - low).length / 2.0
    data = bpy.data.cameras.new("Camera")
    data.lens = lens
    data.sensor_fit = "HORIZONTAL" if aspect >= 1.0 else "VERTICAL"
    camera = bpy.data.objects.new("Camera", data)
    bpy.context.scene.collection.objects.link(camera)
    fov = 2.0 * math.atan(18.0 / lens)

    if aspect > 1.0:
        fov = 2.0 * math.atan(math.tan(fov / 2.0) / aspect)

    distance = radius / math.sin(fov / 2.0) * fill
    direction = Vector(direction).normalized()
    camera.location = center + direction * distance
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = camera

    return camera, distance


def add_label(camera, distance, lines, size=0.045, aspect=1.0):
    """Text in front of the camera, seen by the camera only."""
    fov = 2.0 * math.atan(18.0 / camera.data.lens)
    depth = camera.data.clip_start * 2.0
    half = math.tan(fov / 2.0) * depth
    half_height = half / aspect if aspect >= 1.0 else half
    half_width = half if aspect >= 1.0 else half * aspect
    material = bpy.data.materials.new("Label")
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (0.92, 0.92, 0.92, 1.0)
    emission.inputs["Strength"].default_value = 1.0
    tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])

    for i, line in enumerate(lines):
        curve = bpy.data.curves.new("Label %d" % i, "FONT")
        curve.body = line
        curve.size = half_height * 2.0 * (size if i == 0 else size * 0.75)
        curve.materials.append(material)
        text = bpy.data.objects.new("Label %d" % i, curve)
        bpy.context.scene.collection.objects.link(text)
        text.parent = camera
        y = -half_height * 0.90 + half_height * 2.0 * size * 1.25 * (len(lines) - 1 - i)
        text.location = (-half_width * 0.93, y, -depth)

        for attribute in ("visible_shadow", "visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter"):
            setattr(text, attribute, False)


def render(path, width, height, samples):
    scene = bpy.context.scene
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.cycles.samples = samples
    scene.render.filepath = path
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def load_pixels(path):
    image = bpy.data.images.load(path)
    width, height = image.size
    pixels = np.zeros(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)

    return pixels.reshape(height, width, 4)


def save_pixels(pixels, path):
    height, width = pixels.shape[:2]
    image = bpy.data.images.new("Sheet", width, height, alpha=True)
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


# ------------------------------------------------------------------------------------------ pictures

# the camera looks from the front left and above (the head points along +Y)
VIEW = (-0.95, 1.25, 0.62)


def model_manifest(folder):
    path = os.path.join(folder, "manifest-lifeforms.json")

    if not os.path.exists(path):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest-lifeforms.json")

    if not os.path.exists(path):
        return {}

    with open(path, "r", encoding="utf-8") as manifest_file:
        return {e["name"]: e for e in json.load(manifest_file)["lifeforms"]}


def render_cell(folder, model, path, size, samples, info):
    reset()
    obj = load_model(folder, model)
    camera, distance = add_camera(obj, VIEW)
    lines = [model]

    if info:
        lines.append("%d triangles" % info["triangles"])

    add_label(camera, distance, lines)
    render(path, size, size, samples)


def render_closeup(folder, model, path, samples, view=(-1.0, 1.0, 0.38)):
    reset()
    obj = load_model(folder, model)
    camera, distance = add_camera(obj, view, lens=70.0, fill=0.72, aspect=1.5)
    low, high = bounds(obj)
    camera.data.dof.use_dof = True
    camera.data.dof.focus_distance = distance
    camera.data.dof.aperture_fstop = 4.0 * distance
    render(path, 1500, 1000, samples)


def render_legend(path, size, lines):
    """A cell with text only: what the sheet shows."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    use_gpu(scene)
    scene.world = bpy.data.worlds.new("Dark")
    scene.world.color = (0.012, 0.012, 0.014)
    scene.view_settings.view_transform = "Standard"
    data = bpy.data.cameras.new("Camera")
    data.type = "ORTHO"
    data.ortho_scale = 2.0
    camera = bpy.data.objects.new("Camera", data)
    scene.collection.objects.link(camera)
    camera.location = (0.0, 0.0, 5.0)
    scene.camera = camera
    material = bpy.data.materials.new("Text")
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (0.8, 0.8, 0.8, 1.0)
    tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])

    for i, line in enumerate(lines):
        curve = bpy.data.curves.new("Line %d" % i, "FONT")
        curve.body = line
        curve.size = 0.13 if i == 0 else 0.085
        curve.materials.append(material)
        text = bpy.data.objects.new("Line %d" % i, curve)
        scene.collection.objects.link(text)
        text.location = (-0.9, 0.75 - i * 0.15 - (0.05 if i > 0 else 0.0), 0.0)

    render(path, size, size, 16)


def contact_sheet(folder, models, path, cell, samples, columns=6):
    scratch = tempfile.mkdtemp(prefix="lifeform-cells-")
    manifest = model_manifest(folder)
    rows = int(math.ceil(len(models) / columns))
    sheet = np.zeros((rows * cell, columns * cell, 4), dtype=np.float32)
    sheet[:, :] = (0.05, 0.05, 0.055, 1.0)

    for index, model in enumerate(models):
        cell_path = os.path.join(scratch, model + ".png")
        render_cell(folder, model, cell_path, cell, samples, manifest.get(model))
        pixels = load_pixels(cell_path)
        row = index // columns
        column = index % columns
        top = (rows - 1 - row) * cell
        sheet[top:top + cell, column * cell:(column + 1) * cell] = pixels
        log("cell %s" % model)

    if len(models) < rows * columns:
        legend_path = os.path.join(scratch, "legend.png")
        render_legend(legend_path, cell, [
            "Starflight lifeforms",
            "One model per body plan of the",
            "1984 notes, two per plant size.",
            "",
            "Cycles renders of the exported",
            "FBX models and their baked maps.",
            "Each tinted with another colour,",
            "as the species colour will be;",
            "eyes, claws, wood and pods are",
            "never tinted.",
        ])
        pixels = load_pixels(legend_path)
        index = len(models)
        top = (rows - 1 - index // columns) * cell
        sheet[top:top + cell, (index % columns) * cell:(index % columns + 1) * cell] = pixels

    save_pixels(sheet, path)
    log("sheet " + path)


def main():
    arguments = script_arguments()

    def value(flag, default=None):
        return arguments[arguments.index(flag) + 1] if flag in arguments else default

    folder = os.path.abspath(value("--source", LIFEFORMS_FOLDER))
    models = [m for m in ORDER if os.path.exists(os.path.join(folder, m + ".fbx"))]

    if value("--plan"):
        wanted = value("--plan").split(",")
        models = [m for m in models if m in wanted]

    cell = int(value("--cell", "560"))
    samples = int(value("--samples", "160"))

    if value("--sheet"):
        contact_sheet(folder, models, os.path.abspath(value("--sheet")), cell, samples)

    if value("--closeups"):
        out = os.path.abspath(value("--closeups"))
        os.makedirs(out, exist_ok=True)
        chosen = value("--closeup-plans", "Snakelike,BilateralFourLegs,BilateralManyLegs,RadialManyLegs,AmorphousLegs,LeavedSmallB").split(",")

        for model in chosen:
            if model in models:
                target = os.path.join(out, "closeup-%s.png" % model)
                render_closeup(folder, model, target, max(samples, 256))
                log("closeup " + target)


main()
