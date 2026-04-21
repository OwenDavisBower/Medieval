#!/usr/bin/env python3
"""
Reduce polygon count of mesh data in an FBX and write a new FBX in the same directory.

Requires Blender (3.x+) — `bpy` is only available inside Blender.

Usage:
  blender --background --python decimate_fbx_mesh.py -- input.fbx TARGET_FACE_COUNT [output.fbx]

  TARGET_FACE_COUNT is the desired approximate Blender face count (polygon count).
  Decimate uses a collapse ratio target/current; the result is approximate.

  If output is omitted, writes alongside the input as <name>_<target>poly<.ext>
  (e.g. pine01.fbx 500 → pine01_500poly.fbx).

Rigged FBX: armatures are not edited. The join step uses a mesh that already has an
Armature modifier (or is parented to an armature) as the active object so the merged
mesh keeps skinning; all armatures in the file are re-exported with unchanged data.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def _parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Decimate mesh(es) from an FBX and export a lower-poly FBX (run inside Blender).",
    )
    parser.add_argument(
        "input_fbx",
        help="Source .fbx file path.",
    )
    parser.add_argument(
        "target_face_count",
        type=int,
        help="Target maximum face (polygon) count after decimation.",
    )
    parser.add_argument(
        "output_fbx",
        nargs="?",
        default=None,
        help="Destination .fbx path. If omitted, uses <inputStem>_<target>poly<.ext> next to the input file.",
    )
    return parser.parse_args(argv)


def _face_count(obj) -> int:
    return len(obj.data.polygons)


def _add_apply_decimate(bpy, obj, ratio: float) -> None:
    mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)


def _find_target_armature(meshes: list) -> object | None:
    """Armature used for skinning, if any (join keeps Armature modifier only from the active mesh)."""
    for m in meshes:
        for mod in m.modifiers:
            if mod.type == "ARMATURE" and getattr(mod, "object", None):
                return mod.object
    for m in meshes:
        if m.parent and m.parent.type == "ARMATURE":
            return m.parent
    return None


def _mesh_for_join_active(meshes: list, target_armature: object | None):
    """Pick active mesh for join() so Armature modifier survives when possible."""
    if target_armature is None:
        return meshes[0]
    for m in meshes:
        for mod in m.modifiers:
            if mod.type == "ARMATURE" and getattr(mod, "object", None) == target_armature:
                return m
    for m in meshes:
        if m.parent == target_armature:
            return m
    return meshes[0]


def _ensure_armature_modifier(merged, target_armature) -> None:
    if target_armature is None:
        return
    for mod in merged.modifiers:
        if mod.type == "ARMATURE" and mod.object == target_armature:
            return
    mod = merged.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = target_armature


def main() -> None:
    try:
        import bpy  # type: ignore[import-untyped]
    except ImportError as e:
        raise SystemExit(
            "This script must be run with Blender so that `bpy` is available.\n"
            "Example:\n"
            "  blender --background --python decimate_fbx_mesh.py -- in.fbx 500 [out.fbx]\n"
        ) from e

    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1 :]
    else:
        argv = sys.argv[1:]

    args = _parse_args(argv)

    input_path = Path(args.input_fbx).resolve()
    if not input_path.is_file():
        raise SystemExit(f"Input file not found: {input_path}")

    target_faces = args.target_face_count
    if target_faces < 1:
        raise SystemExit("Target face count must be at least 1.")

    if args.output_fbx is None:
        output_path = input_path.parent / f"{input_path.stem}_{target_faces}poly{input_path.suffix}"
    else:
        output_path = Path(args.output_fbx).resolve()
    output_fbx = str(output_path)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(input_path))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit(f"No mesh objects found in {input_path}")

    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    target_armature = _find_target_armature(meshes)
    join_active = _mesh_for_join_active(meshes, target_armature)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = join_active
    bpy.ops.object.join()

    merged = bpy.context.view_layer.objects.active
    if merged is None or merged.type != "MESH":
        raise SystemExit("Join failed: no active mesh object.")

    _ensure_armature_modifier(merged, target_armature)

    current = _face_count(merged)
    if current == 0:
        raise SystemExit("Mesh has no faces.")

    ratio = min(1.0, max(1e-6, float(target_faces) / float(current)))
    _add_apply_decimate(bpy, merged, ratio)

    _ensure_armature_modifier(merged, target_armature)

    bpy.ops.object.select_all(action="DESELECT")
    merged.select_set(True)
    scene_objs = set(bpy.context.scene.objects)
    for arm in armatures:
        if arm in scene_objs:
            arm.select_set(True)
    bpy.context.view_layer.objects.active = merged

    bpy.ops.export_scene.fbx(
        filepath=output_fbx,
        use_selection=True,
        object_types={"MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
    )


if __name__ == "__main__":
    main()
