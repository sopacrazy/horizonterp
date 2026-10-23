import bpy,pathlib,json
p=pathlib.Path(r'C:\SistemaAdr\gta\personagem_unity')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(p/'ByBeyby.fbx'))
bpy.context.view_layer.update()
print('ALL',[(o.name,o.type,o.users) for o in bpy.data.objects])
result=[]
for o in bpy.context.scene.objects:
 if o.type=='ARMATURE':result.append(dict(type=o.type,bones=len(o.data.bones)))
 if o.type=='MESH':result.append(dict(type=o.type,vertices=len(o.data.vertices),triangles=sum(len(x.vertices)-2 for x in o.data.polygons),unweighted=sum(not any(g.weight>0 for g in v.groups) for v in o.data.vertices),armature=any(m.type=='ARMATURE' and m.object for m in o.modifiers),height=o.dimensions.z))
(p/'fbx_roundtrip.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
assert any(x.get('bones')==32 for x in result)
assert any(x.get('vertices')==24930 and x.get('unweighted')==0 and x.get('armature') for x in result)

