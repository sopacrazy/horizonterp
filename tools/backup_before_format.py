"""Create a recoverable source snapshot; never include credentials in public archives."""
import argparse,collections,hashlib,json,os,pathlib,shutil,time,zipfile

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'tools/format-backup-20261104'
SKIP={'.git','Library','Temp','Logs','node_modules','__pycache__','.vs','.idea','obj','Builds','build','.utmp','.pytest_cache'}
PRIVATE_EXT={'.env','.pgpass','.key','.pfx','.p12','.keystore','.jks','.dump'}
def private(p):
    s=p.as_posix().lower();n=p.name.lower()
    return p.suffix.lower() in PRIVATE_EXT or '.env.' in n or n=='.env' or any(x in s.split('/') for x in ['credentials','backups']) or 'fixture' in n and p.suffix=='.json' or n=='assinatura-horizonte.zip'
def walk(folder):
    for base,dirs,files in os.walk(folder):
        dirs[:]=sorted(d for d in dirs if d not in SKIP and not d.startswith('sync-backup'))
        for name in sorted(files):yield pathlib.Path(base)/name
def digest(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(2**20),b''):h.update(b)
    return h.hexdigest()
def source_groups():
    groups={}
    for name in ['TestePersonagem','TestePersonagemAndroidLS','FavelaRioTeste','GTA-V-Mapa-Editavel','mapa','personagem','personagem_unity','Backend','releases','reports','sites']:
        folder=ROOT/name
        if folder.exists():groups[name]=[p for p in walk(folder) if not private(p) and p.suffix.lower() not in {'.log','.apk','.aab','.blend1'}]
    tools=[]
    for p in (ROOT/'tools').iterdir():
        if p.is_file() and p.suffix.lower() in {'.py','.ps1','.sh','.cs','.md','.txt','.json','.service','.timer'} and not private(p):
            # Runtime reports and fixture outputs can contain sessions; only scripts/config source.
            if p.suffix.lower()=='.json':continue
            tools.append(p)
    for name in ['DragonFF-master','wuzimu-review']:
        folder=ROOT/'tools'/name
        if folder.exists():
            tools.extend(p for p in walk(folder) if not private(p) and p.suffix.lower() in {'.py','.blend','.fbx','.png','.md','.txt','.meta'})
    groups['tools-source']=tools
    groups['root']=[p for p in ROOT.iterdir() if p.is_file() and p.suffix.lower() in {'.bat','.md'} or p.is_file() and p.name in {'.gitignore','.gitattributes'}]
    return groups
def public_backup():
    OUT.mkdir(exist_ok=True)
    groups=source_groups();report={'created':'2026-11-04','archives':[]}
    for name,files in groups.items():
        archive=OUT/(name+'.zip')
        if archive.exists():
            with zipfile.ZipFile(archive) as z:
                if len(z.infolist())==len(files) and z.testzip() is None:
                    print('REUSE',name,len(files),flush=True)
                    report['archives'].append({'file':archive.name,'files':len(files),'bytes':archive.stat().st_size,'sha256':digest(archive)});continue
        print('ARCHIVING',name,len(files),sum(p.stat().st_size for p in files),flush=True)
        with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as z:
            for p in files:z.write(p,p.relative_to(ROOT).as_posix())
        with zipfile.ZipFile(archive) as z:
            bad=z.testzip()
            if bad:raise RuntimeError('Archive CRC failed: '+bad)
        entry={'file':archive.name,'files':len(files),'bytes':archive.stat().st_size,'sha256':digest(archive)}
        report['archives'].append(entry)
        (OUT/'manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        print('VERIFIED',name,entry['bytes'],flush=True)
    print('PUBLIC_SNAPSHOT_READY',sum(x['files'] for x in report['archives']),sum(x['bytes'] for x in report['archives']),flush=True)
def private_backup(destination):
    dest=pathlib.Path(destination)/'Horizonte-privado-2026-11-04';dest.mkdir(parents=True,exist_ok=True)
    selected=[]
    for base in [ROOT/'Backend/backups',ROOT/'tools/assinatura-horizonte',ROOT/'tools/release-071/credentials',ROOT/'tools/realtime-integration/credentials']:
        if base.exists():selected.extend(p for p in walk(base) if p.is_file())
    for base,dirs,files in os.walk(ROOT):
        dirs[:]=[d for d in dirs if d not in SKIP and d not in {'android','blender-4.2.0-windows-x64','third_party','ssh_libs','format-backup-20261104','wuzimu-validation-project'}]
        for name in files:
            p=pathlib.Path(base)/name
            if private(p):selected.append(p)
    entries=[]
    for p in sorted(set(selected)):
        rel=p.relative_to(ROOT);target=dest/'projeto'/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
        if digest(p)!=digest(target):raise RuntimeError('Private backup hash mismatch')
        entries.append({'path':('projeto'/rel).as_posix(),'sha256':digest(target),'bytes':target.stat().st_size})
    home=pathlib.Path.home()
    for p in [home/'.android/debug.keystore',home/'.ssh/known_hosts']:
        if p.exists():
            target=dest/'computador'/p.relative_to(home);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
            entries.append({'path':target.relative_to(dest).as_posix(),'sha256':digest(target),'bytes':target.stat().st_size})
    (dest/'manifesto-privado.json').write_text(json.dumps(entries,indent=2),encoding='utf-8')
    (dest/'LEIA-ME.txt').write_text('BACKUP PRIVADO - NAO ENVIAR AO GITHUB\nCopie esta pasta para o HD ANTES de formatar.\nPreserve o keystore: necessario para atualizar os APKs existentes.\nCredenciais da VPS e Vivox ficam privadas. O banco ativo permanece na VPS; dumps locais podem ser antigos.\n',encoding='utf-8')
    print('PRIVATE_BACKUP_VERIFIED',len(entries),str(dest),flush=True)
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--private-destination');args=parser.parse_args()
    private_backup(args.private_destination) if args.private_destination else public_backup()
