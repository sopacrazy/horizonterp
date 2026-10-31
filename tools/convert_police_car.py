import bpy
from mathutils import Vector
from pathlib import Path

root=Path(r'C:\SistemaAdr\gta')
source=root/'personagem'/'carro_policia.glb'
output=root/'TestePersonagem'/'Assets'/'Resources'/'Vehicles'/'Police'/'PoliceCar.fbx'
output.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source))
meshes=[obj for obj in bpy.context.scene.objects if obj.type=='MESH']
points=[obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
lo=Vector([min(point[i] for point in points) for i in range(3)])
hi=Vector([max(point[i] for point in points) for i in range(3)])
center=(lo+hi)*.5
scale=4.55/(hi.y-lo.y)
root_object=bpy.data.objects.new('PoliceCar',None)
bpy.context.collection.objects.link(root_object)
for obj in list(bpy.context.scene.objects):
    if obj!=root_object and obj.parent is None:
        world=obj.matrix_world.copy()
        obj.parent=root_object
        obj.matrix_world=world
root_object.scale=(scale,scale,scale)
root_object.location=(-center.x*scale,-center.y*scale,-lo.z*scale)
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active=root_object
bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,object_types={'MESH','EMPTY'},
                         add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
                         apply_scale_options='FBX_SCALE_UNITS',path_mode='AUTO')
print(f'POLICE_CAR_CONVERTED meshes={len(meshes)} scale={scale:.5f} output={output} bytes={output.stat().st_size}')
