import bpy,json
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r'C:\SistemaAdr\gta\personagem\Idle.fbx')
bpy.context.view_layer.update()
for o in bpy.data.objects:
 if o.type=='ARMATURE':print('RIG',o.name,[(b.name,b.parent.name if b.parent else None,list(b.head_local)) for b in o.data.bones])
print('ACTIONS',[(a.name,a.frame_range[:]) for a in bpy.data.actions]);print('FPS',bpy.context.scene.render.fps)
