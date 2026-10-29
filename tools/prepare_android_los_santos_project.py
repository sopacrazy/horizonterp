"""Create an isolated Android build project from Los Santos GUID dependencies.

The original full map is never moved, deleted or rewritten.
"""
from pathlib import Path
from collections import deque
import json,re,shutil,subprocess,time

root=Path(__file__).resolve().parents[1]
source=root/'TestePersonagem'
target=root/'TestePersonagemAndroidLS'
if target.exists():raise SystemExit('Isolated project already exists; inspect it before recreating.')
started=time.time()
print('Indexing source GUIDs...',flush=True)
result=subprocess.run(['rg','--hidden','--no-ignore','-g','*.meta','^guid: ',str(source/'Assets')],capture_output=True,text=True,encoding='utf-8',check=True)
index={}
for line in result.stdout.splitlines():
    match=re.match(r'(.+\.meta):guid: ([a-f0-9]{32})$',line)
    if match:index[match[2]]=Path(match[1][:-5])
print(f'GUIDs indexed: {len(index)}',flush=True)
pending=deque();selected=set();missing=set()
def add(path):
    path=Path(path)
    if path.is_file() and path not in selected:
        selected.add(path);pending.append(path)
        meta=Path(str(path)+'.meta')
        if meta.is_file() and meta not in selected:selected.add(meta);pending.append(meta)
        for parent in path.parents:
            if parent==source or source not in parent.parents:break
            meta=Path(str(parent)+'.meta')
            if meta.is_file() and meta not in selected:selected.add(meta);pending.append(meta)
def tree(folder):
    if folder.is_dir():
        for path in folder.rglob('*'):
            if path.is_file():add(path)
for folder in ['Resources','Startup','Branding','Plugins']:tree(source/'Assets'/folder)
for path in (source/'Assets').rglob('*.cs'):add(path)
for name in ['Entrada.unity','TestePersonagem.unity','SanAndreasIntegration.enabled']:
    add(source/'Assets/Scenes'/name)
sector_count=0
for path in (source/'Assets/Scenes/GTASanAndreasModFullSectors').glob('*.unity'):
    m=re.fullmatch(r'GTASASector_([mp])(\d+)_([mp])(\d+)',path.stem)
    if not m:continue
    x=int(m[2])*(-1 if m[1]=='m' else 1);y=int(m[4])*(-1 if m[3]=='m' else 1)
    if -2<=x<=12 and -13<=y<=-2:add(path);sector_count+=1
assert sector_count==146,sector_count
text_extensions={'.unity','.prefab','.mat','.asset','.controller','.mask','.overridecontroller',
                 '.meta','.tss','.uss','.uxml','.shader','.cginc','.hlsl','.compute','.json','.mtl'}
while pending:
    path=pending.popleft()
    if path.suffix.lower()=='.obj':add(path.with_suffix('.mtl'))
    if path.suffix.lower() not in text_extensions:continue
    data=path.read_text(encoding='utf-8',errors='ignore')
    for guid in re.findall(r'\bguid:\s*([a-f0-9]{32})',data):
        if guid in index:add(index[guid])
        elif not guid.startswith('0000000000000000'):missing.add(guid)
    if path.suffix.lower() in {'.shader','.cginc','.hlsl','.compute'}:
        for name in re.findall(r'#include\s+"([^"]+)"',data):
            linked=path.parent/name
            if linked.is_file() and source in linked.resolve().parents:add(linked.resolve())
    if path.suffix.lower()=='.mtl':
        for name in re.findall(r'^map_\w+\s+(.+)$',data,re.MULTILINE):
            linked=(path.parent/name.strip()).resolve()
            if source in linked.parents:add(linked)
print(f'Copying {len(selected)} dependency files; {sector_count} sectors...',flush=True)
target.mkdir()
for folder in ['ProjectSettings','Packages']:
    shutil.copytree(source/folder,target/folder)
total=0
for count,path in enumerate(sorted(selected),1):
    destination=target/path.relative_to(source)
    destination.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,destination);total+=path.stat().st_size
    if count%5000==0:print(f'Copied {count}/{len(selected)}',flush=True)
report={'source':str(source),'project':str(target),'files':len(selected),'sectors':sector_count,
        'bytes':total,'missingGuids':sorted(missing),'seconds':round(time.time()-started,2),
        'fullSourcePreserved':True}
folder=root/'tools/android-los-santos';folder.mkdir(parents=True,exist_ok=True)
(folder/'isolated-project.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report),flush=True)
