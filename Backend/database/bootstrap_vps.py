"""Initial HBRP-only installation. Run as root on the authorized VPS. No password output."""
import os,pathlib,secrets,subprocess,json,re
root=pathlib.Path('/srv/hbrp')
def run(args,**kw):return subprocess.run(args,check=True,text=True,**kw)
def sql(text,database='postgres'):
 return run(['runuser','-u','postgres','--','psql','-X','-p','55432','-v','ON_ERROR_STOP=1','-d',database],input=text,capture_output=True).stdout
assert os.geteuid()==0
assert not (root/'credentials').exists(),'Already initialized; inspect instead of resetting credentials.'
if pathlib.Path('/etc/postgresql/16/main').exists():
 run(['pg_ctlcluster','16','main','stop'])
 run(['pg_renamecluster','16','main','hbrp'])
config=pathlib.Path('/etc/postgresql/16/hbrp/postgresql.conf')
content=config.read_text()
for key,value in {'port':'55432','listen_addresses':'127.0.0.1','max_connections':'30','shared_buffers':'64MB',
                  'work_mem':'4MB','maintenance_work_mem':'64MB','password_encryption':'scram-sha-256',
                  'timezone':'UTC','log_timezone':'UTC','log_min_duration_statement':'1000'}.items():
 if key in ('listen_addresses','shared_buffers','work_mem','maintenance_work_mem','password_encryption','timezone','log_timezone'):
  value="'"+value+"'"
 pattern=r'^\s*#?\s*'+re.escape(key)+r'\s*=.*$'
 if re.search(pattern,content,re.M):content=re.sub(pattern,key+' = '+value,content,flags=re.M)
 else:content+='\n'+key+' = '+value+'\n'
config.write_text(content)
hba=pathlib.Path('/etc/postgresql/16/hbrp/pg_hba.conf')
hba.write_text('''# Dedicated HBRP cluster. Local administration and loopback application only.
local all postgres peer
local all all reject
host hbrp hbrp_app 127.0.0.1/32 scram-sha-256
host all all 0.0.0.0/0 reject
host all all ::0/0 reject
''')
os.chown(hba, __import__('pwd').getpwnam('postgres').pw_uid,__import__('grp').getgrnam('postgres').gr_gid)
os.chmod(hba,0o640)
dropin=pathlib.Path('/etc/systemd/system/postgresql@16-hbrp.service.d')
dropin.mkdir(parents=True,exist_ok=True)
(dropin/'hbrp-limits.conf').write_text('[Service]\nMemoryMax=768M\nCPUQuota=100%\nTasksMax=128\n')
run(['systemctl','daemon-reload'])
run(['pg_ctlcluster','16','hbrp','start'])
password=secrets.token_urlsafe(36)
sql(f"CREATE ROLE hbrp_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE; CREATE ROLE hbrp_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT 12 PASSWORD '{password}';")
sql("CREATE DATABASE hbrp OWNER hbrp_owner TEMPLATE template0 ENCODING 'UTF8';")
sql("REVOKE ALL ON DATABASE hbrp FROM PUBLIC; GRANT CONNECT ON DATABASE hbrp TO hbrp_app; ALTER ROLE hbrp_app IN DATABASE hbrp SET search_path = hbrp, pg_catalog; ALTER ROLE hbrp_app IN DATABASE hbrp SET statement_timeout='15s'; ALTER ROLE hbrp_app IN DATABASE hbrp SET idle_in_transaction_session_timeout='30s';")
schema=(root/'database/001_initial.sql').read_text()
sql('SET ROLE hbrp_owner;\n'+schema,'hbrp')
sql((root/'database/002_transfer_locking.sql').read_text(),'hbrp')
credentials=root/'credentials';credentials.mkdir(mode=0o700)
env=credentials/'database.env'
env.write_text(f'PGHOST=127.0.0.1\nPGPORT=55432\nPGDATABASE=hbrp\nPGUSER=hbrp_app\nPGPASSWORD={password}\nDATABASE_URL=postgresql://hbrp_app:{password}@127.0.0.1:55432/hbrp\n')
pgpass=credentials/'app.pgpass';pgpass.write_text(f'127.0.0.1:55432:hbrp:hbrp_app:{password}\n')
for path in (env,pgpass):os.chmod(path,0o600)
print(sql("SELECT count(*) AS tables FROM information_schema.tables WHERE table_schema='hbrp';",'hbrp'))
print('HBRP_DATABASE_CREATED credentials stored root-only; not printed')
