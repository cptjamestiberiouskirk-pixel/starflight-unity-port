"""Textured models of the original's 15 special artifacts (Blender 5.2, background mode).

blender --background --factory-startup --python DevTools/ShipModels/import_meshy_artifacts.py -- DevTools/ShipModels/meshy-artifacts.json --source <folder with the GLB files> [--preview <png>] [--no-export]

The artifacts had no models: the game knew them only by name, as the original did. Each model is made from a concept picture
drawn from the artifact's name and analysis, and the game loads it by the artifact's name from Resources/Artifacts
(ArtifactModels), to show it beside the ruin that holds it and in the turning windows of the Trade Depot and the terrain vehicle's cargo list (ArtifactPreview). So each model is one mesh node named for its artifact, centred on its origin and one unit across at its
widest (the game sizes it where it shows it), written in metres in the file's own unit scale, with its maps for SF - Standard.
It writes manifest-artifacts.json for wire.ps1: the materials and the models whose import takes them.
"""

import json
import os
import sys

import bpy
import numpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import import_meshy  # noqa: E402
import import_meshy_props  # noqa: E402  (its export and review sheet, shared with the props)
import ship_kit  # noqa: E402

MODELS = "Assets/Resources/Artifacts"
MATERIALS = "Assets/Game Objects/Artifacts"
TEXTURES = "Assets/Game Objects/Artifacts/Textures"


def vertices_of(mesh):
    co = numpy.empty(len(mesh.vertices) * 3, dtype=numpy.float64)
    mesh.vertices.foreach_get("co", co)

    return co.reshape(-1, 3)


def main():
    arguments = ship_kit.script_arguments()

    if not arguments or "--source" not in arguments:
        raise SystemExit("usage: import_meshy_artifacts.py -- <meshy-artifacts.json> --source <folder with the GLB files> [--preview <png>] [--no-export]")

    config_path = os.path.abspath(arguments[0])

    with open(config_path, encoding="utf-8") as config_file:
        config = json.load(config_file)

    source = arguments[arguments.index("--source") + 1]
    preview = arguments[arguments.index("--preview") + 1] if "--preview" in arguments else None
    do_export = "--no-export" not in arguments
    size = config.get("textureSize", 1024)

    ship_kit.reset_scene()
    objects = []
    manifest = {"race": "Artifacts", "vessels": [], "materials": [], "models": []}

    for artifact in config["artifacts"]:
        name = artifact["name"]
        obj = import_meshy.import_glb(os.path.join(source, artifact["glb"]), name)
        obj.data.materials[0].name = "Artifact " + name

        # centred on the origin, one unit across at its widest
        points = vertices_of(obj.data)
        low, high = points.min(axis=0), points.max(axis=0)
        obj.data.transform(Matrix.Translation(-Vector(( low + high ) / 2.0)))
        obj.data.transform(Matrix.Scale(1.0 / float(( high - low ).max()), 4))
        obj.data.update()
        obj.data.name = name
        objects.append(obj)
        triangles = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print("ARTIFACT %s: %d triangles, size %s" % (name, triangles, tuple(round(float(v), 3) for v in ( high - low ) / ( high - low ).max())))

        if not do_export:
            continue

        maps = import_meshy.convert_maps(obj, TEXTURES, "Artifact " + name, size, artifact.get("glow"))
        path = MODELS + "/" + name + ".fbx"
        os.makedirs(os.path.join(ship_kit.PROJECT_ROOT, MODELS), exist_ok=True)
        import_meshy_props.export(obj, os.path.join(ship_kit.PROJECT_ROOT, path))

        manifest["materials"].append({
            "name": "Artifact " + name,
            "albedo": [1.0, 1.0, 1.0],
            "specular": [1.0, 1.0, 1.0],
            "smoothness": 1.0,
            "emissive": [0.0, 0.0, 0.0],
            "folder": MATERIALS,
            "albedoMap": maps.get("Albedo", ""),
            "specularMap": maps.get("Specular", ""),
            "normalMap": maps.get("Normal", ""),
            "emissiveMap": maps.get("Emissive", ""),
        })
        manifest["models"].append({"model": path, "materials": ["Artifact " + name], "meshy": artifact.get("meshy", {})})

    if preview:
        # each artifact on its own tile, five to a row
        import_meshy_props.render_preview(objects, os.path.abspath(preview), columns=5)
        print("PREVIEW " + os.path.abspath(preview))

    if not do_export:
        return

    manifest_path = os.path.join(os.path.dirname(config_path), "manifest-artifacts.json")

    with open(manifest_path, "w", encoding="utf-8", newline="\n") as manifest_file:
        json.dump(manifest, manifest_file, indent="\t")
        manifest_file.write("\n")

    print("MANIFEST " + manifest_path)


if __name__ == "__main__":
    main()
