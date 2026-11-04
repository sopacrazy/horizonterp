import json
from pathlib import Path

import bpy


ROOT = Path(r"C:\SistemaAdr\gta\TestePersonagem\Assets\Characters\Ballas2026")
characters = ("g_f_y_ballas_01", "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01")
report = []
for character in characters:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / character / f"{character}.blend"), load_ui=False)
    meshes = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        weighted = sum(1 for vertex in obj.data.vertices if vertex.groups)
        meshes.append({
            "name": obj.name,
            "vertices": len(obj.data.vertices),
            "weighted": weighted,
            "vertex_groups": len(obj.vertex_groups),
            "modifiers": [{"name": mod.name, "type": mod.type, "object": getattr(getattr(mod, "object", None), "name", None)} for mod in obj.modifiers],
            "parent": obj.parent.name if obj.parent else None,
        })
    item = {"character": character, "meshes": meshes}
    report.append(item)
    print("RIG", json.dumps(item))
(ROOT / "rig-inspection.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
