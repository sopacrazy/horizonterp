import bpy,json
bpy.ops.wm.open_mainfile(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.blend')
print(json.dumps([dict(name=m.name,images=[n.image.filepath for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]) for m in bpy.data.materials if m.use_nodes]))
