"""Select reproducible runtime sources and map dependencies; never stage work caches or credentials."""
from pathlib import Path
from collections import deque
import subprocess,re,json,argparse
root=Path(__file__).resolve().parents[1];project=root/'TestePersonagem'
parser=argparse.ArgumentParser();parser.add_argument('--stage',action='store_true');args=parser.parse_args()
index={}
found=subprocess.run(['rg','--hidden','--no-ignore','-g','*.meta','^guid: ',str(project/'Assets')],capture_output=True,text=True,encoding='utf-8',check=True)
for line in found.stdout.splitlines():
 match=re.match(r'(.+\.meta):guid: ([a-f0-9]{32})$',line)
 if match:index[match[2]]=Path(match[1][:-5])
selected=set();pending=deque();missing=set()
def add(path):
 path=Path(path)
 if not path.is_file() or path in selected:return
 selected.add(path);pending.append(path)
 if not str(path).endswith('.meta'):add(Path(str(path)+'.meta'))
 if project in path.parents:
  for parent in path.parents:
   if parent==project:break
   add(Path(str(parent)+'.meta'))
def tree(path):
 if path.is_dir():
  for file in path.rglob('*'):
   if file.is_file():add(file)
for folder in ['Resources','Animations','Scripts','Editor','Plugins','Startup','Branding']:tree(project/'Assets'/folder)
for folder in ['ProjectSettings','Packages']:tree(project/folder)
for folder in ['GTASanAndreasModFullSectors','AndroidLosSantos']:tree(project/'Assets/Scenes'/folder)
for name in ['Entrada.unity','TestePersonagem.unity','LosSantosAndroid.enabled','SanAndreasIntegration.enabled']:add(project/'Assets/Scenes'/name)
for folder in ['Backend/api','Backend/database','Backend/migration']:
 for file in (root/folder).rglob('*'):
  if any(x in file.parts for x in ['node_modules','__pycache__','validation','test-results','credentials','backups']):continue
  if file.is_file() and file.suffix in ['.mjs','.py','.sql','.sh','.md'] and not file.name.startswith('PROMPT'):add(file)
for name in ['package.json','package-lock.json','arrival-config.json']:add(root/'Backend/api'/name)
add(root/'Backend/migration/ApiConfig.kvm1.json')
for name in ['README.md','Jogar.bat','releases/README.md','releases/notes-0.7.0.md','personagem/los_santos.png','personagem/Los-Santos.webp']:add(root/name)
for name in ['publish_android_update.py','verify_android_release.py','verify_published_release.py','package_windows_070.py','prepare_android_los_santos_project.py','stage_android_map.py','sync_android_los_santos_profile.py','stage_release_070.py','calibrate_city_map.py','convert_police_car.py']:add(root/'tools'/name)
extensions={'.unity','.prefab','.mat','.asset','.controller','.mask','.overridecontroller','.meta','.tss','.uss','.uxml','.shader','.cginc','.hlsl','.compute','.json','.mtl'}
while pending:
 path=pending.popleft()
 if path.suffix.lower()=='.obj':add(path.with_suffix('.mtl'))
 if path.suffix.lower() not in extensions:continue
 text=path.read_text(encoding='utf-8',errors='ignore')
 for guid in re.findall(r'\bguid:\s*([a-f0-9]{32})',text):
  if guid in index:add(index[guid])
  elif not guid.startswith('0000000000000000'):missing.add(guid)
 if path.suffix.lower()=='.mtl':
  for name in re.findall(r'^map_\w+\s+(.+)$',text,re.MULTILINE):
   linked=(path.parent/name.strip()).resolve()
   if project in linked.parents:add(linked)
tracked=set(subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0'))
paths=sorted(p.relative_to(root).as_posix() for p in selected)
new=[p for p in paths if p not in tracked]
large=[p for p in paths if (root/p).stat().st_size>=100*1024*1024]
report={'selected':len(paths),'newFiles':len(new),'newBytes':sum((root/p).stat().st_size for p in new),'missingGuids':sorted(missing),'oversize':large}
(root/'tools/release070-git-paths.txt').write_text('\n'.join(paths)+'\n',encoding='utf-8')
(root/'tools/release070-source-selection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report))
if args.stage:
 assert not missing and not large,'Review missing GUIDs and large files before staging'
 subprocess.run(['git','update-index','--add','-z','--stdin'],input=('\0'.join(paths)+'\0').encode(),cwd=root,check=True)
