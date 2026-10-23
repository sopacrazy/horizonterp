import bpy,json
for fn in ['Fast Run.fbx','Walking Left Turn.fbx']:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath='C:/SistemaAdr/gta/personagem/'+fn)
 a=next(o for o in bpy.data.objects if o.type=='ARMATURE');act=a.animation_data.action;s,e=act.frame_range
 allframes=[]
 for f in range(int(s),int(e)+1):
  bpy.context.scene.frame_set(f);allframes.append({b.name:((a.matrix_world@b.matrix).to_quaternion().copy(),(a.matrix_world@b.matrix).translation.copy()) for b in a.pose.bones})
 diffs=sorted([(round(allframes[0][n][0].rotation_difference(allframes[-1][n][0]).angle*57.2958,2),n) for n in allframes[0]],reverse=True)
 print('DIAG',fn,'end angles',diffs[:8]);print('PELVIS',[tuple(round(x,4) for x in d[' Pelvis'][1]) for d in allframes]);print('YAW', [tuple(round(x*57.2958,2) for x in d[' Pelvis'][0].to_euler()) for d in allframes])

