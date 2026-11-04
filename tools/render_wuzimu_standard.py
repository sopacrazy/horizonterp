import bpy,sys
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
bpy.ops.wm.open_mainfile(filepath=args[0] if args else 'C:/SistemaAdr/gta/tools/wuzimu-review/WuzimuStandard.blend')
for o in bpy.context.scene.objects:
 if o.type=='ARMATURE':o.hide_render=True
bpy.ops.object.camera_add(location=(2,-5,2));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=2.4;bpy.context.scene.camera=cam
for loc in [(2,-3,4),(-2,-2,2)]:
 bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=400;o.data.size=3;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.world.color=(.2,.2,.2);s.render.resolution_x=500;s.render.resolution_y=700;s.render.resolution_percentage=100;s.render.filepath=args[1] if len(args)>1 else 'C:/SistemaAdr/gta/tools/wuzimu-review/standard-preview.png';bpy.ops.render.render(write_still=True)
