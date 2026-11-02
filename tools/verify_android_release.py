from pathlib import Path
import subprocess, re, hashlib, zipfile, json, shutil, argparse, sys

parser=argparse.ArgumentParser()
parser.add_argument('--version',default='0.4.0')
parser.add_argument('--version-code',type=int,default=6)
parser.add_argument('--log',default='horizonte-online-040-android.log')
parser.add_argument('--apk',type=Path,default=None)
parser.add_argument('--chat',action='store_true')
parser.add_argument('--updater',action='store_true')
parser.add_argument('--stairs',action='store_true')
parser.add_argument('--walk',action='store_true')
parser.add_argument('--network',action='store_true')
parser.add_argument('--hud2',action='store_true')
parser.add_argument('--admin-chat',action='store_true')
parser.add_argument('--admin-panel',action='store_true')
parser.add_argument('--remembered-access',action='store_true')
parser.add_argument('--official-city',action='store_true')
parser.add_argument('--los-santos',action='store_true')
parser.add_argument('--current-features',action='store_true')
parser.add_argument('--realtime',action='store_true')
parser.add_argument('--female-character',action='store_true')
parser.add_argument('--rifle',action='store_true')
parser.add_argument('--named-access',action='store_true')
args=parser.parse_args()
assert re.fullmatch(r'\d+\.\d+\.\d+',args.version)
verify_female=args.female_character or tuple(map(int,args.version.split('.'))) >= (0,7,4)
verify_rifle_feature=args.rifle or tuple(map(int,args.version.split('.'))) >= (0,7,4)
verify_named_feature=args.named_access or tuple(map(int,args.version.split('.'))) >= (0,7,4)
female_character=None
rifle_feature=named_access=None

root=Path(__file__).resolve().parents[1]
apk=args.apk.resolve() if args.apk else root/'TestePersonagem/Builds/Android/TestePersonagem.apk'
old=root/'TestePersonagem/Builds/Android/HorizonteBrasil-0.5.9.apk'
android_tools=root/'tools/android'
if not (android_tools/'SDK').is_dir():
    android_tools=Path(r'C:\Program Files\Unity\Hub\Editor\6000.6.7f1-x86_64\Editor\Data\PlaybackEngines\AndroidPlayer')
sdk=android_tools/'SDK/build-tools/36.0.0'
java=android_tools/'OpenJDK/bin/java.exe'
def run(args):
    return subprocess.run([str(a) for a in args],check=True,capture_output=True,text=True).stdout

build=(root/'tools'/args.log).read_text(encoding='utf-8',errors='replace')
assert 'ANDROID_APK_OK' in build and 'PROTOTYPE_BUILD_FAILED' not in build
assert f'GAME_VERSION_APPLIED version={args.version} code={args.version_code}' in build,'Build must use the shared version configuration'
if args.hud2:assert 'HUD2_ATLAS_VERIFIED' in build,'HUB2 atlas must be prepared for the build'
if verify_female:
    assert 'FEMALE_DEPENDENCIES_VERIFIED' in build and 'FEMALE_CHARACTER_VERIFIED' in build,'Build must validate the feminine dependency graph and imported Humanoid avatar'
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
    max_apk_mib=300 if args.version_code<=21 else 600
    assert apk.stat().st_size<=max_apk_mib*1024*1024,'Installed Android updater cannot accept this APK size'
    assert 'android.permission.REQUEST_INSTALL_PACKAGES' in badging
    manifest=run([sdk/'aapt.exe','dump','xmltree',apk,'AndroidManifest.xml'])
    assert 'com.horizonte.update.HbrpApkProvider' in manifest
    assert 'com.adriano.testepersonagem.updates' in manifest
