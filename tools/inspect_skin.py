import bpy,sys,importlib.util,pathlib,json
root=pathlib.Path(r'C:\SistemaAdr\gta'); out=root/'personagem_unity'; out.mkdir(exist_ok=True)
spec=importlib.util.spec_from_file_location('DragonFF',root/'tools/DragonFF-master/__init__.py',submodule_search_locations=[str(root/'tools/DragonFF-master')]); mod=importlib.util.module_from_spec(spec); sys.modules['DragonFF']=mod; spec.loader.exec_module(mod); mod.register()
from DragonFF.ops import dff_importer,txd_importer
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
txd=txd_importer.import_txd({'file_name':str(root/'personagem/bybeyby.txd'),'skip_mipmaps':True,'pack':True})
dff_importer.import_dff(dict(file_name=str(root/'personagem/bybeyby.dff'),txd_images=txd.images,image_ext='png',connect_bones=False,use_mat_split=False,remove_doubles=False,create_backfaces=False,group_materials=False,import_normals=True,materials_naming='DEF'))
report=[]
for o in bpy.context.scene.objects:
 d=dict(name=o.name,type=o.type,dimensions=list(o.dimensions))
 if o.type=='ARMATURE': d['bones']=[dict(name=b.name,parent=b.parent.name if b.parent else None,head=list(b.head_local),tail=list(b.tail_local),props=dict(b.items())) for b in o.data.bones]
 if o.type=='MESH':
  d.update(vertices=len(o.data.vertices),triangles=sum(len(p.vertices)-2 for p in o.data.polygons),groups=[g.name for g in o.vertex_groups],unweighted=sum(not any(g.weight>0 for g in v.groups) for v in o.data.vertices),modifiers=[(m.type,m.object.name if m.type=='ARMATURE' and m.object else None) for m in o.modifiers])
 report.append(d)
(out/'inspection.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'personagem_importado.blend'))
print(json.dumps(report))
