import bpy
rests={}
for tag,file in [('target','personagem_unity/ByBeyby.fbx'),('run','personagem/Fast Run.fbx')]:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath='C:/SistemaAdr/gta/'+file);a=next(o for o in bpy.data.objects if o.type=='ARMATURE');rests[tag]={b.name:(a.matrix_world@b.matrix_local).copy() for b in a.data.bones}
for n in [' Pelvis',' Spine',' Spine1',' L Thigh',' L Calf',' L UpperArm',' L ForeArm']:
 x,y=rests['target'][n],rests['run'][n];print(n,'restAngle',x.to_quaternion().rotation_difference(y.to_quaternion()).angle*57.2958,'posDelta',list(y.translation-x.translation))
