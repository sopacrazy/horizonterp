import bpy
bpy.ops.wm.open_mainfile(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.blend')
mesh=bpy.data.objects['ByBeyby_Mesh']; arm=bpy.data.objects['ByBeyby_Rig']
for o in (arm,mesh):
 m=o.matrix_world.copy();o.parent=None;o.matrix_world=m
m=mesh.matrix_world.copy();mesh.parent=arm;mesh.matrix_world=m
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.fbx',use_selection=False,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',path_mode='COPY')
bpy.ops.wm.save_as_mainfile(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.blend')
