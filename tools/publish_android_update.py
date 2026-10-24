"""Publish a verified APK to GitHub Releases, then write the public update manifest.
Commit/push releases/android.json after this command succeeds. No credentials stored.
"""
import argparse,hashlib,json,pathlib,subprocess,sys

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--apk',type=pathlib.Path,required=True)
parser.add_argument('--notes-file',type=pathlib.Path,required=True)
args=parser.parse_args()
config=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text(encoding='utf-8'))
apk=args.apk.resolve();version=config['version'];code=config['versionCode']
assert apk.name==f'HorizonteBrasil-{version}.apk' and apk.is_file()
validation=json.loads((root/f"tools/android-{version.replace('.','')}-validation.json").read_text(encoding='utf-8'))
digest=hashlib.sha256(apk.read_bytes()).hexdigest()
assert validation['signature_verified'] and validation['same_signer_as_previous_apk']
assert validation['updater_included']
assert validation['versionCode']==code and validation['sha256']==digest
notes=args.notes_file.resolve()
assert notes.is_file() and notes.read_text(encoding='utf-8').strip(),'Release notes required'
repo='sopacrazy/horizonterp';tag='v'+version
def run(*args):return subprocess.run(args,check=True,text=True,capture_output=True).stdout
existing=subprocess.run(['gh','release','view',tag,'--repo',repo],capture_output=True)
if existing.returncode==0:raise SystemExit('Release already exists; never overwrite an APK already offered to clients.')
commit=run('git','rev-parse','HEAD').strip()
run('gh','release','create',tag,str(apk),'--repo',repo,'--target',commit,'--draft','--title','Horizonte Brasil '+version,
    '--notes-file',str(notes))
run('gh','release','edit',tag,'--repo',repo,'--draft=false','--latest')
release={'available':True,'version':version,'versionCode':code,'packageId':config['packageId'],
    'apkUrl':f'https://github.com/{repo}/releases/download/{tag}/{apk.name}','bytes':apk.stat().st_size,'sha256':digest}
(root/'releases/android.json').write_text(json.dumps(release,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'release':f'https://github.com/{repo}/releases/tag/{tag}','manifest':'releases/android.json','sha256':digest},indent=2))