run([sdk/'zipalign.exe','-c','-P','16','4',apk])
with zipfile.ZipFile(apk) as archive:
    assert archive.testzip() is None
    # LZ4HC builds store serialized scenes/resources inside a UnityFS bundle.
    # Inspect the actual decompressed files with the same checks as loose builds.
    if 'assets/bin/Data/data.unity3d' in archive.namelist():
        sys.path.insert(0,str(root/'tools/apk-inspection-deps'))
        import UnityPy
        environment=UnityPy.load(archive.read('assets/bin/Data/data.unity3d'))
        bundle=next(iter(environment.files.values()))
        packed={'assets/bin/Data/'+name: value for name,value in bundle.files.items()}
        text_assets={}
        for obj in environment.objects:
            if obj.type.name=='TextAsset':
                asset=obj.read()
                text_assets[asset.m_Name]=asset.m_Script.encode('utf-8',errors='surrogateescape') if isinstance(asset.m_Script,str) else asset.m_Script
        text_by_guid={}
        for asset_name in ['Hud2AtlasLayout','GameVersion','ApiConfig']:
            meta=(root/f'TestePersonagem/Assets/Resources/{asset_name}.json.meta').read_text()
            guid=re.search(r'^guid: ([a-f0-9]+)',meta,re.MULTILINE).group(1)
            text_by_guid['assets/bin/Data/'+guid]=text_assets[asset_name]
        class PackedArchive:
            def namelist(self):return list(dict.fromkeys(archive_names+list(packed)))
            def read(self,name):
                if name in text_by_guid:return text_by_guid[name]
                if name not in packed:return original_read(name)
                value=packed[name]
                return value.reader.bytes if hasattr(value,'reader') else value.bytes
        archive_names=archive.namelist()
        original_read=archive.read
        archive=PackedArchive()
    if verify_female:
        from verify_apk_character import verify_female_character
        if 'environment' not in globals():
            sys.path.insert(0,str(root/'tools/apk-inspection-deps'))
            import UnityPy
            environment=UnityPy.load({name:archive.read(name) for name in archive.namelist()
                if re.fullmatch(r'assets/bin/Data/(?:resources|sharedassets\d+)\.assets',name)})
        female_character=verify_female_character(environment)
    names=archive.namelist()
    assert 'lib/arm64-v8a/libil2cpp.so' in names
    metadata=archive.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    if verify_rifle_feature:
        from verify_apk_character import verify_rifle
        assert 'environment' in globals(), 'Rifle artifact verification requires the deserialized Unity assets'
        calibration=json.loads((root/'TestePersonagem/Assets/Resources/Weapons/Rifle/SocketCalibration.json').read_text(encoding='utf-8'))
        rifle_feature=verify_rifle(environment,metadata,calibration)
    if verify_named_feature:
        from verify_apk_character import verify_named_access
        named_access=verify_named_access(metadata)
    if args.realtime:
        for feature in [b'RealtimeMovement',b'UtpMovementClient',b'UnreliableSequencedPipelineStage',b'VivoxVoice',b'IVivoxTokenProvider',b'VoicePcmBuffer',b'MinimapHeading']:
            assert feature in metadata,'Missing realtime integration: '+str(feature)
        assert any('vivox' in n.lower() and n.startswith('lib/arm64-v8a/') for n in names),'Native Vivox ARM64 library missing'
    assert b'MultiplayerWorld' in metadata and b'MultiplayerPeer' in metadata and b'JumpPhase' in metadata
    if args.chat:assert b'InGameChat' in metadata and b'ChatMessage' in metadata and b'OpenKeyboard' in metadata
    if args.network:assert b'RemoteAvatarAnimation' in metadata and b'RemoteMotionBuffer' in metadata and b'updatedAt' in metadata
    if args.admin_chat:assert b'isAdmin' in metadata and b'chat-author-admin' in metadata and b'action-admin' in metadata
    if args.admin_panel:
        assert b'AdminPlayerPanel' in metadata and b'AdminRequest' in metadata
        assert b'/v1/admin/players' in metadata
        assert (b'AdminHelpPanel' in metadata if args.current_features else b'/ahelp' in metadata),'Administrative help must be packaged'
        assert b'PlayerDisplayName' in metadata and b'/v1/admin/players' in metadata
    if args.remembered_access:
        assert b'RestoreRemembered' in metadata and b'remembered-play' in metadata and b'SessionVault' in metadata
        vault_dex=b''.join(archive.read(name) for name in names if re.fullmatch(r'classes\d*\.dex',name))
        assert b'Lcom/horizonte/update/HbrpSessionVault;' in vault_dex
        assert b'AndroidKeyStore' in vault_dex and b'AES/GCM/NoPadding' in vault_dex
    if args.official_city:
        assert b'NeighborhoodMap' in metadata
        folder='GTASanAndreasModFullSectors' if (root/'TestePersonagem/Assets/Scenes/SanAndreasIntegration.enabled').exists() else 'CitySectors'
        if folder=='GTASanAndreasModFullSectors':
            assert b'GtaSaSectorStreaming' in metadata and b'IntegratedMapEntry' in metadata
            assert b'CanApplyPlayerState' in metadata
        else:
            assert b'GtaCityEnvironment' in metadata and b'CitySectorStreaming' in metadata
        expected_sectors=list((root/'TestePersonagem/Assets/Scenes'/folder).glob('*.unity'))
        if args.los_santos:
            expected_sectors=[root/'TestePersonagem'/line.strip() for line in
                (root/'tools/android-los-santos/sectors.txt').read_text(encoding='utf-8-sig').splitlines() if line.strip()]
            assert 20<len(expected_sectors)<300
            assert b'MapRegionBoundary' in metadata and b'ContainsXZ' in metadata
        assert len(expected_sectors)>1,'Official city must include streaming sector sources'
        levels={re.match(r'assets/bin/Data/level(\d+)',name).group(1) for name in names if re.fullmatch(r'assets/bin/Data/level\d+(?:\.split\d+)?',name)}
        assert len(levels)==len(expected_sectors)+2,'APK must package boot, official city and every streaming sector'
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
        expected_manifest=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text())['updateManifestUrl']
        assert expected_manifest.encode() in version_resources
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
    expected_api=json.loads((root/'TestePersonagem/Assets/Resources/ApiConfig.json').read_text())
    assert expected_api['baseUrl'].encode() in resources
    assert expected_api['certificateSha256'].encode() in resources
    if args.current_features:
        for feature in [b'VoiceChat',b'PoliceRadio',b'CuffedAvatarPose',b'PoliceCarWorld',b'WantedListPanel',b'SelectCityMapDestination',b'RequestCityMapTeleport',b'PreviewMapDestination',b'CancelCityMapSelection',b'BeginAdminMapTeleport',b'AdminMapArrival',b'IsPlayerCommand']:
            assert feature in metadata,'Missing current feature: '+str(feature)
        assert 'android.permission.RECORD_AUDIO' in badging
        assert b'city-teleport-confirm' in metadata
        if 'environment' in globals():
            atlas=next(obj.read() for obj in environment.objects if obj.type.name=='Texture2D' and obj.read().m_Name=='LosSantos')
            assert atlas.m_Width==1498 and atlas.m_Height==1050

