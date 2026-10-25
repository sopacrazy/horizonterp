from pathlib import Path
import subprocess, re, hashlib, zipfile, json, shutil, argparse

parser=argparse.ArgumentParser()
parser.add_argument('--version',default='0.4.0')
parser.add_argument('--version-code',type=int,default=6)
parser.add_argument('--log',default='horizonte-online-040-android.log')
parser.add_argument('--chat',action='store_true')
parser.add_argument('--updater',action='store_true')
parser.add_argument('--stairs',action='store_true')
parser.add_argument('--walk',action='store_true')
parser.add_argument('--network',action='store_true')
parser.add_argument('--hud2',action='store_true')
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
assert f'GAME_VERSION_APPLIED version={args.version} code={args.version_code}' in build,'Build must use the shared version configuration'
if args.hud2:assert 'HUD2_ATLAS_VERIFIED' in build,'HUB2 atlas must be prepared for the build'
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
    if args.network:assert b'RemoteAvatarAnimation' in metadata and b'RemoteMotionBuffer' in metadata and b'updatedAt' in metadata
    if args.hud2:
        assert b'Hud2Atlas' in metadata and b'h2_sprint' in metadata
        layout_meta=(root/'TestePersonagem/Assets/Resources/Hud2AtlasLayout.json.meta').read_text()
        layout_guid=re.search(r'^guid: ([a-f0-9]+)',layout_meta,re.MULTILINE).group(1)
        # Unity stores TextAssets in a serialized object, not as a raw JSON file.
        layout_asset=archive.read('assets/bin/Data/'+layout_guid)
        start=re.search(rb'\{\s*"regions"\s*:',layout_asset)
        assert start,'HUB2 layout TextAsset payload missing'
        packaged,_=json.JSONDecoder().raw_decode(layout_asset[start.start():].decode('utf-8',errors='replace'))
        expected=json.loads((root/'TestePersonagem/Assets/Resources/Hud2AtlasLayout.json').read_text())
        assert packaged==expected,'HUB2 sprite regions differ from the verified layout'
    if args.updater:
        assert b'GameUpdater' in metadata and b'GameVersion' in metadata and b'FileHash' in metadata
        dex=b''.join(archive.read(name) for name in names if re.fullmatch(r'classes\d*\.dex',name))
        assert b'Lcom/horizonte/update/HbrpApkProvider;' in dex
        version_meta=(root/'TestePersonagem/Assets/Resources/GameVersion.json.meta').read_text()
        version_guid=re.search(r'^guid: ([a-f0-9]+)',version_meta,re.MULTILINE).group(1)
        version_resources=archive.read('assets/bin/Data/'+version_guid)
        assert b'https://raw.githubusercontent.com/sopacrazy/horizonterp/main/releases/android.json' in version_resources
        assert args.version.encode() in version_resources
    if args.stairs:
        scenes=[archive.read(name) for name in names if re.fullmatch(r'assets/bin/Data/level\d+(?:\.split\d+)?',name)]
        assert sum(data.count(b'Colisao suave da escada') for data in scenes)==2,'Both stair ramps must be in the Android scene'
        meshes=[archive.read(name) for name in names if re.fullmatch(r'assets/bin/Data/sharedassets\d+\.assets(?:\.split\d+)?',name)]
        assert any(b'Smooth residential stair ramp' in data for data in meshes),'Smooth ramp collision mesh missing'
    if args.walk:
        animation_assets=[archive.read(name) for name in names if re.fullmatch(r'assets/bin/Data/(?:sharedassets\d+|resources)\.assets(?:\.split\d+)?',name)]
        assert any(b'Andar_WalkingOriginal' in data for data in animation_assets),'Authored walking animation missing'
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
    'smooth_stair_collisions_included':args.stairs,
    'authored_walking_included':args.walk,
    'remote_motion_and_animation_fix_included':args.network,
    'transparent_hub2_layout_included':args.hud2,
    'bytes':release.stat().st_size,'sha256':hashlib.sha256(release.read_bytes()).hexdigest(),
    'device_runtime_tested':False,'file':str(release)}
(root/('tools/android-'+args.version.replace('.','')+'-validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
