import urllib.request, pathlib, zipfile
root=pathlib.Path(r'C:\SistemaAdr\gta\tools')
for name,url in [('DragonFF.zip','https://github.com/Parik27/DragonFF/archive/refs/heads/master.zip'),('blender.zip','https://download.blender.org/release/Blender4.2/blender-4.2.0-windows-x64.zip')]:
 p=root/name
 if not p.exists():
  print('Downloading',name,flush=True); urllib.request.urlretrieve(url,p)
 print('Extracting',name,flush=True)
 with zipfile.ZipFile(p) as z:z.extractall(root)
print('Ready',flush=True)