release=apk.parent/f'HorizonteBrasil-{args.version}.apk'
if apk.resolve()!=release.resolve():shutil.copy2(apk,release)
report={'result':'ANDROID_RELEASE_VERIFIED','version':args.version,'versionCode':args.version_code,
    'package':'com.adriano.testepersonagem','label':'Horizonte','architecture':'arm64-v8a',
    'minimum_android':'8.0 (API 26)','internet_permission':True,'signature_verified':True,
    'same_signer_as_previous_apk':True,'zip_and_16k_alignment_valid':True,
    'multiplayer_and_jump_included':True,'existing_api_and_certificate_pin_included':True,'api_base_url':expected_api['baseUrl'],'current_features_verified':args.current_features,
    'chat_included':args.chat,
    'updater_included':args.updater,
    'smooth_stair_collisions_included':args.stairs,
    'authored_walking_included':args.walk,
    'remote_motion_and_animation_fix_included':args.network,
    'transparent_hub2_layout_included':args.hud2,
    'admin_identity_and_placeholder_button_included':args.admin_chat,
    'admin_player_panel_and_command_catalog_included':args.admin_panel,
    'protected_remembered_access_included':args.remembered_access,
    'official_city_streaming_included':args.official_city,
    'los_santos_only':args.los_santos,
    'udp_dtls_and_vivox_included':args.realtime,
    'female_character_verified':verify_female,'female_character':female_character,
    'rifle_and_low_ready_verified':verify_rifle_feature,'rifle':rifle_feature,
    'named_beta_access_verified':verify_named_feature,'named_beta_access':named_access,
    'bytes':release.stat().st_size,'sha256':hashlib.sha256(release.read_bytes()).hexdigest(),
    'device_runtime_tested':False,'file':str(release)}
(root/('tools/android-'+args.version.replace('.','')+'-validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
