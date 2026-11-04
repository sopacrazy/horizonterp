import os
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(r"C:\SistemaAdr\gta\TestePersonagem\Assets\Characters\Ballas2026")
OUTPUT = Path(r"C:\SistemaAdr\gta\TestePersonagem\Assets\CharacterReferences\Ballas2026\generated-previews")
ALL_CHARACTERS = (
    "g_f_y_ballas_01",
    "g_m_y_ballaeast_01",
    "g_m_y_ballaorig_01",
    "g_m_y_ballasout_01",
)
requested = os.environ.get("BALLAS_CHARACTER")
CHARACTERS = (requested,) if requested else ALL_CHARACTERS


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def bounds(meshes):
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return low, high


OUTPUT.mkdir(parents=True, exist_ok=True)
for character in CHARACTERS:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / character / f"{character}.blend"), load_ui=False)
    scene = bpy.context.scene
    meshes = [obj for obj in scene.objects if obj.type == "MESH"]
    low, high = bounds(meshes)
    center = (low + high) * 0.5
    height = max(high.z - low.z, 0.1)
    width = max(high.x - low.x, high.y - low.y, 0.1)

    bpy.ops.object.camera_add(location=(center.x, low.y - max(height * 1.8, width * 2.0), center.z + height * 0.03))
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    camera.data.lens = 58
    look_at(camera, center)
    scene.camera = camera

    for location, energy, size in (
        ((center.x - height, center.y - height, center.z + height), 1200, 4.0),
        ((center.x + height, center.y - height * 0.4, center.z + height * 0.55), 900, 3.0),
        ((center.x, center.y + height, center.z + height * 0.8), 1100, 3.0),
    ):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.data.energy = energy
        light.data.shape = "DISK"
        light.data.size = size
        look_at(light, center)

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.render.resolution_x = 768
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.025, 0.025, 0.035)
    scene.render.filepath = str(OUTPUT / f"{character}.png")
    scene.view_settings.look = "AgX - Medium High Contrast"
    bpy.ops.render.render(write_still=True)
    print(f"RENDERED {character} -> {scene.render.filepath}")
