from pathlib import Path
import json,math,numpy as np
from PIL import Image,ImageFilter,ImageDraw
root=Path('TestePersonagem/Assets/GTASanAndreasModFull/ImportedModels')
rows=[json.loads(l) for l in Path('tools/gta-sa-import/output/placements.jsonl').open()]
rng=np.random.default_rng(12);points=[];cache={}
for p in rows:
 n=p['model_name'].lower()
 if p.get('scene_layer')!='exterior' or not(-150<p['x']<3100 and -2900<p['y']<-650) or 'lod' in n:continue
 if n not in cache:
  obj=root/(n+'.obj');meta=root/(n+'.materials.json')
  if not obj.exists() or not meta.exists():cache[n]=None;continue
  mats={m['material'] for m in json.loads(meta.read_text())['materials'] if any(t in str(m.get('textures',[])).lower() for t in ['road','asphalt','crossing'])}
  if not mats:cache[n]=None;continue
  verts=[];faces=[];selected=False
  for l in obj.open():
   if l.startswith('v '):
    a,b,c=map(float,l.split()[1:4]);verts.append((-a,-c,b))
   elif l.startswith('usemtl '):selected=l.split()[1] in mats
   elif selected and l.startswith('f '):
    ids=[int(x.split('/')[0])-1 for x in l.split()[1:]]
    for j in range(1,len(ids)-1):faces.append((ids[0],ids[j],ids[j+1]))
  if not faces:cache[n]=None;continue
  v=np.array(verts);tri=v[np.array(faces)];norm=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);area=np.linalg.norm(norm,axis=1)*.5
  good=(area>2)&(abs(norm[:,2])/(np.linalg.norm(norm,axis=1)+1e-9)>.75)
  tri=tri[good];area=area[good]
  samples=[]
  for t,a in zip(tri,area):
   count=min(30,max(1,int(a/80)));uv=rng.random((count,2));uv[uv.sum(axis=1)>1]=1-uv[uv.sum(axis=1)>1]
   samples.extend(t[0]+uv[:,0,None]*(t[1]-t[0])+uv[:,1,None]*(t[2]-t[0]))
  cache[n]=np.array(samples) if samples else None
 v=cache[n]
 if v is None:continue
 q=np.array([p[k] for k in ['qx','qy','qz','qw']]);q/=np.linalg.norm(q)
 uv=np.cross(q[:3],v);rv=v+2*q[3]*uv+2*np.cross(q[:3],uv)
 points.extend(np.stack([rv[:,0]+p['x'],-rv[:,1]-p['y']],axis=1))
points=np.array(points);points=points[(points[:,0]>100)&(points[:,0]<2850)&(points[:,1]>850)&(points[:,1]<2700)]
if len(points)>18000:points=points[rng.choice(len(points),18000,replace=False)]
im=Image.open('personagem/los_santos.png').convert('RGB').resize((428,300),Image.Resampling.LANCZOS);rgb=np.array(im);dark=(rgb.max(axis=2)<90).astype(np.uint8)*255
soft=np.array(Image.fromarray(dark).filter(ImageFilter.GaussianBlur(.8)))/255
xy=points

def score(par):
 sx,sy,ox,oy=par;x=np.rint(xy[:,0]*sx+ox).astype(int);y=np.rint(xy[:,1]*sy+oy).astype(int)
 good=(x>=0)&(x<im.width)&(y>=0)&(y<im.height)
 return float(soft[y[good],x[good]].mean())
par=np.array([.15,.15,-12,-118.5]);initial=score(par)
for step,shift in [(.003,8),(.001,3),(.0003,1),(.0001,.3)]:
 for repeat in range(6):
  old=par.copy()
  for dim,delta in enumerate([step,step,shift,shift]):
   opts=[]
   for off in range(-3,4):
    trial=par.copy();trial[dim]+=off*delta
    if not(.13<trial[0]<.175 and .13<trial[1]<.175 and -35<trial[2]<10 and -155<trial[3]<-70):continue
    opts.append((score(trial),trial))
   par=max(opts,key=lambda a:a[0])[1]
  if np.array_equal(par,old):break
sx,sy,ox,oy=par
report={'points':len(points),'initialScore':initial,'fitScore':score(par),'pixelTransform':par.tolist(),'min':[float(-ox/sx),float(-oy/sy)],'max':[float((im.width-ox)/sx),float((im.height-oy)/sy)]}
Path('tools/city-map-fit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
over=im.resize((1284,900),Image.Resampling.NEAREST);draw=ImageDraw.Draw(over)
for x,z in points[::12]:
 px=(x*sx+ox)*3;py=(z*sy+oy)*3;draw.ellipse((px-1,py-1,px+1,py+1),fill=(255,0,0))
over.save('tools/city-map-road-fit.png')
