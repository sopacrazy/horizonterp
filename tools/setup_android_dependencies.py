import json,pathlib,urllib.request,zipfile,concurrent.futures,shutil
root=pathlib.Path(__file__).resolve().parent
data=json.loads((root/'windows-modules.json').read_text(encoding='utf-8-sig'))
module=next(m for m in data['modules'] if m['id']=='android')
prefix='{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer/'
base=root/'android'
def install(m):
 dest=base/m['destination'].removeprefix(prefix)
 dest.mkdir(parents=True,exist_ok=True)
 archive=root/(m['id'].replace('+','_')+'.zip')
 if not archive.exists():
  print('DOWNLOAD '+m['name'],flush=True);urllib.request.urlretrieve(m['url'],archive)
 print('EXTRACT '+m['name'],flush=True)
 with zipfile.ZipFile(archive) as z:z.extractall(dest)
 if m.get('extractedPathRename'):
  a=base/m['extractedPathRename']['from'].removeprefix(prefix)
  b=base/m['extractedPathRename']['to'].removeprefix(prefix)
  if a.exists():
   if b==a.parent:
    for child in a.iterdir():shutil.move(str(child),str(b/child.name))
    a.rmdir()
   else:a.rename(b)
 print('READY '+m['id'],flush=True)
mods=[module['subModules'][0]]+module['subModules'][1]['subModules']
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
 for result in pool.map(install,mods):pass
print('ANDROID_DEPENDENCIES_OK',flush=True)
