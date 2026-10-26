"""Publish a verified APK to GitHub Releases, then write the public update manifest.
Commit/push releases/android.json after this command succeeds. No credentials stored.
"""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,urllib.error,urllib.parse,urllib.request

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
commit=run('git','rev-parse','HEAD').strip()
if shutil.which('gh'):
    existing=subprocess.run(['gh','release','view',tag,'--repo',repo],capture_output=True)
    if existing.returncode==0:raise SystemExit('Release already exists; never overwrite an APK already offered to clients.')
    run('gh','release','create',tag,str(apk),'--repo',repo,'--target',commit,'--draft','--title','Horizonte Brasil '+version,
        '--notes-file',str(notes))
    run('gh','release','edit',tag,'--repo',repo,'--draft=false','--latest')
else:
    credential=subprocess.run(
        ['git','credential','fill'],input='protocol=https\nhost=github.com\n\n',text=True,capture_output=True,check=True).stdout
    token=next((line[9:] for line in credential.splitlines() if line.startswith('password=')),None)
    if not token:raise SystemExit('GitHub credential unavailable')
    headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json',
        'X-GitHub-Api-Version':'2022-11-28','User-Agent':'Horizonte-release'}
    def api(method,url,body=None,content_type='application/json'):
        request=urllib.request.Request(url,data=body,headers={**headers,'Content-Type':content_type},method=method)
        with urllib.request.urlopen(request,timeout=600) as response:
            data=response.read()
            return json.loads(data) if data else None
    base=f'https://api.github.com/repos/{repo}/releases'
    try:
        api('GET',base+'/tags/'+tag)
    except urllib.error.HTTPError as error:
        if error.code!=404:raise
    else:raise SystemExit('Release already exists; never overwrite an APK already offered to clients.')
    payload={'tag_name':tag,'target_commitish':commit,'name':'Horizonte Brasil '+version,
        'body':notes.read_text(encoding='utf-8'),'draft':True,'prerelease':False}
    release_draft=api('POST',base,json.dumps(payload).encode())
    upload=release_draft['upload_url'].split('{',1)[0]+'?'+urllib.parse.urlencode({'name':apk.name})
    api('POST',upload,apk.read_bytes(),'application/vnd.android.package-archive')
    api('PATCH',base+'/'+str(release_draft['id']),json.dumps({'draft':False,'make_latest':'true'}).encode())
release={'available':True,'version':version,'versionCode':code,'packageId':config['packageId'],
    'apkUrl':f'https://github.com/{repo}/releases/download/{tag}/{apk.name}','bytes':apk.stat().st_size,'sha256':digest}
(root/'releases/android.json').write_text(json.dumps(release,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'release':f'https://github.com/{repo}/releases/tag/{tag}','manifest':'releases/android.json','sha256':digest},indent=2))
