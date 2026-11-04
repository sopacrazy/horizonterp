"""Convert user-provided Sakeland CodeWalker XML to compact Unity test inputs.

Run tools/sakeland/Converter first. Conversion tools remain outside the player.
"""
from pathlib import Path
import collections, hashlib, json, struct, sys, xml.etree.ElementTree as ET
sys.path.insert(0, str(Path(__file__).parent / 'video_libs'))
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'tools/sakeland/xml'
OUT = ROOT / 'tools/sakeland/processed'
TEXTURES = ROOT / 'TestePersonagem/Assets/SakelandTest/Textures'
OUT.mkdir(parents=True, exist_ok=True)
TEXTURES.mkdir(parents=True, exist_ok=True)
WIDTHS = {'Position':3,'Normal':3,'Colour0':4,'Colour1':4,'TexCoord0':2,'TexCoord1':2,'TexCoord2':2,
          'TexCoord3':2,'TexCoord4':2,'TexCoord5':2,'TexCoord6':2,'TexCoord7':2,'Tangent':4,
          'BlendWeights':4,'BlendIndices':4}

def vector(node):
    return [float(node.get(a, 0)) for a in 'xyz']

def meshfile(name, positions, normals, uvs, indices):
    assert len(indices) % 3 == 0 and all(0 <= i < len(positions) for i in indices)
    with (OUT / (name+'.meshbin')).open('wb') as f:
        f.write(struct.pack('<ii', len(positions), len(indices)))
        for p,n,uv in zip(positions,normals,uvs):
            f.write(struct.pack('<8f',p[0],p[2],p[1],n[0],n[2],n[1],uv[0],1-uv[1]))
        # Swapping GTA Y/Z reverses handedness.
        for i in range(0,len(indices),3):
            f.write(struct.pack('<iii',indices[i],indices[i+2],indices[i+1]))

models, wanted_textures = [], set()
omitted = {'beach_tiki_bar_light_strips'}  # 15 MB of tiny light geometry; use a few Unity lights.
for path in sorted(SOURCE.glob('*.ydr.xml')):
    name=path.name.removesuffix('.ydr.xml')
    if name in omitted or name.startswith('vb_'): continue
    root=ET.parse(path).getroot()
    shaders=[]
    for shader in root.findall('ShaderGroup/Shaders/Item'):
        params={item.get('name'):item for item in shader.findall('Parameters/Item')}
        def texture(parameter):
            node=params.get(parameter)
            return node.findtext('Name','') if node is not None else ''
        material={'diffuse':texture('DiffuseSampler'),'normal':texture('BumpSampler'),
                  'bucket':int(shader.find('RenderBucket').get('value','0'))}
        shaders.append(material)
        wanted_textures.update(v for k,v in material.items() if k in ('diffuse','normal') and v)
    geometries=[]
    for index, geometry in enumerate(root.findall('DrawableModelsHigh/Item/Geometries/Item')):
        layout=geometry.find('VertexBuffer/Layout')
        offsets={};offset=0
        for attribute in layout:
            offsets[attribute.tag]=offset;offset+=WIDTHS[attribute.tag]
        positions,normals,uvs=[],[],[]
        vertex_data=geometry.findtext('VertexBuffer/Data') or geometry.findtext('VertexBuffer/Data2','')
        for line in vertex_data.splitlines():
            if not line.strip():continue
            values=list(map(float,line.split()))
            if len(values)!=offset:raise ValueError((name,len(values),offset))
            p=offsets['Position'];positions.append(values[p:p+3])
            n=offsets.get('Normal');normals.append(values[n:n+3] if n is not None else [0,0,1])
            uv=offsets.get('TexCoord0');uvs.append(values[uv:uv+2] if uv is not None else [0,0])
        indices=list(map(int,geometry.findtext('IndexBuffer/Data','').split()))
        mesh_name=f'{name}_{index:02}'
        meshfile(mesh_name,positions,normals,uvs,indices)
        geometries.append({'mesh':mesh_name,'shader':int(geometry.find('ShaderIndex').get('value')),
                           'vertices':len(positions),'triangles':len(indices)//3})
    models.append({'name':name,'shaders':shaders,'geometries':geometries})

placements=[]; unknown=collections.Counter()
known={m['name'] for m in models}
for entity in ET.parse(SOURCE/'beach_tiki_bar_placements.ymap.xml').findall('entities/Item'):
    name=entity.findtext('archetypeName')
    if name not in known:
        unknown[name]+=1;continue
    rotation=entity.find('rotation')
    # YMAP stores the inverse quaternion. After changing handedness, converted
    # Unity rotation is (qx,qz,qy,qw) from that stored quaternion.
    q=[float(rotation.get(a)) for a in 'xyzw']
    placements.append({'name':name,'position':vector(entity.find('position')),
                       'rotation':[q[0],q[2],q[1],q[3]],
                       'scaleXY':float(entity.find('scaleXY').get('value')),
                       'scaleZ':float(entity.find('scaleZ').get('value'))})

# The supplied club collision is already in world space, matching its YMAP placement.
collision=[]
for i, child in enumerate(ET.parse(SOURCE/'beach_tiki_bar_01_collision.ybn.xml').findall('Bounds/Children/Item')):
    center=vector(child.find('GeometryCenter'))
    positions=[]
    for line in child.findtext('Vertices','').splitlines():
        if line.strip(): positions.append([float(v)+c for v,c in zip(line.split(','),center)])
    indices=[]
    for triangle in child.findall('Polygons/Triangle'):
        indices.extend(int(triangle.get(a)) for a in ('v1','v2','v3'))
    if not positions or not indices:continue
    transform=list(map(float,child.findtext('CompositeTransform').split()))
    assert transform == [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1], 'Nonidentity collision transform'
    name=f'club_collision_{i}'
    meshfile(name,positions,[[0,0,1]]*len(positions),[[0,0]]*len(positions),indices)
    collision.append({'mesh':name,'triangles':len(indices)//3})

missing=[]
for name in sorted(wanted_textures):
    source=SOURCE/'textures'/(name+'.dds')
    if not source.exists():missing.append(name);continue
    image=Image.open(source).convert('RGBA')
    image.thumbnail((1024,1024),Image.Resampling.LANCZOS)
    image.save(TEXTURES/(name+'.png'), optimize=True)

manifest={'models':models,'placements':placements,'collision':collision,'origin':[-2052.2861,-547.4264,3.0],
          'omitted':sorted(omitted),'unresolvedPlacements':dict(unknown),'missingTextures':missing}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
summary={'models':len(models),'placements':len(placements),'geometries':sum(len(m['geometries']) for m in models),
         'modelTriangles':sum(g['triangles'] for m in models for g in m['geometries']),
         'textures':len(wanted_textures)-len(missing),'missingTextures':missing,'unresolvedPlacements':dict(unknown),
         'collisionTriangles':sum(c['triangles'] for c in collision)}
(OUT/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
