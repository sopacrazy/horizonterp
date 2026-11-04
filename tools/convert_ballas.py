import json
import os
from pathlib import Path

import addon_utils
import bpy


SOURCE = Path(r"C:\SistemaAdr\gta\personagem\gtav\THE BALLAS 2026\File\Model")
HATS = Path(r"C:\SistemaAdr\gta\personagem\gtav\THE BALLAS 2026\File\Hat")
OUTPUT = Path(r"C:\SistemaAdr\gta\TestePersonagem\Assets\Characters\Ballas2026")
CHARACTERS = (
    "g_f_y_ballas_01",
    "g_m_y_ballaeast_01",
    "g_m_y_ballaorig_01",
    "g_m_y_ballasout_01",
)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def import_assets(directory: Path, names: list[str], texture_dir: Path, skeleton_path: Path):
    existing = [name for name in names if (directory / name).exists()]
    if not existing:
        return
    result = bpy.ops.sollumz.import_assets(
        directory=str(directory) + os.sep,
        files=[{"name": name} for name in existing],
        use_custom_settings=True,
        textures_mode="CUSTOM_DIR",
        textures_extract_custom_directory=str(texture_dir),
        dwd_import_external_skeleton="SAVED",
        dwd_import_external_skeleton_saved_path=str(skeleton_path),
    )
    if "FINISHED" not in result:
        raise RuntimeError(f"Sollumz import failed for {existing}: {result}")


def stats_for(character: str):
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    return {
        "character": character,
        "objects": len(bpy.context.scene.objects),
        "meshes": len(meshes),
        "armatures": len(armatures),
        "vertices": sum(len(obj.data.vertices) for obj in meshes),
        "triangles": sum(len(obj.data.loop_triangles) for obj in meshes),
        "bones": sum(len(obj.data.bones) for obj in armatures),
        "materials": len({slot.material.name for obj in meshes for slot in obj.material_slots if slot.material}),
    }


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    addon_utils.enable("Sollumz", default_set=True, persistent=True)
    results = []

    for character in CHARACTERS:
        clear_scene()
        out_dir = OUTPUT / character
        out_dir.mkdir(parents=True, exist_ok=True)
        texture_dir = out_dir / "Textures"
        texture_dir.mkdir(parents=True, exist_ok=True)
        skeleton_path = SOURCE / f"{character}.yft"
        import_assets(SOURCE, [f"{character}.ytd", f"{character}.ydd"], texture_dir, skeleton_path)
        import_assets(HATS, [f"{character}_p.ytd", f"{character}_p.ydd"], texture_dir, skeleton_path)

        if not any(obj.type == "MESH" for obj in bpy.context.scene.objects):
            raise RuntimeError(f"No mesh imported for {character}")

        for obj in bpy.context.scene.objects:
            obj.select_set(obj.type in {"MESH", "ARMATURE", "EMPTY"})

        fbx_path = out_dir / f"{character}.fbx"
        blend_path = out_dir / f"{character}.blend"

        bpy.ops.export_scene.fbx(
            filepath=str(fbx_path),
            use_selection=True,
            object_types={"MESH", "ARMATURE", "EMPTY"},
            add_leaf_bones=False,
            bake_anim=False,
            axis_forward="-Z",
            axis_up="Y",
            apply_scale_options="FBX_SCALE_UNITS",
            path_mode="RELATIVE",
            embed_textures=False,
        )
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), check_existing=False)
        item = stats_for(character)
        item["fbx"] = str(fbx_path)
        item["blend"] = str(blend_path)
        results.append(item)
        print("CONVERTED", json.dumps(item, ensure_ascii=False))

    (OUTPUT / "conversion-report.json").write_text(
        json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
