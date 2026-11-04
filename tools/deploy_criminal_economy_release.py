"""Publish the reviewed economy-only release to the authorized HBRP API."""
import getpass
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/ssh_libs'))
import paramiko

# Only these files and the hbrp-api service are changed remotely. Database
# migrations are additive; failure restores the prior API, never drops data.
REMOTE = r'''
import datetime,grp,hashlib,json,os,pathlib,shutil,subprocess,time
api=pathlib.Path('/srv/hbrp/api')
root=pathlib.Path('/srv/hbrp')
release=root/'backups'/'__RELEASE_NAME__'
stage=release/'staging'
manifest=json.loads((stage/'manifest.json').read_text())
allowed={'server.mjs','criminal-economy.mjs','015_criminal_economy_pilot.sql','016_criminal_economy_runtime.sql','verify-criminal-economy-postgres.mjs'}
assert set(manifest['files'])==allowed
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def run(args,input=None):
 p=subprocess.run(args,input=input,text=True,capture_output=True,check=True)
 return p.stdout.strip()
def sql(query):return run(['runuser','-u','postgres','--','psql','-p','55432','-d','hbrp','-A','-t','-v','ON_ERROR_STOP=1'],query)
def snapshot():
 return json.loads(sql("""SELECT json_build_object(
 'accounts',(SELECT count(*) FROM hbrp.accounts),'characters',(SELECT count(*) FROM hbrp.characters),
 'identities',(SELECT md5(coalesce(string_agg(id::text||':'||uid::text||':'||coalesce(email,'')||':'||status||':'||account_role||':'||admin_level,'|' ORDER BY id),'')) FROM hbrp.accounts),
 'credentials',(SELECT md5(coalesce(string_agg(account_id::text||':'||password_hash||':'||credential_version||':'||coalesce(revoked_at::text,''),'|' ORDER BY account_id),'')) FROM hbrp.beta_access),
 'charactersIdentity',(SELECT md5(coalesce(string_agg(id::text||':'||account_id::text||':'||name||':'||coalesce(gender,''),'|' ORDER BY id),'')) FROM hbrp.characters));"""))
def health():return json.loads(run(['curl','--fail','--silent','--show-error','--cacert',str(api/'tls/server.crt'),'https://187.41.106.219:8443/health']))
assert os.geteuid()==0 and api.resolve()==pathlib.Path('/srv/hbrp/api')
assert run(['systemctl','is-active','hbrp-api'])=='active'
assert digest(api/'server.mjs')==manifest['sourceServerHash'],'Production API changed after inspection'
for name,sha in manifest['files'].items():assert digest(stage/name)==sha,'Staging hash mismatch: '+name
reference=(api/'server.mjs').stat()
tls_hash=digest(api/'tls/server.crt')
before=snapshot()
(release/'before.json').write_text(json.dumps(before))
shutil.copy2(api/'server.mjs',release/'server.mjs.previous')
if (api/'criminal-economy.mjs').exists():shutil.copy2(api/'criminal-economy.mjs',release/'criminal-economy.mjs.previous')
dump=release/'database.dump'
with dump.open('wb') as output:
 subprocess.run(['runuser','-u','postgres','--','pg_dump','-p','55432','-d','hbrp','--format=custom','--no-owner'],stdout=output,stderr=subprocess.PIPE,check=True)
assert dump.stat().st_size>0
run(['pg_restore','--list',str(dump)])
os.chmod(dump,0o600)
(release/'backup-confirmed.json').write_text(json.dumps({'bytes':dump.stat().st_size,'catalogVerified':True,'sourceServerHash':manifest['sourceServerHash']}))
for name in ['server.mjs','criminal-economy.mjs','verify-criminal-economy-postgres.mjs']:run(['node','--check',str(stage/name)])
applied=[]
stopped=False
try:
 run(['systemctl','stop','hbrp-api']);stopped=True
 for version,name in [(15,'015_criminal_economy_pilot.sql'),(16,'016_criminal_economy_runtime.sql')]:
  if sql('SELECT count(*) FROM hbrp.schema_migrations WHERE version='+str(version)+';')=='0':
   sql((stage/name).read_text());applied.append(version)
  assert sql('SELECT count(*) FROM hbrp.schema_migrations WHERE version='+str(version)+';')=='1'
  shutil.copy2(stage/name,root/'database'/name)
 for name in ['criminal-economy.mjs','server.mjs']:
  target=api/name;temporary=api/(name+'.economy-update')
  shutil.copy2(stage/name,temporary);os.chown(temporary,reference.st_uid,reference.st_gid);os.chmod(temporary,reference.st_mode&0o777)
  os.replace(temporary,target)
  assert digest(target)==manifest['files'][name]
 run(['node','--check',str(api/'server.mjs')])
 run(['systemctl','start','hbrp-api']);stopped=False
 healthy=None
 for _ in range(20):
  try:
   healthy=health()
   if healthy.get('status')=='ok':break
  except Exception:pass
  time.sleep(.5)
 assert healthy and healthy.get('status')=='ok'
 assert run(['systemctl','is-active','hbrp-api'])=='active'
 # Owner connection is used only by a short-lived verification subprocess.
 # Sources are read using a supplementary group, without changing users/groups.
 verification=subprocess.run(['node','--input-type=module'],input=(stage/'verify-criminal-economy-postgres.mjs').read_text(),
  text=True,capture_output=True,user='postgres',group='postgres',extra_groups=[grp.getgrnam('hbrp-api').gr_gid],cwd='/tmp',timeout=90)
 if verification.returncode:
  (release/'verification-error.txt').write_text(verification.stderr)
  raise RuntimeError('PostgreSQL verification failed: '+verification.stderr[-2000:])
 verified=json.loads(verification.stdout.strip().splitlines()[-1])
 assert verified['temporaryAccountsRemaining']==0 and verified['fixtureTransactionRolledBack']
 (release/'verification.json').write_text(json.dumps(verified))
 status=run(['curl','--silent','--show-error','--cacert',str(api/'tls/server.crt'),'-o','/dev/null','-w','%{http_code}',
  'https://187.41.106.219:8443/v1/criminal-economy'])
 assert status=='401','Protected economy route unavailable'
 after=snapshot()
 assert before==after,'Existing identities/credentials changed during deployment'
 assert digest(api/'tls/server.crt')==tls_hash
 report={'deployed':True,'service':'active','health':healthy,'migrationsApplied':applied,'backup':str(release),
  'databaseBackupBytes':dump.stat().st_size,'dataPreserved':before==after,'before':before,'after':after,
  'certificatePreserved':True,'protectedEconomyStatus':int(status),'verification':verified,'hashes':manifest['files']}
 (release/'deployment.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
except Exception:
 shutil.copy2(release/'server.mjs.previous',api/'server.mjs')
 os.chown(api/'server.mjs',reference.st_uid,reference.st_gid);os.chmod(api/'server.mjs',reference.st_mode&0o777)
 if (release/'criminal-economy.mjs.previous').exists():
  shutil.copy2(release/'criminal-economy.mjs.previous',api/'criminal-economy.mjs')
  os.chown(api/'criminal-economy.mjs',reference.st_uid,reference.st_gid)
 run(['systemctl','restart','hbrp-api'])
 raise
'''

