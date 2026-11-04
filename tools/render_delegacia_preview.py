from pathlib import Path
from collections import defaultdict
import numpy as np
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[1]
base=root/'TestePersonagem/Assets/GTASanAndreasModFull/ImportedModels'
obj=base/'delegacia.obj'
mtl=base/'delegacia.mtl'
colors={}
name=None
for line in mtl.read_text().splitlines():
    if line.startswith('newmtl '):name=line.split()[1]
    if line.startswith('Kd ') and name:
        colors[name]=tuple(round(float(v)*255) for v in line.split()[1:4])
vertices=[];faces=[];materials=[];current=None
for line in obj.open():
    if line.startswith('v '):vertices.append([float(x) for x in line.split()[1:4]])
    elif line.startswith('usemtl '):current=line.split()[1]
    elif line.startswith('f '):
        ids=[int(x.split('/')[0])-1 for x in line.split()[1:]]
        for i in range(1,len(ids)-1):faces.append((ids[0],ids[i],ids[i+1]));materials.append(current)
v=np.asarray(vertices,dtype=np.float32);f=np.asarray(faces,dtype=np.int32)
center=(v.min(axis=0)+v.max(axis=0))/2
angles=[0,90,180,270]
out=Image.new('RGB',(1600,1000),'#e8e1d2')
draw=ImageDraw.Draw(out)
for panel,degrees in enumerate(angles):
    a=np.deg2rad(degrees);forward=np.array([np.sin(a),0,np.cos(a)])
    right=np.array([np.cos(a),0,-np.sin(a)])
    relative=v-center
    xy=np.column_stack((relative@right,relative[:,1]*.98+relative@forward*.10))
    depth=relative@forward
    w,h=760,440
    span=max(np.ptp(xy[:,0])/w,np.ptp(xy[:,1])/h)*1.13
    pts=np.column_stack((xy[:,0]/span+w/2, h/2-xy[:,1]/span))
    order=np.argsort(depth[f].mean(axis=1))[::-1]
    tile=Image.new('RGB',(w,h),'#b7cad2');p=ImageDraw.Draw(tile)
    for index in order:
        tri=f[index];coords=[tuple(pts[j]) for j in tri]
        normal=np.cross(v[tri[1]]-v[tri[0]],v[tri[2]]-v[tri[0]])
        normal/=max(np.linalg.norm(normal),1e-5)
        light=.55+.45*max(0,np.dot(normal,np.array([-.3,.8,.5])))
        rgb=colors.get(materials[index],(160,160,160))
        fill=tuple(max(0,min(255,int(c*light))) for c in rgb)
        p.polygon(coords,fill=fill)
    x=20+(panel%2)*800;y=30+(panel//2)*500
    out.paste(tile,(x,y+24))
    draw.text((x+8,y),f'Delegacia - vista {degrees} graus',fill='#111111')
target=root/'tools/delegacia-vistas.png'
out.save(target)
print(target)
