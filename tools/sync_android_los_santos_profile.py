"""Sync generated Android profile/import settings back with recoverable backups."""
from pathlib import Path
import datetime,json,shutil

root=Path(__file__).resolve().parents[1]
source=root/'TestePersonagemAndroidLS';target=root/'TestePersonagem'
backup=root/'tools/android-los-santos'/('sync-backup-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
changes=[]
def sync(relative):
    relative=Path(relative)
    if relative.is_absolute() or '..' in relative.parts:raise ValueError('Invalid relative path')
    src=source/relative;dst=target/relative
    if not src.is_file():return
    if dst.is_file() and dst.read_bytes()==src.read_bytes():return
    if dst.is_file():
        saved=backup/relative;saved.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dst,saved)
    dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst);changes.append(relative.as_posix())
profile=source/'Assets/Scenes/AndroidLosSantos'
assert (profile/'Entrada.unity').is_file() and (profile/'TestePersonagem.unity').is_file()
for path in profile.rglob('*'):
    if path.is_file():sync(path.relative_to(source))
for relative in ['Assets/Scenes/AndroidLosSantos.meta','Assets/Scenes/LosSantosAndroid.enabled',
                 'Assets/Scenes/LosSantosAndroid.enabled.meta','Assets/Resources/GameVersion.json',
                 'ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset']:
    sync(relative)
for relative in (root/'tools/android-map-optimization/dependencies.txt').read_text(encoding='utf-8-sig').splitlines():
    relative=relative.strip()
    if not relative.startswith('Assets/'):continue
    meta=relative+'.meta'
    if relative.startswith('Assets/GTASanAndreasModFull/') or not (target/meta).exists():sync(meta)
report={'changedFiles':len(changes),'backup':str(backup),'files':changes,'fullMapSceneUntouched':True}
folder=root/'tools/android-los-santos';folder.mkdir(parents=True,exist_ok=True)
(folder/'profile-sync.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='files'}))
