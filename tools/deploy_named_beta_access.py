"""Run on the new HBRP VPS after upload; never print credentials or tokens."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import time
from urllib.parse import urlparse

ROOT = Path('/srv/hbrp')
BACKUP = ROOT / 'backups/named-team-20261102-213901'
STAGE = BACKUP / 'staging'
API = ROOT / 'api'
EXPECTED = {
    'server.mjs': '992168d45df4c72003f7aae4d2d92bf20be1412f5c3a447a094270f96cfba128',
    'beta-login-name.mjs': '8b0a0af8ebb147072ce8c720f0a2029b3cd244751b7020998114a41d228d496a',
    'provision_named_beta_accounts.mjs': 'efc211fd5a1aa73ab93022b75c522f4a5bfb7e9260c621fdba7f97c56fb59111',
    '014_beta_named_login.sql': 'bb274f6700b42c31b1466de694fe3c0d16ac9618337a9680030df34260e37ca8',
    'issuance-input.json': 'debb69adb2bf8bfb5db1048bcd205cd22a0baa9c68b3b203c42338af85a6fd98',
}


def digest(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


def run(args, **kwargs):
    result = subprocess.run(args, text=True, capture_output=True, **kwargs)
    if result.returncode:
        raise RuntimeError('Command failed: ' + args[0])
    return result.stdout.strip()


def sql(command):
    return run(['runuser', '-u', 'postgres', '--', 'psql', '-p', '55432', '-d', 'hbrp',
                '-A', '-t', '-v', 'ON_ERROR_STOP=1'], input=command)


def snapshot():
    return json.loads(sql("""SELECT json_build_object(
      'accounts',(SELECT count(*) FROM hbrp.accounts),
      'characters',(SELECT count(*) FROM hbrp.characters),
      'priorCredentials',(SELECT md5(string_agg(a.uid::text||':'||a.account_role||':'||a.admin_level||':'||
        b.password_hash||':'||b.credential_version||':'||b.can_manage||':'||coalesce(b.revoked_at::text,''),'|' ORDER BY a.uid))
        FROM hbrp.accounts a JOIN hbrp.beta_access b ON b.account_id=a.id WHERE a.uid<=117),
      'manager',(SELECT json_build_object('uid',a.uid::text,'role',a.account_role,'level',a.admin_level,'canManage',b.can_manage)
        FROM hbrp.accounts a JOIN hbrp.beta_access b ON b.account_id=a.id WHERE a.uid=18));"""))


def health():
    return json.loads(run(['curl', '--fail', '--silent', '--show-error', '--cacert',
                          str(API / 'tls/server.crt'), 'https://187.41.106.219:8443/health']))


def main():
    assert os.geteuid() == 0
    assert Path('/srv/hbrp/api').resolve() == API.resolve()
    manifest = json.loads((BACKUP / 'manifest.json').read_text())
    assert manifest['backupSucceeded'] and manifest['catalogVerified']
    assert Path(manifest['databaseBackup']).stat().st_size == manifest['bytes']
    run(['pg_restore', '--list', manifest['databaseBackup']])
    for name, expected in EXPECTED.items():
        assert digest(STAGE / name) == expected, 'Unexpected staged file: ' + name
        os.chmod(STAGE / name, 0o600)
    current_hash = digest(API / 'server.mjs')
    assert current_hash in {manifest['sourceServerHash'], EXPECTED['server.mjs']}
    before_file = BACKUP / 'before.json'
    if before_file.exists():
        before = json.loads(before_file.read_text())
    else:
        before = snapshot()
        assert before['accounts'] == 10 and before['characters'] == 1
        before_file.write_text(json.dumps(before)); os.chmod(before_file, 0o600)
    assert before['manager'] == {'uid': '18', 'role': 'admin', 'level': 1, 'canManage': True}
    tls_hash = digest(API / 'tls/server.crt')
    for name in ['server.mjs', 'beta-login-name.mjs', 'provision_named_beta_accounts.mjs']:
        run(['node', '--check', str(STAGE / name)])
    pending = sql('SELECT count(*) FROM hbrp.schema_migrations WHERE version=14;') == '0'
    if pending:
        sql((STAGE / '014_beta_named_login.sql').read_text())
    assert sql('SELECT count(*) FROM hbrp.schema_migrations WHERE version=14;') == '1'
    reference = (API / 'server.mjs').stat()
    for name in ['beta-login-name.mjs', 'provision_named_beta_accounts.mjs', 'server.mjs']:
        destination = API / name
        temporary = API / (name + '.named-update')
        shutil.copy2(STAGE / name, temporary)
        os.chown(temporary, reference.st_uid, reference.st_gid); os.chmod(temporary, 0o640)
        os.replace(temporary, destination)
        assert digest(destination) == EXPECTED[name]
    shutil.copy2(STAGE / '014_beta_named_login.sql', ROOT / 'database/014_beta_named_login.sql')
    env = os.environ.copy()
    for line in (ROOT / 'credentials/api.env').read_text().splitlines():
        if line.startswith('DATABASE_URL='):
            env['DATABASE_URL'] = line.split('=', 1)[1].strip().strip("\"'")
    url = urlparse(env['DATABASE_URL'])
    assert url.hostname in {'127.0.0.1', 'localhost', '::1'} and url.port == 55432 and url.path == '/hbrp'
    issued = json.loads(run(['node', str(API / 'provision_named_beta_accounts.mjs'), '--input',
                             str(STAGE / 'issuance-input.json')], cwd=API, env=env))
    assert len(issued['accounts']) == 14
    assert all(row['accountRole'] == 'user' and row['adminLevel'] == 0 for row in issued['accounts'])
    (BACKUP / 'issuance-result.json').write_text(json.dumps(issued)); os.chmod(BACKUP / 'issuance-result.json', 0o600)
    run(['node', '--check', str(API / 'server.mjs')])
    run(['systemctl', 'restart', 'hbrp-api'])
    healthy = None
    try:
        for _ in range(12):
            try:
                healthy = health()
                if healthy.get('status') == 'ok':
                    break
            except Exception:
                time.sleep(.5)
        assert healthy and healthy.get('status') == 'ok'
        assert run(['systemctl', 'is-active', 'hbrp-api']) == 'active'
    except Exception:
        shutil.copy2(BACKUP / 'server.mjs', API / 'server.mjs')
        os.chown(API / 'server.mjs', reference.st_uid, reference.st_gid); os.chmod(API / 'server.mjs', 0o640)
        run(['systemctl', 'restart', 'hbrp-api'])
        raise
    after = snapshot()
    assert after['accounts'] == before['accounts'] + 14 and after['characters'] == before['characters']
    assert after['priorCredentials'] == before['priorCredentials'] and after['manager'] == before['manager']
    assert digest(API / 'tls/server.crt') == tls_hash
    report = {'deployed': True, 'migration14Applied': pending, 'service': 'active', 'health': healthy,
              'before': before, 'after': after, 'accounts': issued['accounts'], 'created': issued['created'],
              'reused': issued['reused'], 'backup': str(BACKUP), 'certificatePreserved': True,
              'completedAt': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    (BACKUP / 'deployment.json').write_text(json.dumps(report)); os.chmod(BACKUP / 'deployment.json', 0o600)
    print(json.dumps(report))


if __name__ == '__main__':
    main()
