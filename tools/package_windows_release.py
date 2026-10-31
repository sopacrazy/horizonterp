from pathlib import Path
import zipfile,hashlib,json,argparse
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--source',type=Path,required=True);args=parser.parse_args()
version=json.loads((root/'TestePersonagem/Assets/Resources/GameVersion.json').read_text(encoding='utf-8'))['version']
source=args.source.resolve();folder=root/('tools/release-'+version.replace('.',''));folder.mkdir(exist_ok=True)
smoke=(folder/'windows-smoke.log').read_text(encoding='utf-8',errors='replace');assert 'SA_REMEMBERED_ENTRY_PASSED' in smoke
files=[p for p in source.rglob('*') if p.is_file() and not any('ButDontShipItWithYourGame' in part for part in p.parts) and p.suffix not in ['.pdb','.mdb']]
archive=folder/f'HorizonteBrasil-Windows-{version}.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=4) as output:
 for p in files:output.write(p,'Horizonte/'+p.relative_to(source).as_posix())
with zipfile.ZipFile(archive) as output:assert output.testzip() is None
with archive.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
report={'file':str(archive),'bytes':archive.stat().st_size,'sha256':digest,'files':len(files),'smoke':'SA_REMEMBERED_ENTRY_PASSED'}
(folder/'windows-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
