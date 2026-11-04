"""Read-only inspection of the user-authorized HBRP host. No remote writes."""
import getpass
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/ssh_libs'))
import paramiko

REMOTE = r'''
import hashlib,json,pathlib,subprocess
api=pathlib.Path('/srv/hbrp/api')
def run(args,input=None):
 p=subprocess.run(args,input=input,text=True,capture_output=True,check=True)
 return p.stdout.strip()
sql="SELECT json_build_object('accounts',(SELECT count(*) FROM hbrp.accounts),'characters',(SELECT count(*) FROM hbrp.characters),'migrations',(SELECT json_agg(version ORDER BY version) FROM hbrp.schema_migrations));"
logs=run(['journalctl','-u','hbrp-api','--since','10 minutes ago','--no-pager','-o','cat'])
print(json.dumps({
 'service':run(['systemctl','is-active','hbrp-api']),
 'node':run(['node','--version']),
 'servicePaths':run(['systemctl','show','hbrp-api','-p','User','-p','Group','-p','WorkingDirectory','-p','FragmentPath']),
 'snapshot':json.loads(run(['runuser','-u','postgres','--','psql','-p','55432','-d','hbrp','-A','-t','-v','ON_ERROR_STOP=1'],sql)),
 'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(api.glob('*.mjs'))},
 'certificateSha256':hashlib.sha256((api/'tls/server.crt').read_bytes()).hexdigest(),
 'health':json.loads(run(['curl','--fail','--silent','--show-error','--cacert',str(api/'tls/server.crt'),'https://187.41.106.219:8443/health'])),
 'backupScriptPresent':pathlib.Path('/srv/hbrp/database/backup.sh').is_file()
 ,'recentServiceErrorCount':sum(1 for line in logs.splitlines() if any(tag in line for tag in ['request_error','database_connection_error','session_cleanup_error','SyntaxError','Error [ERR_']))
}))
'''

def main():
    client = paramiko.SSHClient()
    client.load_host_keys(str(ROOT / 'tools/release-071/known_hosts'))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    password = getpass.getpass('VPS password: ')
    try:
        client.connect('187.41.106.219', username='root', password=password,
                       timeout=20, allow_agent=False, look_for_keys=False)
        password = None
        stdin, stdout, stderr = client.exec_command('python3 -', timeout=90)
        stdin.write(REMOTE)
        stdin.flush()
        stdin.channel.shutdown_write()
        data = stdout.read().decode()
        error = stderr.read().decode()
        if stdout.channel.recv_exit_status():
            raise RuntimeError('Read-only inspection failed: ' + error[-1500:])
        report = json.loads(data)
        output = ROOT / 'tools/criminal-economy-validation/vps-before'
        output.mkdir(parents=True, exist_ok=True)
        (output / 'inspection.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        with client.open_sftp() as sftp:
            for name in report['files']:
                sftp.get('/srv/hbrp/api/' + name, str(output / name))
            sftp.get('/srv/hbrp/database/backup.sh', str(output / 'backup.sh'))
        print(json.dumps(report))
    finally:
        password = None
        client.close()

if __name__ == '__main__':
    main()
