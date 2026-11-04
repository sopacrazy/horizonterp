import bpy, json, pathlib
from mathutils import Vector

source=pathlib.Path(r'C:\SistemaAdr\gta\personagem\carro_policia.glb')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
points=[o.matrix_world @ Vector(corner) for o in meshes for corner in o.bound_box]
summary={
 'meshes':[{'name':o.name,'vertices':len(o.data.vertices),'materials':[m.name for m in o.data.materials]} for o in meshes],
 'bounds':{'min':[min(p[i] for p in points) for i in range(3)],'max':[max(p[i] for p in points) for i in range(3)]},
 'images':[{'name':i.name,'size':list(i.size)} for i in bpy.data.images],
}
print('POLICE_CAR_INSPECTION',json.dumps(summary,ensure_ascii=False))
