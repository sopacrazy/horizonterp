import bpy,pathlib,json
from mathutils import Vector,Matrix
root=pathlib.Path('C:/SistemaAdr/gta')
# Reuse the textured DFF import, stopping before export/render.
exec((root/'tools/convert_wuzimu.py').read_text().split("report={'bones'")[0])
source=arm; skin_meshes=list(meshes)
source_points={b.name:source.matrix_world@b.head for b in source.pose.bones}
evaluated={}
depsgraph=bpy.context.evaluated_depsgraph_get()
for obj in skin_meshes:
 evaluated[obj.name]=[obj.matrix_world@v.co for v in obj.evaluated_get(depsgraph).data.vertices]
turn=Matrix.Rotation(3.141592653589793,4,'Z')
source_points={n:turn@p for n,p in source_points.items()}
evaluated={n:[turn@p for p in pts] for n,pts in evaluated.items()}
before=set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(root/'TestePersonagem/Assets/Resources/GangsterMusculo/GangsterMusculo.fbx'))
added=set(bpy.context.scene.objects)-before
target=next(o for o in added if o.type=='ARMATURE')
target.animation_data_clear()
for p in target.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
prefix='Gangsta2_HIK:CC_Base_'
mapping={'ByBetinho':'Hip',' Pelvis':'Hip',' Spine':'Waist',' Spine1':'Spine02',' Neck':'NeckTwist01',' Head':'Head','Jaw':'Head','L Brow':'Head','R Brow':'Head','L breast':'Spine02','R breast':'Spine02','Belly':'Waist'}
for side in ['L','R']:
 for src,dst in [('UpperArm','Upperarm'),('ForeArm','Forearm'),('Hand','Hand'),('Thigh','Thigh'),('Calf','Calf'),('Foot','Foot'),('Toe0','ToeBase'),('Finger','Mid1')]:mapping[' '+side+' '+src]=side+'_'+dst
 mapping['Bip01 '+side+' Clavicle']=side+'_Clavicle'
 mapping[side+' Finger01']=side+'_Mid2'
mapping={k:prefix+v for k,v in mapping.items()}
target_points={b.name:target.matrix_world@b.head_local for b in target.data.bones}
children={' Pelvis':' Spine',' Spine':' Spine1',' Spine1':' Neck',' Neck':' Head'}
for side in ['L','R']:
 for b,c in [('UpperArm','ForeArm'),('ForeArm','Hand'),('Hand','Finger'),('Thigh','Calf'),('Calf','Foot'),('Foot','Toe0')]:children[' '+side+' '+b]=' '+side+' '+c
 children['Bip01 '+side+' Clavicle']=' '+side+' UpperArm'
ratio=(target_points[mapping[' Head']]-target_points[mapping[' Pelvis']]).length/(source_points[' Head']-source_points[' Pelvis']).length
transforms={}
for name,dest in mapping.items():
 rot=Matrix.Identity(3);scale=ratio
 if name in children:
  child=children[name];sv=source_points[child]-source_points[name];tv=target_points[mapping[child]]-target_points[dest]
  if sv.length>.02 and tv.length>.02:rot=sv.rotation_difference(tv).to_matrix();scale=max(ratio*.7,min(ratio*1.4,tv.length/sv.length))
 transforms[name]=(target_points[dest],rot,scale)
print('TRANSFER_DEBUG',ratio,[(n,list(source_points[n]),t[2]) for n,t in transforms.items()])
print('MESH_DEBUG',[(o.name,[min(p[i] for p in evaluated[o.name]) for i in range(3)],[max(p[i] for p in evaluated[o.name]) for i in range(3)]) for o in skin_meshes])
for obj in skin_meshes:
 oldgroups={g.index:g.name for g in obj.vertex_groups}; weights=[]
 for v in obj.data.vertices:
  world=evaluated[obj.name][v.index];point=Vector();total=0;new={}
  for g in v.groups:
   name=oldgroups[g.group]
   if name not in mapping:raise RuntimeError('Unmapped '+name)
   head,rot,scale=transforms[name]
   point+=(head+rot@((world-source_points[name])*scale))*g.weight;total+=g.weight
   dest=mapping[name];new[dest]=new.get(dest,0)+g.weight
  if total<=0:raise RuntimeError('Unweighted vertex')
  v.co=point/total;weights.append(new)
 obj.parent=None;obj.matrix_world=Matrix.Identity(4)
 obj.vertex_groups.clear()
 groups={name:obj.vertex_groups.new(name=name) for name in set(n for w in weights for n in w)}
 for i,w in enumerate(weights):
  for name,value in w.items():groups[name].add([i],value,'REPLACE')
 for modifier in list(obj.modifiers):obj.modifiers.remove(modifier)
 modifier=obj.modifiers.new('Standard skeleton','ARMATURE');modifier.object=target
 obj.parent=target;obj.matrix_parent_inverse=target.matrix_world.inverted()
for obj in added:
 if obj!=target and obj.type=='MESH':bpy.data.objects.remove(obj,do_unlink=True)
bpy.data.objects.remove(source,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in [target,*skin_meshes]:obj.select_set(True)
bpy.context.view_layer.objects.active=target
output=root/'tools/wuzimu-review/WuzimuStandard.fbx'
bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',path_mode='AUTO')
bpy.ops.wm.save_as_mainfile(filepath=str(root/'tools/wuzimu-review/WuzimuStandard.blend'))
print('STANDARD_RIG_EXPORTED',len(target.data.bones),len(skin_meshes),sum(len(o.data.vertices) for o in skin_meshes))
