"""Publish a verified APK to GitHub Releases, then write the public update manifest.
Commit/push releases/android.json after this command succeeds. No credentials stored.
"""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,urllib.error,urllib.parse,urllib.request

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--apk',type=pathlib.Path,required=True)
parser.add_argument('--notes-file',type=pathlib.Path,required=True)
parser.add_argument('--windows',type=pathlib.Path,help='Validated Windows ZIP to include in the same release')
args=parser.parse_args()
config=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text(encoding='utf-8'))
apk=args.apk.resolve();version=config['version'];code=config['versionCode']
assert apk.name==f'HorizonteBrasil-{version}.apk' and apk.is_file()
validation=json.loads((root/f"tools/android-{version.replace('.','')}-validation.json").read_text(encoding='utf-8'))
digest=hashlib.sha256(apk.read_bytes()).hexdigest()
assert validation['signature_verified'] and validation['same_signer_as_previous_apk']
assert validation['updater_included']
assert validation['versionCode']==code and validation['sha256']==digest
assert apk.stat().st_size<=300*1024*1024,'Installed updater cannot accept more than 300 MiB'
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
if shutil.which('gh'):
    existing=subprocess.run(['gh','release','view',tag,'--repo',repo],capture_output=True)
    if existing.returncode==0:raise SystemExit('Release already exists; never overwrite an APK already offered to clients.')
    assets=[str(apk)]+([str(windows)] if windows else [])
    run('gh','release','create',tag,*assets,'--repo',repo,'--target',commit,'--draft','--title','Horizonte Brasil '+version,
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
    for asset,content_type in [(apk,'application/vnd.android.package-archive')]+([(windows,'application/zip')] if windows else []):
        upload=release_draft['upload_url'].split('{',1)[0]+'?'+urllib.parse.urlencode({'name':asset.name})
        with asset.open('rb') as source:
            request=urllib.request.Request(upload,data=source,headers={**headers,'Content-Type':content_type,'Content-Length':str(asset.stat().st_size)},method='POST')
            with urllib.request.urlopen(request,timeout=1800) as response:
                uploaded=json.load(response)
        assert uploaded['size']==asset.stat().st_size
        print(json.dumps({'uploaded':asset.name,'bytes':uploaded['size']}),flush=True)
    api('PATCH',base+'/'+str(release_draft['id']),json.dumps({'draft':False,'make_latest':'true'}).encode())
release={'available':True,'version':version,'versionCode':code,'packageId':config['packageId'],
    'apkUrl':f'https://github.com/{repo}/releases/download/{tag}/{apk.name}','bytes':apk.stat().st_size,'sha256':digest}
(root/'releases/android.json').write_text(json.dumps(release,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'release':f'https://github.com/{repo}/releases/tag/{tag}','manifest':'releases/android.json','sha256':digest},indent=2))
