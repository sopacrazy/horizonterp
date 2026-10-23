import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r'C:\SistemaAdr\gta\personagem\Fast Run.fbx')
bpy.context.view_layer.update()
for o in bpy.data.objects:
 if o.type=='ARMATURE':print('BONES',[b.name for b in o.data.bones])
print('ACTIONS',[(a.name,a.frame_range[:]) for a in bpy.data.actions])
