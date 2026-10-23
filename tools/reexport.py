import bpy
bpy.ops.wm.open_mainfile(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.blend')
bpy.ops.object.select_all(action='SELECT')
print('selected',[(o.name,o.type) for o in bpy.context.selected_objects])
print(bpy.ops.export_scene.fbx(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.fbx',use_selection=False,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY'))
