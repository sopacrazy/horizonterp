"""Sync only source/config into the existing isolated Android project; retain map import caches."""
from pathlib import Path
import hashlib,json,shutil,time
root=Path(__file__).resolve().parents[1];source=root/'TestePersonagem';target=root/'TestePersonagemAndroidLS'
backup=root/'tools/release-071/sync-backup';changed=[]
assert (target/'Assets/Scenes/LosSantosAndroid.enabled').exists()
files=[]
for folder in ['Assets/Scripts','Assets/Editor','Assets/Resources','Packages','ProjectSettings']:
 files.extend(p for p in (source/folder).rglob('*') if p.is_file())
for path in files:
 rel=path.relative_to(source);dest=target/rel
 if rel.suffix in {'.log','.tmp'}:continue
 if path.name=='Settings.json' and 'com.unity.services.vivox' in path.parts:
  data=json.loads(path.read_text(encoding='utf-8'))
  for entry in data.get('m_Dictionary',{}).get('m_DictionaryValues',[]):
   if entry.get('key')=='tokenKey':assert not json.loads(entry['value']).get('m_Value'),'Do not ship a Vivox signing key'
 if dest.exists() and hashlib.sha256(path.read_bytes()).digest()==hashlib.sha256(dest.read_bytes()).digest():continue
 if dest.exists():
  copy=backup/rel;copy.parent.mkdir(parents=True,exist_ok=True)
  if not copy.exists():shutil.copy2(dest,copy)
 dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest);changed.append(str(rel))
report={'changed':len(changed),'paths':changed,'mapAssetsUntouched':True,'backup':str(backup)}
(root/'tools/release-071/isolated-sync.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('RELEASE_SOURCE_SYNCED',len(changed))
