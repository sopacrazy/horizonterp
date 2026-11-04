"""Deploy the isolated HBRP API on the new VPS. No credentials are printed."""
import grp
import hashlib
import json
import os
import pathlib
import pwd
import shutil
import subprocess

root = pathlib.Path('/srv/hbrp')
api = root / 'api'
credentials = root / 'credentials'
ip = '187.41.106.219'

def run(args, **kwargs):
    return subprocess.run(args, check=True, text=True, **kwargs)

assert os.geteuid() == 0
assert api.joinpath('server.mjs').is_file()
assert credentials.joinpath('database.env').is_file()
assert not api.joinpath('tls/server.key').exists(), 'Certificate already exists; inspect before replacing'

if subprocess.run(['id', '-u', 'hbrp-api'], capture_output=True, check=False).returncode:
    run(['useradd', '--system', '--no-create-home', '--shell', '/usr/sbin/nologin', 'hbrp-api'])
gid = grp.getgrnam('hbrp-api').gr_gid
run(['npm', 'ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund'], cwd=api)

for path in (root, api):
    os.chown(path, 0, gid)
    os.chmod(path, 0o750)
for path in api.rglob('*'):
    os.chown(path, 0, gid)
    os.chmod(path, 0o750 if path.is_dir() else 0o640)

tls = api / 'tls'
tls.mkdir(mode=0o750, exist_ok=True)
os.chown(tls, 0, gid)
key, cert = tls / 'server.key', tls / 'server.crt'
run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-sha256', '-nodes',
     '-days', '365', '-subj', '/CN=HBRP API Brasil',
     '-addext', f'subjectAltName=IP:{ip},IP:127.0.0.1',
     '-keyout', str(key), '-out', str(cert)], capture_output=True)
for path in (key, cert):
    os.chown(path, 0, gid)
    os.chmod(path, 0o640)

der = subprocess.check_output(['openssl', 'x509', '-in', str(cert), '-outform', 'DER'])
public = {'baseUrl': f'https://{ip}:8443', 'certificateSha256': hashlib.sha256(der).hexdigest()}
api.joinpath('public-client-config.json').write_text(json.dumps(public, indent=2) + '\n')
os.chown(api / 'public-client-config.json', 0, gid)
os.chmod(api / 'public-client-config.json', 0o640)

database_url = next(line.split('=', 1)[1] for line in credentials.joinpath('database.env').read_text().splitlines()
                    if line.startswith('DATABASE_URL='))
api_env = credentials / 'api.env'
api_env.write_text(f'DATABASE_URL={database_url}\nPORT=8443\nTLS_KEY={key}\nTLS_CERT={cert}\n'
                   'NODE_OPTIONS=--max-old-space-size=128\n')
os.chmod(api_env, 0o600)

unit = '''[Unit]
Description=HBRP isolated account and multiplayer API
After=network.target postgresql@18-hbrp.service
Requires=postgresql@18-hbrp.service

[Service]
Type=simple
User=hbrp-api
Group=hbrp-api
WorkingDirectory=/srv/hbrp/api
EnvironmentFile=/srv/hbrp/credentials/api.env
ExecStart=/usr/bin/node /srv/hbrp/api/server.mjs
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
run(['systemctl', 'daemon-reload'])
run(['systemctl', 'enable', 'postgresql@18-hbrp.service'])
run(['systemctl', 'enable', '--now', 'hbrp-api.service'])
if run(['ufw', 'status'], capture_output=True).stdout.startswith('Status: active'):
    run(['ufw', 'allow', '8443/tcp', 'comment', 'HBRP API'])
print('HBRP_API_DEPLOYED ' + json.dumps(public))
