import bpy,json
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r'C:/SistemaAdr/gta/TestePersonagem/Assets/Resources/GangsterMusculo/GangsterMusculo.fbx')
a=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
print('STANDARD_BONES',json.dumps([b.name for b in a.data.bones]))
print('JOINTS',[(b.name,list(a.matrix_world@b.head_local)) for b in a.data.bones if any(k in b.name for k in ['Hip','Head','Hand','Foot','Upperarm','Forearm'])])
bpy.ops.import_scene.fbx(filepath=r'C:/SistemaAdr/gta/TestePersonagem/Assets/Resources/Characters/Wuzimu/Wuzimu.fbx')
print('SOURCE_OBJECTS',[(o.name,o.type,list(o.matrix_world.translation)) for o in bpy.context.selected_objects])
