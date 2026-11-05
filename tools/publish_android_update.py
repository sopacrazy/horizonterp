"""Publish a verified APK to GitHub Releases, then write the public update manifest.
Commit/push releases/android.json after this command succeeds. No credentials stored.
"""
import argparse,hashlib,json,pathlib,shutil,subprocess

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--apk',type=pathlib.Path,required=True)
parser.add_argument('--notes-file',type=pathlib.Path,required=True)
parser.add_argument('--windows',type=pathlib.Path,help='Validated Windows ZIP to include in the same release')
parser.add_argument('--gh',help='Path to the official GitHub CLI executable')
args=parser.parse_args()
config=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text(encoding='utf-8'))
apk=args.apk.resolve();version=config['version'];code=config['versionCode']
assert apk.name==f'HorizonteBrasil-{version}.apk' and apk.is_file()
validation=json.loads((root/f"tools/android-{version.replace('.','')}-validation.json").read_text(encoding='utf-8'))
digest=hashlib.sha256(apk.read_bytes()).hexdigest()
assert validation['signature_verified'] and validation['same_signer_as_previous_apk']
assert validation['updater_included']
assert validation['versionCode']==code and validation['sha256']==digest
max_apk_mib=300 if code<=21 else 600
assert apk.stat().st_size<=max_apk_mib*1024*1024,'Installed updater cannot accept this APK size'
windows=args.windows.resolve() if args.windows else None
if windows:
    windows_validation=json.loads((root/f"tools/release-{version.replace('.','')}/windows-validation.json").read_text(encoding='utf-8'))
    assert windows.name==f'HorizonteBrasil-Windows-{version}.zip' and windows.is_file()
    assert windows_validation['smoke']=='SA_REMEMBERED_ENTRY_PASSED'
    with windows.open('rb') as source:
        windows_digest=hashlib.file_digest(source,'sha256').hexdigest()
    assert windows_validation['sha256']==windows_digest and windows_validation['bytes']==windows.stat().st_size
notes=args.notes_file.resolve()
assert notes.is_file() and notes.read_text(encoding='utf-8').strip(),'Release notes required'
repo='sopacrazy/horizonterp';tag='v'+version
def run(*args):return subprocess.run(args,check=True,text=True,capture_output=True).stdout
commit=run('git','rev-parse','HEAD').strip()
gh=args.gh or shutil.which('gh')
if not gh or subprocess.run([gh,'auth','status','--hostname','github.com'],capture_output=True).returncode!=0:
    raise SystemExit('Sign in with the official GitHub CLI (gh auth login) before publishing.')
existing=subprocess.run([gh,'release','view',tag,'--repo',repo],capture_output=True)
if existing.returncode==0:raise SystemExit('Release already exists; never overwrite an APK already offered to clients.')
assets=[str(apk)]+([str(windows)] if windows else [])
run(gh,'release','create',tag,*assets,'--repo',repo,'--target',commit,'--draft','--title','Horizonte Brasil '+version,
    '--notes-file',str(notes))
run(gh,'release','edit',tag,'--repo',repo,'--draft=false','--latest')
release={'available':True,'version':version,'versionCode':code,'packageId':config['packageId'],
    'apkUrl':f'https://github.com/{repo}/releases/download/{tag}/{apk.name}','bytes':apk.stat().st_size,'sha256':digest}
manifest_paths=['releases/android.json','releases/android-next.json'] if code==21 else ['releases/android-next.json']
for manifest in manifest_paths:
    (root/manifest).write_text(json.dumps(release,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'release':f'https://github.com/{repo}/releases/tag/{tag}','manifests':manifest_paths,'sha256':digest},indent=2))
