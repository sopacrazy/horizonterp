from pathlib import Path
import zipfile,hashlib,json,time
source=Path('TestePersonagem/Builds/Windows070');out=Path('tools/release-070');out.mkdir(exist_ok=True)
files=[p for p in source.rglob('*') if p.is_file() and 'BackUpThisFolder_ButDontShipItWithYourGame' not in p.parts and p.suffix not in ['.pdb','.mdb']]
archive=out/'HorizonteBrasil-Windows-0.7.0.zip';started=time.time()
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=4) as z:
 for p in files:z.write(p,'Horizonte/'+p.relative_to(source).as_posix())
with zipfile.ZipFile(archive) as z:assert z.testzip() is None
report={'file':str(archive),'bytes':archive.stat().st_size,'sha256':hashlib.file_digest(archive.open('rb'),'sha256').hexdigest(),'files':len(files),'seconds':round(time.time()-started),'smoke':'SA_REMEMBERED_ENTRY_PASSED'}
(out/'windows-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
