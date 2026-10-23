import bpy,json
for file in [r'C:/SistemaAdr/gta/personagem_unity/ByBeyby.fbx',r'C:/SistemaAdr/gta/personagem/Fast Run.fbx',r'C:/SistemaAdr/gta/personagem/Walking Left Turn.fbx']:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=file);a=next(o for o in bpy.data.objects if o.type=='ARMATURE')
 pts={b.name:(a.matrix_world@b.matrix_local).translation for b in a.data.bones}
 print(file, 'restHips',list(pts[' Pelvis']),'height',(pts[' Pelvis']-pts[' L Foot']).length,'chain',(pts[' L Thigh']-pts[' L Calf']).length+(pts[' L Calf']-pts[' L Foot']).length)
