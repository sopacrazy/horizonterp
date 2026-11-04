"""Split oversized release assets without changing their original ZIP contents."""
import hashlib,json,pathlib
root=pathlib.Path(__file__).resolve().parent/'format-backup-20261104'
path=root/'manifest.json';manifest=json.loads(path.read_text())
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(2**20),b''):h.update(b)
 return h.hexdigest()
for entry in manifest['archives']:
 if entry['bytes']<1900*1024**2:continue
 file=root/entry['file'];parts=[]
 existing=sorted(root.glob(file.name+'.part[0-9][0-9][0-9]'))
 if existing and sum(p.stat().st_size for p in existing)==entry['bytes']:
  joined=hashlib.sha256()
  for part in existing:
   with part.open('rb') as src:
    for data in iter(lambda:src.read(2**20),b''):joined.update(data)
   parts.append({'file':part.name,'bytes':part.stat().st_size,'sha256':sha(part)})
  if joined.hexdigest()==entry['sha256']:
   entry['parts']=parts;print('REUSED_VERIFIED_PARTS',entry['file'],len(parts),flush=True);continue
  parts=[]
 with file.open('rb') as src:
  index=1
  while True:
   data=src.read(2**20)
   if not data:break
   part=root/(file.name+'.part%03d'%index)
   with part.open('wb') as dst:
    dst.write(data);remaining=1024**3-len(data)
    while remaining>0:
     data=src.read(min(2**20,remaining))
     if not data:break
     dst.write(data);remaining-=len(data)
   parts.append({'file':part.name,'bytes':part.stat().st_size,'sha256':sha(part)});index+=1
 entry['parts']=parts;print('SPLIT',entry['file'],len(parts),flush=True)
path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
