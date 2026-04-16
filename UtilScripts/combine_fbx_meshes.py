#!/usr/bin/env python3
"""
Merge all mesh objects in an FBX into a single mesh and write a new FBX.

Requires Blender (3.x+) — `bpy` is only available inside Blender.

Usage:
  blender --background --python combine_fbx_meshes.py -- input.fbx [output.fbx]

  If output is omitted, writes alongside the input as <name>Combined<.ext>
  (e.g. pine01.fbx → pine01Combined.fbx).

Optional:
  --preserve-non-mesh-objects   Keep armatures, empties, cameras, lights, etc.
                                (default: strip them; export only the merged mesh)
  --apply-transforms            Apply location/rotation/scale before join
                                (default: off)

Static meshes are the intended use case. Merging skinned/rigged setups is not supported.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def _parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Combine all meshes in an FBX into one mesh (run inside Blender).",
    )
    parser.add_argument(
        "input_fbx",
        help="Source .fbx file path.",
    )
    parser.add_argument(
        "output_fbx",
        nargs="?",
        default=None,
        help="Destination .fbx path. If omitted, uses <inputStem>Combined<.ext> next to the input file.",
    )
    parser.add_argument(
        "--preserve-non-mesh-objects",
        action="store_true",
        help="Keep non-mesh objects in the scene when exporting (default: strip them).",
    )
    parser.add_argument(
        "--apply-transforms",
        dest="apply_transforms",
        action="store_true",
        help="Apply object location/rotation/scale before joining.",
    )
    parser.set_defaults(apply_transforms=False)
    return parser.parse_args(argv)


def main() -> None:
    try:
        import bpy  # type: ignore[import-untyped]
    except ImportError as e:
        raise SystemExit(
            "This script must be run with Blender so that `bpy` is available.\n"
            "Example:\n"
            "  blender --background --python combine_fbx_meshes.py -- in.fbx [out.fbx]\n"
        ) from e

    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1 :]
    else:
        argv = sys.argv[1:]

    args = _parse_args(argv)

    input_path = Path(args.input_fbx)
    if args.output_fbx is None:
        output_path = input_path.parent / f"{input_path.stem}Combined{input_path.suffix}"
    else:
        output_path = Path(args.output_fbx)
    output_fbx = str(output_path.resolve())

    # Fresh scene
    bpy.ops.wm.read_factory_settings(use_empty=True)

    bpy.ops.import_scene.fbx(filepath=str(input_path.resolve()))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit(f"No mesh objects found in: {input_path}")

    bpy.ops.object.select_all(action="DESELECT")

    if args.apply_transforms:
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bpy.ops.object.select_all(action="DESELECT")

    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()

    merged = bpy.context.view_layer.objects.active
    if merged is None or merged.type != "MESH":
        raise SystemExit("Join failed: no active mesh object.")

    if not args.preserve_non_mesh_objects:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in list(bpy.context.scene.objects):
            if obj != merged:
                obj.select_set(True)
        bpy.ops.object.delete()

    bpy.ops.object.select_all(action="DESELECT")
    merged.select_set(True)
    bpy.context.view_layer.objects.active = merged

    export_kw = dict(
        filepath=output_fbx,
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
    )
    if args.preserve_non_mesh_objects:
        # Whole scene: merged mesh plus armatures, empties, etc.
        bpy.ops.export_scene.fbx(use_selection=False, **export_kw)
    else:
        bpy.ops.export_scene.fbx(
            use_selection=True,
            object_types={"MESH"},
            **export_kw,
        )


if __name__ == "__main__":
    main()
