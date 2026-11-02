"""Convert the supplied Legacy.oiv rifle to static FBX with named grip sockets.

Requires the existing CodeWalker converter to have exported tools/rifle-import/xml.
Run with Blender 4.2: blender --background --python tools/convert_rifle.py
Only high-detail visual geometry from the normal world model is used, not _hi.
"""
import bpy, json, xml.etree.ElementTree as ET
from mathutils import Vector, Matrix, Quaternion
from pathlib import Path

ROOT=Path(__file__).resolve().parent.parent
SRC=ROOT/'tools/rifle-import/xml'
OUT=ROOT/'TestePersonagem/Assets/Resources/Weapons/Rifle'
OUT.mkdir(parents=True,exist_ok=True)
WIDTHS={'Position':3,'Normal':3,'Colour0':4,'Colour1':4,'TexCoord0':2,'TexCoord1':2,
        'TexCoord2':2,'TexCoord3':2,'Tangent':4,'BlendWeights':4,'BlendIndices':4}
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
document=ET.parse(SRC/'w_ar_heavyrifleh.ydr.xml').getroot()
bone_matrices={};bone_by_name={}
for bone in document.findall('Skeleton/Bones/Item'):
    def xyzw(tag,letters): return [float(bone.find(tag).get(a)) for a in letters]
    t=Vector(xyzw('Translation','xyz'));q=xyzw('Rotation','xyzw')
    local=Matrix.LocRotScale(t,Quaternion((q[3],q[0],q[1],q[2])),Vector(xyzw('Scale','xyz')))
    index=int(bone.find('Index').get('value'));parent=int(bone.find('ParentIndex').get('value'))
    bone_matrices[index]=(bone_matrices[parent]@local) if parent>=0 else local
    bone_by_name[bone.findtext('Name')]=bone_matrices[index].translation
grip=bone_by_name['Gun_GripR']
# GTA barrel +X/up +Z -> Blender barrel -Y/up +Z -> Unity barrel +Z/up +Y.
def convert(p):
    p=Vector(p)-grip
    return (-p.y,-p.x,p.z)

root=bpy.data.objects.new('HeavyRifle',None);bpy.context.collection.objects.link(root)
textures={}
for filename in ['w_ar_heavyrifleh','w_ar_heavyrifleh_stock','w_ar_heavyrifleh_mag1']:
    image=bpy.data.images.load(str(SRC/'textures'/(filename+'.dds')))
    # Source diffuse files are monochrome tint masks; Unity material supplies finish color.
    scale=min(1.0,1024/max(image.size));image.scale(round(image.size[0]*scale),round(image.size[1]*scale))
    image.filepath_raw=str(OUT/(filename+'.png'));image.file_format='PNG';image.save()
    textures[filename]=image

materials={}
def material(texture):
    if texture not in materials:
        m=bpy.data.materials.new(texture);m.use_nodes=True
        nodes=m.node_tree.nodes; shader=nodes.get('Principled BSDF')
        shader.inputs['Roughness'].default_value=.64;shader.inputs['Metallic'].default_value=.25
        image=nodes.new('ShaderNodeTexImage');image.image=textures[texture]
        m.node_tree.links.new(image.outputs['Color'],shader.inputs['Base Color'])
        m.diffuse_color=(.24,.26,.28,1)
        materials[texture]=m
    return materials[texture]

stats=[]
def import_document(doc,name,translation=None):
    shaders=[]
    for shader in doc.findall('ShaderGroup/Shaders/Item'):
        tex=shader.findtext("Parameters/Item[@name='DiffuseSampler']/Name",'').lower()
        shaders.append(tex)
    for i,g in enumerate(doc.findall('DrawableModelsHigh/Item/Geometries/Item')):
        shader=int(g.find('ShaderIndex').get('value'));tex=shaders[shader]
        # Skip decorative decal layers whose specialist GTA shaders are not transferable.
        if tex not in textures:continue
        offsets={};stride=0
        for a in g.find('VertexBuffer/Layout'):
            offsets[a.tag]=stride;stride+=WIDTHS[a.tag]
        vertices=[];uvs=[]
        for line in (g.findtext('VertexBuffer/Data') or g.findtext('VertexBuffer/Data2')).splitlines():
            if not line.strip():continue
            values=list(map(float,line.split())); assert len(values)==stride
            p=Vector(values[offsets['Position']:offsets['Position']+3])
            if translation is not None:p+=translation
            vertices.append(convert(p)); u=offsets['TexCoord0'];uvs.append(values[u:u+2])
        indices=list(map(int,g.findtext('IndexBuffer/Data').split()))
        faces=[(indices[j],indices[j+2],indices[j+1]) for j in range(0,len(indices),3)]
        mesh=bpy.data.meshes.new(name+str(i));mesh.from_pydata(vertices,[],faces);mesh.update()
        obj=bpy.data.objects.new(name+str(i),mesh);bpy.context.collection.objects.link(obj);obj.parent=root
        mesh.materials.append(material(tex));layer=mesh.uv_layers.new(name='UVMap')
        for polygon in mesh.polygons:
            polygon.use_smooth=True
            for li in polygon.loop_indices:
                u,v=uvs[mesh.loops[li].vertex_index];layer.data[li].uv=(u,1-v)
        stats.append({'mesh':obj.name,'vertices':len(vertices),'triangles':len(faces)})
import_document(document,'RifleBody')
mag=ET.parse(SRC/'w_ar_heavyrifleh_mag1.ydr.xml').getroot()
import_document(mag,'Magazine',bone_by_name['WAPClip'])
for name,p in [('GripRight',grip),('GripLeft',bone_by_name['WAPGrip']),
               ('Muzzle',bone_by_name['Gun_Muzzle']),('Stock',bone_by_name['NM_Butt_Marker'])]:
    obj=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(obj);obj.parent=root;obj.location=convert(p)
bpy.ops.object.select_all(action='SELECT');bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'HeavyRifle.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
    add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
    apply_scale_options='FBX_SCALE_UNITS',path_mode='AUTO')
report={'source':'personagem/arma/Legacy.oiv','meshes':stats,'axis':'Unity +Z barrel, +Y up',
        'pivot':'GripRight at origin, model authored in meters','sockets':{n:list(convert(p)) for n,p in bone_by_name.items()
        if n in ('Gun_GripR','Gun_Muzzle','WAPGrip','NM_Butt_Marker')}}
(ROOT/'tools/rifle-import/conversion.json').write_text(json.dumps(report,indent=2))
print('RIFLE_CONVERTED '+json.dumps(report))
