import json
from pathlib import Path

import bpy


ROOT = Path(r"C:\SistemaAdr\gta\TestePersonagem\Assets\Characters\Ballas2026")
characters = (
    "g_f_y_ballas_01",
    "g_m_y_ballaeast_01",
    "g_m_y_ballaorig_01",
    "g_m_y_ballasout_01",
)
report = []

for character in characters:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = ROOT / character / f"{character}.fbx"
    result = bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    unweighted = 0
    for obj in meshes:
        for vertex in obj.data.vertices:
            if not vertex.groups:
                unweighted += 1
    item = {
        "character": character,
        "import_result": sorted(result),
        "meshes": len(meshes),
        "armatures": len(armatures),
        "vertices": sum(len(obj.data.vertices) for obj in meshes),
        "triangles": sum(len(poly.vertices) - 2 for obj in meshes for poly in obj.data.polygons),
        "bones": sum(len(obj.data.bones) for obj in armatures),
        "unweighted_vertices": unweighted,
        "materials": len({slot.material.name for obj in meshes for slot in obj.material_slots if slot.material}),
    }
    assert "FINISHED" in result
    assert meshes
    assert armatures
    report.append(item)
    print("VERIFIED", json.dumps(item))

(ROOT / "fbx-validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
