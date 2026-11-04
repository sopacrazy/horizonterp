"""One-time HBRP database restore on a fresh, dedicated Ubuntu VPS.

Run as root after uploading the verified HBRP dump. Never prints credentials.
"""
import os
import pathlib
import re
import secrets
import subprocess

root = pathlib.Path('/srv/hbrp')
config = pathlib.Path('/etc/postgresql/18/hbrp/postgresql.conf')
hba = pathlib.Path('/etc/postgresql/18/hbrp/pg_hba.conf')
dump = pathlib.Path('/root/hbrp-20261030T164210Z.dump')

def run(args, **kwargs):
    return subprocess.run(args, check=True, text=True, **kwargs)

def sql(statement, database='postgres'):
    return run(['runuser', '-u', 'postgres', '--', 'psql', '-X', '-A', '-t', '-v', 'ON_ERROR_STOP=1',
                '-p', '55432', '-d', database], input=statement, capture_output=True).stdout

assert os.geteuid() == 0
assert root.joinpath('api/server.mjs').is_file() and dump.is_file()
assert config.is_file() and hba.is_file()
assert not root.joinpath('credentials').exists(), 'HBRP credentials already exist; do not reset them'

content = config.read_text()
for key, value in {
    'port': '55432',
    'listen_addresses': "'127.0.0.1'",
    'max_connections': '30',
    'shared_buffers': "'128MB'",
    'work_mem': "'4MB'",
    'maintenance_work_mem': "'64MB'",
    'password_encryption': "'scram-sha-256'",
    'timezone': "'UTC'",
    'log_timezone': "'UTC'",
    'log_min_duration_statement': '1000',
}.items():
    pattern = r'^\s*#?\s*' + re.escape(key) + r'\s*=.*$'
    if re.search(pattern, content, re.M):
        content = re.sub(pattern, key + ' = ' + value, content, flags=re.M)
    else:
        content += '\n' + key + ' = ' + value + '\n'
config.write_text(content)
hba.write_text('''# HBRP-only cluster: local administration and loopback application.
local all postgres peer
local all all reject
host hbrp hbrp_app 127.0.0.1/32 scram-sha-256
host all all 0.0.0.0/0 reject
host all all ::0/0 reject
''')
os.chown(hba, __import__('pwd').getpwnam('postgres').pw_uid,
         __import__('grp').getgrnam('postgres').gr_gid)
os.chmod(hba, 0o640)
run(['pg_ctlcluster', '18', 'hbrp', 'restart'])

password = secrets.token_urlsafe(36)
if sql("SELECT count(*) FROM pg_roles WHERE rolname='hbrp_owner';").strip() == '0':
    sql("CREATE ROLE hbrp_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;")
if sql("SELECT count(*) FROM pg_roles WHERE rolname='hbrp_app';").strip() == '0':
    sql(f"CREATE ROLE hbrp_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION "
        f"CONNECTION LIMIT 12 PASSWORD '{password}';")
else:
    sql(f"ALTER ROLE hbrp_app PASSWORD '{password}';")
if sql("SELECT count(*) FROM pg_database WHERE datname='hbrp';").strip() == '0':
    sql("CREATE DATABASE hbrp OWNER hbrp_owner TEMPLATE template0 ENCODING 'UTF8';")
sql("REVOKE ALL ON DATABASE hbrp FROM PUBLIC; GRANT CONNECT ON DATABASE hbrp TO hbrp_app; "
    "ALTER ROLE hbrp_app IN DATABASE hbrp SET search_path = hbrp, pg_catalog; "
    "ALTER ROLE hbrp_app IN DATABASE hbrp SET statement_timeout='15s'; "
    "ALTER ROLE hbrp_app IN DATABASE hbrp SET idle_in_transaction_session_timeout='30s';")

assert sql("SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname='hbrp';", 'hbrp').strip() == '0', \
    'Target database already contains HBRP tables; inspect before restoring'
with root.joinpath('database/restore-kvm1.log').open('w') as log, dump.open('rb') as archive:
    run(['runuser', '-u', 'postgres', '--', 'pg_restore', '--exit-on-error',
         '--no-owner', '--role=hbrp_owner', '-p', '55432', '-d', 'hbrp'],
        stdin=archive, stdout=log, stderr=subprocess.STDOUT)

count = sql("SELECT max(version), count(*) FROM hbrp.schema_migrations;", 'hbrp').strip()
assert count.startswith('10|'), count

credentials = root / 'credentials'
credentials.mkdir(mode=0o700)
env = credentials / 'database.env'
env.write_text('PGHOST=127.0.0.1\nPGPORT=55432\nPGDATABASE=hbrp\n'
               'PGUSER=hbrp_app\nPGPASSWORD=' + password + '\n'
               'DATABASE_URL=postgresql://hbrp_app:' + password + '@127.0.0.1:55432/hbrp\n')
os.chmod(env, 0o600)
pgpass = credentials / 'app.pgpass'
pgpass.write_text('127.0.0.1:55432:hbrp:hbrp_app:' + password + '\n')
os.chmod(pgpass, 0o600)
print('HBRP_DATABASE_RESTORED migration=10; credentials stored root-only')
