from pathlib import Path
import subprocess, re, hashlib, zipfile, json, shutil, argparse

parser=argparse.ArgumentParser()
parser.add_argument('--version',default='0.4.0')
parser.add_argument('--version-code',type=int,default=6)
parser.add_argument('--log',default='horizonte-online-040-android.log')
parser.add_argument('--chat',action='store_true')
parser.add_argument('--updater',action='store_true')
args=parser.parse_args()
assert re.fullmatch(r'\d+\.\d+\.\d+',args.version)

root=Path(__file__).resolve().parents[1]
apk=root/'TestePersonagem/Builds/Android/TestePersonagem.apk'
old=apk.parent/'HorizonteBrasil-0.1.1.apk'
sdk=root/'tools/android/SDK/build-tools/36.0.0'
java=root/'tools/android/OpenJDK/bin/java.exe'
def run(args):
    return subprocess.run([str(a) for a in args],check=True,capture_output=True,text=True).stdout

build=(root/'tools'/args.log).read_text(encoding='utf-8',errors='replace')
assert 'ANDROID_APK_OK' in build and 'PROTOTYPE_BUILD_FAILED' not in build
signer=[java,'-jar',sdk/'lib/apksigner.jar','verify','--verbose','--print-certs']
previous=run(signer+[old]);current=run(signer+[apk])
digest=lambda output:re.search(r'Signer #1 certificate SHA-256 digest: ([0-9a-f]+)',output).group(1)
assert digest(previous)==digest(current),'Signing identity changed'
badging=run([sdk/'aapt.exe','dump','badging',apk])
assert f"name='com.adriano.testepersonagem' versionCode='{args.version_code}' versionName='{args.version}'" in badging
assert "sdkVersion:'26'" in badging and "native-code: 'arm64-v8a'" in badging
assert "application-label:'Horizonte'" in badging
assert 'android.permission.INTERNET' in badging
if args.updater:
    assert 'android.permission.REQUEST_INSTALL_PACKAGES' in badging
    manifest=run([sdk/'aapt.exe','dump','xmltree',apk,'AndroidManifest.xml'])
    assert 'com.horizonte.update.HbrpApkProvider' in manifest
    assert 'com.adriano.testepersonagem.updates' in manifest
run([sdk/'zipalign.exe','-c','-P','16','4',apk])
with zipfile.ZipFile(apk) as archive:
    assert archive.testzip() is None
    names=archive.namelist()
    assert 'lib/arm64-v8a/libil2cpp.so' in names
    metadata=archive.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    assert b'MultiplayerWorld' in metadata and b'MultiplayerPeer' in metadata and b'JumpPhase' in metadata
    if args.chat:assert b'InGameChat' in metadata and b'ChatMessage' in metadata and b'OpenKeyboard' in metadata
    if args.updater:
        assert b'GameUpdater' in metadata and b'GameVersion' in metadata and b'FileHash' in metadata
        dex=b''.join(archive.read(name) for name in names if re.fullmatch(r'classes\d*\.dex',name))
        assert b'Lcom/horizonte/update/HbrpApkProvider;' in dex
        version_meta=(root/'TestePersonagem/Assets/Resources/GameVersion.json.meta').read_text()
        version_guid=re.search(r'^guid: ([a-f0-9]+)',version_meta,re.MULTILINE).group(1)
        version_resources=archive.read('assets/bin/Data/'+version_guid)
        assert b'https://raw.githubusercontent.com/sopacrazy/horizonterp/main/releases/android.json' in version_resources
        assert args.version.encode() in version_resources
    config_meta=(root/'TestePersonagem/Assets/Resources/ApiConfig.json.meta').read_text()
    config_guid=re.search(r'^guid: ([a-f0-9]+)',config_meta,re.MULTILINE).group(1)
    resources=archive.read('assets/bin/Data/'+config_guid)
    assert b'https://70.38.101.147:8443' in resources
    assert b'3a0a59cc28b5b2a5aab7acae6546ed91f861b41086e8a1312da8b4550c149c42' in resources

release=apk.parent/f'HorizonteBrasil-{args.version}.apk'
shutil.copy2(apk,release)
report={'result':'ANDROID_RELEASE_VERIFIED','version':args.version,'versionCode':args.version_code,
    'package':'com.adriano.testepersonagem','label':'Horizonte','architecture':'arm64-v8a',
    'minimum_android':'8.0 (API 26)','internet_permission':True,'signature_verified':True,
    'same_signer_as_previous_apk':True,'zip_and_16k_alignment_valid':True,
    'multiplayer_and_jump_included':True,'existing_api_and_certificate_pin_included':True,
    'chat_included':args.chat,
    'updater_included':args.updater,
    'bytes':release.stat().st_size,'sha256':hashlib.sha256(release.read_bytes()).hexdigest(),
    'device_runtime_tested':False,'file':str(release)}
(root/('tools/android-'+args.version.replace('.','')+'-validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
