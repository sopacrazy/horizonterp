import bpy,pathlib,json,math
from mathutils import Vector
root=pathlib.Path(r'C:\SistemaAdr\gta'); out=root/'personagem_unity'
bpy.ops.wm.open_mainfile(filepath=str(out/'personagem_importado.blend'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH'); arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh.name='ByBeyby_Mesh'; arm.name='ByBeyby_Rig'
texdir=out/'Textures'; texdir.mkdir(exist_ok=True)
textures=[]
for img in bpy.data.images:
 if img.type=='IMAGE' and img.size[0]>0:
  name=img.name.split('/')[-2] if '/' in img.name else img.name
  img.filepath_raw=str(texdir/(name+'.png')); img.file_format='PNG'; img.save(); textures.append(dict(name=name,size=list(img.size)))
# Translate the complete rig and mesh together so the soles rest at ground level.
minz=min((mesh.matrix_world@Vector(v)).z for v in mesh.bound_box)
for o in list(bpy.context.scene.objects):
 if o.parent is None:o.location.z-=minz
bpy.context.view_layer.update()
# Check the supplied skin under a small joint rotation, then restore it.
base=[mesh.matrix_world@v.co for v in mesh.data.vertices]
pb=arm.pose.bones[' L ForeArm']; pb.rotation_mode='XYZ'; pb.rotation_euler.x=0.35
bpy.context.view_layer.update(); ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()); em=ev.to_mesh()
deltas=[((ev.matrix_world@v.co)-base[i]).length for i,v in enumerate(em.vertices)]
ev.to_mesh_clear(); pb.rotation_euler.x=0; bpy.context.view_layer.update()
checks=dict(bones=len(arm.data.bones),vertices=len(mesh.data.vertices),triangles=sum(len(p.vertices)-2 for p in mesh.data.polygons),unweighted_vertices=sum(not any(g.weight>0 for g in v.groups) for v in mesh.data.vertices),max_influences=max(sum(g.weight>0 for g in v.groups) for v in mesh.data.vertices),max_weight_sum_error=max(abs(sum(g.weight for g in v.groups)-1) for v in mesh.data.vertices),forearm_test_moved_vertices=sum(d>0.0001 for d in deltas),forearm_test_max_displacement=max(deltas),textures=textures,animation_actions=len(bpy.data.actions))
(out/'validation.json').write_text(json.dumps(checks,indent=2))
bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(out/'ByBeyby.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',path_mode='COPY',embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'ByBeyby.blend'))
# Render a simple review image; staging is excluded from the delivered model.
for mat in mesh.data.materials:
 if mat and mat.use_nodes:
  bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
  if bs:bs.inputs['Roughness'].default_value=0.85
bpy.ops.object.camera_add(location=(2.6,-5.5,2.4)); cam=bpy.context.object; cam.rotation_euler=(Vector((0,0,0.95))-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.type='ORTHO'; cam.data.ortho_scale=2.5; bpy.context.scene.camera=cam
for loc,power,size in [((2,-4,5),450,4),((-3,-1,3),250,3),((0,3,4),350,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene; s.render.engine='CYCLES'; s.cycles.samples=16;s.world.color=(0.15,0.15,0.15);s.render.resolution_x=700;s.render.resolution_y=800;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.filepath=str(out/'preview.png');bpy.ops.render.render(write_still=True)
print('VALIDATION',json.dumps(checks))
