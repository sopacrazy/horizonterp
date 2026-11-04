import bpy,sys,importlib.util,pathlib,json
from mathutils import Vector
root=pathlib.Path(r'C:\SistemaAdr\gta')
out=root/'TestePersonagem/Assets/Resources/Characters/Wuzimu';out.mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('DragonFF',root/'tools/DragonFF-master/__init__.py',submodule_search_locations=[str(root/'tools/DragonFF-master')])
mod=importlib.util.module_from_spec(spec);sys.modules['DragonFF']=mod;spec.loader.exec_module(mod);mod.register()
from DragonFF.ops import dff_importer,txd_importer
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
txd_path=root/'personagem/wuzimu.txd'
txd=txd_importer.import_txd({'file_name':str(txd_path),'skip_mipmaps':True,'pack':True}) if txd_path.exists() else None
dff_importer.import_dff(dict(file_name=str(root/'personagem/wuzimu.dff'),txd_images=txd.images if txd else {},image_ext='png',connect_bones=False,use_mat_split=True,remove_doubles=False,create_backfaces=False,group_materials=False,import_normals=True,materials_naming='DEF'))
arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE'];meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
if len(arms)!=1 or not meshes:raise RuntimeError('Rig/mesh ausente ou ambíguo')
arm=arms[0]
up=(arm.matrix_world@arm.data.bones[' Head'].head_local)-(arm.matrix_world@arm.data.bones[' Pelvis'].head_local)
upright=up.normalized().rotation_difference(Vector((0,0,1))).to_matrix().to_4x4()
for o in bpy.context.scene.objects:
 if o.parent is None:o.matrix_world=upright@o.matrix_world
bpy.context.view_layer.update()
# Bake the DFF's corrective object rotation into the rest skeleton and mesh.
# Humanoid animation must not depend on an object-level 90-degree correction.
bpy.ops.object.select_all(action='DESELECT')
for o in [arm,*meshes]:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
bpy.context.view_layer.update()
ground=min((o.matrix_world@Vector(v)).z for o in meshes for v in o.bound_box)
for o in bpy.context.scene.objects:
 if o.parent is None:o.location.z-=ground
bpy.context.view_layer.update()
texdir=out/'Textures';texdir.mkdir(exist_ok=True)
for img in bpy.data.images:
 if img.type=='IMAGE' and img.has_data and img.size[0]>0:
  name=img.name.split('/')[-2] if '/' in img.name else pathlib.Path(img.name).stem
  img.filepath_raw=str(texdir/(name+'.png'));img.file_format='PNG';img.save()
for material in bpy.data.materials:
 if material.node_tree:
  nodes=material.node_tree.nodes
  shader=next((n for n in nodes if n.type=='BSDF_PRINCIPLED'),None)
  texture=next((n for n in nodes if n.type=='TEX_IMAGE' and n.image and n.image.has_data),None)
  if shader and texture:
   material.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color'])
   shader.inputs['Roughness'].default_value=.8
report={'bones':[{'name':b.name,'parent':b.parent.name if b.parent else None} for b in arm.data.bones],
 'meshes':[{'name':o.name,'vertices':len(o.data.vertices),'unweighted':sum(not any(g.weight>0 for g in v.groups) for v in o.data.vertices),'slots':[s.material.name if s.material else None for s in o.material_slots],'faces_by_material':{str(i):sum(p.material_index==i for p in o.data.polygons) for i in range(len(o.data.materials))}} for o in meshes],
 'materials':[{'name':m.name,'use_nodes':m.use_nodes,'nodes':[(n.name,n.type,n.image.name if n.type=='TEX_IMAGE' and n.image else '') for n in m.node_tree.nodes] if m.node_tree else []} for m in bpy.data.materials],'images':[i.name for i in bpy.data.images]}
(root/'tools/wuzimu-inspection.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT')
for o in [arm,*meshes]:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(out/'Wuzimu.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',path_mode='AUTO')
print('WUZIMU_EXPORTED',len(arm.data.bones),sum(len(m.data.vertices) for m in meshes))
center=sum((o.matrix_world@Vector(v) for o in meshes for v in o.bound_box),Vector())/(len(meshes)*8)
height=max((o.matrix_world@Vector(v)).z for o in meshes for v in o.bound_box)
for mat in bpy.data.materials:
 mat.use_nodes=True
 bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
 if bs and not txd:
  for link in list(mat.node_tree.links):
   if link.to_node==bs and link.to_socket.name in ('Base Color','Alpha'):mat.node_tree.links.remove(link)
  bs.inputs['Base Color'].default_value=(.4,.43,.47,1);bs.inputs['Roughness'].default_value=.8
bpy.ops.object.camera_add(location=(height*.8,height*3,height*.8));cam=bpy.context.object
cam.rotation_euler=(Vector((center.x,center.y,height*.5))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=height*1.45;bpy.context.scene.camera=cam
for loc in [(height*2,height*2,height*3),(-height*2,height,height*2)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=450;light.data.shape='DISK';light.data.size=height*2
 light.rotation_euler=(Vector((0,0,height*.5))-light.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.world.color=(.18,.18,.18)
scene.render.resolution_x=700;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.render.filepath=str(root/'tools/wuzimu-preview.png');scene.render.image_settings.file_format='PNG';bpy.ops.render.render(write_still=True)