def main(stage=None):
    import datetime
    stage = stage or ROOT / 'tools/criminal-economy-validation/release'
    manifest = json.loads((stage / 'manifest.json').read_text())
    release_name = 'economy-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    destination = '/srv/hbrp/backups/' + release_name
    client = paramiko.SSHClient()
    client.load_host_keys(str(ROOT / 'tools/release-071/known_hosts'))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    password = getpass.getpass('VPS password: ')
    try:
        client.connect('187.41.106.219', username='root', password=password,timeout=20,allow_agent=False,look_for_keys=False)
        password = None
        client.get_transport().set_keepalive(20)
        with client.open_sftp() as sftp:
            sftp.mkdir(destination, mode=0o700)
            sftp.mkdir(destination + '/staging', mode=0o700)
            for name in ['manifest.json', *manifest['files']]:
                if '/' in name or '\\' in name:raise ValueError('Invalid release file')
                sftp.put(str(stage / name), destination + '/staging/' + name)
                sftp.chmod(destination + '/staging/' + name, 0o600)
        stdin, stdout, stderr = client.exec_command('python3 -', timeout=180)
        stdin.write(REMOTE.replace('__RELEASE_NAME__', release_name));stdin.flush();stdin.channel.shutdown_write()
        output = stdout.read().decode();error = stderr.read().decode()
        code = stdout.channel.recv_exit_status()
        if code:
            (stage / 'deployment-error.txt').write_text(error, encoding='utf-8')
            raise RuntimeError('Deployment failed; previous API restored. ' + error[-2500:])
        report = json.loads(output)
        (stage / 'deployment.json').write_text(json.dumps(report,indent=2), encoding='utf-8')
        print(json.dumps(report))
    finally:
        password = None
        client.close()

if __name__ == '__main__':
    main()
