import pathlib,subprocess,os,pwd,grp,json,hashlib,shutil
root=pathlib.Path('/srv/hbrp');api=root/'api'
def run(args,**kw):return subprocess.run(args,check=True,text=True,**kw)
assert os.geteuid()==0
# Dedicated OS identity; no ownership or configuration changes to existing applications.
if subprocess.run(['id','-u','hbrp-api'],capture_output=True).returncode:
 run(['useradd','--system','--no-create-home','--shell','/usr/sbin/nologin','hbrp-api'])
gid=grp.getgrnam('hbrp-api').gr_gid
os.chown(root,0,gid);os.chmod(root,0o750)
os.chown(api,0,gid);os.chmod(api,0o750)
run([shutil.which('npm'),'install','--omit=dev','--ignore-scripts','--no-audit','--no-fund'],cwd=api)
for path in api.rglob('*'):
 os.chown(path,0,gid)
 if path.is_dir():os.chmod(path,0o750)
 else:os.chmod(path,0o640)
tls=api/'tls';tls.mkdir(mode=0o750,exist_ok=True);os.chown(tls,0,gid)
key=tls/'server.key';cert=tls/'server.crt'
if not key.exists():
 run(['openssl','req','-x509','-newkey','rsa:2048','-sha256','-nodes','-days','365','-subj','/CN=HBRP Test API',
      '-addext','subjectAltName=IP:70.38.101.147,IP:127.0.0.1','-keyout',str(key),'-out',str(cert)],capture_output=True)
for path in (key,cert):os.chown(path,0,gid);os.chmod(path,0o640)
der=subprocess.check_output(['openssl','x509','-in',str(cert),'-outform','DER'])
public={'baseUrl':'https://70.38.101.147:8443','certificateSha256':hashlib.sha256(der).hexdigest()}
(api/'public-client-config.json').write_text(json.dumps(public,indent=2))
env={}
for line in (root/'credentials/database.env').read_text().splitlines():
 if '=' in line:
  k,v=line.split('=',1);env[k]=v
api_env=root/'credentials/api.env'
api_env.write_text(f'DATABASE_URL={env["DATABASE_URL"]}\nPORT=8443\nTLS_KEY={key}\nTLS_CERT={cert}\nNODE_OPTIONS=--max-old-space-size=128\n')
os.chmod(api_env,0o600)
run([str(root/'database/backup.sh')])
applied=run(['runuser','-u','postgres','--','psql','-X','-At','-p','55432','-d','hbrp','-c','SELECT version FROM hbrp.schema_migrations'],capture_output=True).stdout.splitlines()
for version,filename in [('3','003_api.sql'),('4','004_character_creation.sql')]:
 if version not in applied:
  run(['runuser','-u','postgres','--','psql','-X','-p','55432','-d','hbrp','-v','ON_ERROR_STOP=1'],input=(root/'database'/filename).read_text())
unit=f'''[Unit]
Description=HBRP isolated account and persistence API
After=network.target postgresql@16-hbrp.service
Requires=postgresql@16-hbrp.service

[Service]
Type=simple
User=hbrp-api
Group=hbrp-api
WorkingDirectory=/srv/hbrp/api
EnvironmentFile=/srv/hbrp/credentials/api.env
ExecStart={shutil.which('node')} /srv/hbrp/api/server.mjs
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
UMask=0077
MemoryMax=512M
CPUQuota=100%
TasksMax=64
LimitNOFILE=4096

[Install]
WantedBy=multi-user.target
'''
pathlib.Path('/etc/systemd/system/hbrp-api.service').write_text(unit)
run(['systemctl','daemon-reload'])
run(['systemctl','enable','hbrp-api'])
run(['systemctl','restart','hbrp-api'])
run(['ufw','allow','8443/tcp','comment','HBRP API isolated TLS'])
print('HBRP_API_DEPLOYED',json.dumps(public))
