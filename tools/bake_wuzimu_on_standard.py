import bpy,pathlib
from mathutils import Matrix
root=pathlib.Path('C:/SistemaAdr/gta')
out=root/'tools/wuzimu-review/StandardTextured';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'tools/wuzimu-review/WuzimuStandard.blend'))
sources=[o for o in bpy.context.scene.objects if o.type=='MESH']
before=set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(root/'TestePersonagem/Assets/Resources/GangsterMusculo/GangsterMusculo.fbx'))
added=set(bpy.context.scene.objects)-before
arm=next(o for o in added if o.type=='ARMATURE');arm.animation_data_clear()
for b in arm.pose.bones:b.matrix_basis=Matrix.Identity(4)
targets=[o for o in added if o.type=='MESH']
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1
scene.render.bake.use_selected_to_active=True;scene.render.bake.max_ray_distance=.32;scene.render.bake.cage_extrusion=.12
scene.render.bake.margin=12;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
for i,obj in enumerate(targets):
 obj.hide_render=False
 for other in targets:other.hide_render=other!=obj
 for source in sources:source.hide_render=False
 images=[]
 for j,slot in enumerate(obj.material_slots):
  mat=bpy.data.materials.new('Transfer_%d_%d'%(i,j));mat.use_nodes=True;slot.material=mat
  img=bpy.data.images.new('Transfer_%d_%d'%(i,j),width=1024,height=1024,alpha=False)
  img.generated_color=(.12,.12,.12,1)
  node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=img;mat.node_tree.nodes.active=node
  images.append((img,mat,node))
 bpy.ops.object.select_all(action='DESELECT')
 for source in sources:source.select_set(True)
 obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.object.bake(type='DIFFUSE')
 for img,mat,node in images:
  img.filepath_raw=str(out/(img.name+'.png'));img.file_format='PNG';img.save()
  bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');mat.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color']);bs.inputs['Roughness'].default_value=.85
for obj in targets:obj.hide_render=False
for obj in sources:obj.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
for obj in [arm,*targets]:obj.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(out/'StandardTextured.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',path_mode='AUTO')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'preview.blend'))
print('TEXTURE_TRANSFER_FINISHED',len(targets))
