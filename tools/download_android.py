import urllib.request,pathlib,time,json
root=pathlib.Path(__file__).parent
url='https://download.unity3d.com/download_unity/b22767f04f04/TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-6000.6.7f1.exe'
last=[0]
def progress(n,s,total):
 if time.time()-last[0]>20:
  print(f'Android: {n*s/1048576:.0f}/{total/1048576:.0f} MiB',flush=True);last[0]=time.time()
urllib.request.urlretrieve(url,root/'UnitySetup-Android.exe',progress)
print('DOWNLOAD_OK',flush=True)
u='https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.6.7f1&limit=1'
with urllib.request.urlopen(u) as f: data=json.load(f)
(root/'unity-release-android.json').write_text(json.dumps(data,indent=2))
