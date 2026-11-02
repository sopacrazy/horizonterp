"""Check anonymous release downloads before offering them in the updater manifest."""
from pathlib import Path
import argparse,hashlib,json,urllib.request

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--version',required=True)
parser.add_argument('--manifest',action='store_true')
parser.add_argument('--manifest-path',choices=['releases/android.json','releases/android-next.json'],
                    help='Updater manifest to verify; defaults to the current game configuration')
parser.add_argument('--android-only',action='store_true',help='Verify a release without a Windows build')
args=parser.parse_args()
version=args.version
headers={'User-Agent':'Horizonte-public-release-check','Accept':'application/vnd.github+json'}
def read_json(url):
    with urllib.request.urlopen(urllib.request.Request(url,headers=headers),timeout=60) as response:
        return json.load(response)
release=read_json(f'https://api.github.com/repos/sopacrazy/horizonterp/releases/tags/v{version}')
assert not release['draft']
expected=json.loads((root/f"tools/android-{version.replace('.','')}-validation.json").read_text(encoding='utf-8'))
checks=[]
assets=[(f'HorizonteBrasil-{version}.apk',expected)]
if not args.android_only:
    windows=json.loads((root/f"tools/release-{version.replace('.','')}/windows-validation.json").read_text(encoding='utf-8'))
    assets.append((f'HorizonteBrasil-Windows-{version}.zip',windows))
for name,validation in assets:
    asset=next(item for item in release['assets'] if item['name']==name)
    assert asset['size']==validation['bytes']
    if asset.get('digest'):assert asset['digest']=='sha256:'+validation['sha256']
    digest=hashlib.sha256();count=0
    request=urllib.request.Request(asset['browser_download_url'],headers={'User-Agent':'Horizonte-public-release-check'})
    with urllib.request.urlopen(request,timeout=900) as response:
        while chunk:=response.read(1024*1024):digest.update(chunk);count+=len(chunk)
    assert count==validation['bytes'] and digest.hexdigest()==validation['sha256']
    check={'name':name,'bytes':count,'sha256':digest.hexdigest(),'publicDownloadVerified':True}
    checks.append(check);print(json.dumps(check),flush=True)
if args.manifest:
    config=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text(encoding='utf-8'))
    manifest_path=args.manifest_path or ('releases/android-next.json' if config['updateManifestUrl'].endswith('/android-next.json') else 'releases/android.json')
    public=read_json('https://raw.githubusercontent.com/sopacrazy/horizonterp/main/'+manifest_path)
    assert public==json.loads((root/manifest_path).read_text(encoding='utf-8'))
    assert public['version']==version and public['versionCode']==expected['versionCode']
    assert public['sha256']==expected['sha256'] and public['bytes']==expected['bytes']
report={'version':version,'release':release['html_url'],'assets':checks,'manifestVerified':args.manifest}
report_path=root/f"tools/release-{version.replace('.','')}/public-validation.json"
report_path.parent.mkdir(parents=True,exist_ok=True)
report_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PUBLIC_RELEASE_VERIFIED',flush=True)
