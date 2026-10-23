import urllib.request,pathlib,time
p=pathlib.Path(r'C:\SistemaAdr\gta\tools\UnitySetup64-6000.6.7f1.exe')
u='https://download.unity3d.com/download_unity/b22767f04f04/Windows64EditorInstaller/UnitySetup64-6000.6.7f1.exe'
last=[0]
def progress(n,s,total):
 now=time.time()
 if now-last[0]>20:
  print(f'Download: {n*s/1048576:.0f}/{total/1048576:.0f} MiB',flush=True);last[0]=now
urllib.request.urlretrieve(u,p,progress)
print('Download complete',p.stat().st_size,flush=True)
