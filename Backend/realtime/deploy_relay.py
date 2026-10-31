"""Run as root on the authorized HBRP VPS after uploading the verified relay archive."""
from pathlib import Path
import json,os,pwd,grp,secrets,subprocess,hashlib,tarfile
root=Path('/srv/hbrp');archive=root/'realtime-0.7.1.tar.gz'
assert os.geteuid()==0
assert hashlib.sha256(archive.read_bytes()).hexdigest()=='3d41730154e2a2a1eef23548f4a51485d6be8b817f55a6d77b11e52ad8e8cb49'
target=root/'realtime/0.7.1';target.mkdir(parents=True,exist_ok=True)
with tarfile.open(archive) as tar:
 for member in tar.getmembers():
  assert not member.issym() and not member.islnk()
  assert str((target/member.name).resolve()).startswith(str(target.resolve())+'/')
 tar.extractall(target,filter='data')
gid=grp.getgrnam('hbrp-api').gr_gid
for p in [target.parent,target,*target.rglob('*')]:
 os.chown(p,0,gid);os.chmod(p,0o750 if p.is_dir() or p.name=='HbrpRealtimeServer' else 0o640)
config=root/'realtime/server.json'
config.write_text(json.dumps({'bindAddress':'0.0.0.0','port':7777,'certificateFile':'/srv/hbrp/api/tls/server.crt','privateKeyFile':'/srv/hbrp/api/tls/server.key'}))
os.chown(config,0,gid);os.chmod(config,0o640)
env=root/'credentials/realtime.env'
if not env.exists():
 env.write_text('HBRP_REALTIME_SECRET='+secrets.token_urlsafe(48)+'\nHBRP_UDP_HOST=187.41.106.219\nHBRP_UDP_PORT=7777\nHBRP_UDP_SERVER_NAME=187.41.106.219\nHBRP_UDP_CA_FILE=/srv/hbrp/api/tls/server.crt\n')
os.chmod(env,0o600)
unit=Path('/etc/systemd/system/hbrp-realtime.service')
assert not unit.exists(),'Review an existing service before replacing it'
unit.write_text('''[Unit]
Description=Horizonte authenticated Unity Transport UDP relay
After=network.target hbrp-api.service

[Service]
Type=simple
User=hbrp-api
Group=hbrp-api
WorkingDirectory=/srv/hbrp/realtime/0.7.1
EnvironmentFile=/srv/hbrp/credentials/realtime.env
ExecStart=/srv/hbrp/realtime/0.7.1/HbrpRealtimeServer -batchmode -nographics -logFile - --hbrp-udp-server-config /srv/hbrp/realtime/server.json
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
MemoryMax=512M
CPUQuota=70%
TasksMax=256
LimitNOFILE=4096
UMask=0077

[Install]
WantedBy=multi-user.target
''')
drop=Path('/etc/systemd/system/hbrp-api.service.d');drop.mkdir(exist_ok=True)
override=drop/'realtime.conf';assert not override.exists()
override.write_text('[Service]\nEnvironmentFile=/srv/hbrp/credentials/realtime.env\n')
subprocess.run(['systemctl','daemon-reload'],check=True)
subprocess.run(['systemctl','enable','--now','hbrp-realtime'],check=True)
subprocess.run(['systemctl','restart','hbrp-api'],check=True)
print('HBRP_RELAY_DEPLOYED credentials_preserved=true database_unchanged=true')
