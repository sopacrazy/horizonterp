"""Prepare only the economy delta against the inspected production API."""
from pathlib import Path
import datetime
import hashlib
import json
import subprocess

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'tools/criminal-economy-validation/release'
BASE = ROOT / 'tools/criminal-economy-validation/vps-before'

def sha(data):
    return hashlib.sha256(data).hexdigest()

def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    inspection = json.loads((BASE / 'inspection.json').read_text())
    old_bytes = (BASE / 'server.mjs').read_bytes()
    assert sha(old_bytes) == inspection['files']['server.mjs']
    original = old_bytes.decode('utf-8')
    local = (ROOT / 'Backend/api/server.mjs').read_text(encoding='utf-8')
    imported = "import {createCriminalEconomy,createCriminalEconomyRouteHandler,CriminalEconomyError} from './criminal-economy.mjs';"
    assert imported not in original, 'Economy already deployed: prepare a reviewed incremental update instead of mounting routes twice'
    anchor = "import {isBetaLoginIdentifier} from './beta-login-name.mjs';"
    assert original.count(anchor) == 1 and imported in local
    result = original.replace(anchor, anchor + '\n' + imported)
    setup = local[local.index('const criminalEconomy='):local.index('let cryptoJobs=0;')]
    assert result.count('let cryptoJobs=0;') == 1
    result = result.replace('let cryptoJobs=0;', setup + 'let cryptoJobs=0;')
    route_start = "  if(path==='/v1/criminal-economy'||path.startsWith('/v1/criminal-economy/')){"
    route_end = "  if(req.method==='GET'&&path==='/v1/missions/arrival')"
    routes = local[local.index(route_start):local.index(route_end)]
    assert result.count(route_end) == 1
    result = result.replace(route_end, routes + route_end)
    catch_old = 'error instanceof RealtimeError||error instanceof BetaAccessError)'
    catch_new = 'error instanceof RealtimeError||error instanceof BetaAccessError||error instanceof CriminalEconomyError)'
    assert result.count(catch_old) == 1
    result = result.replace(catch_old, catch_new)
    (OUTPUT / 'server.mjs').write_text(result, encoding='utf-8', newline='\n')
    sources = {
        'criminal-economy.mjs': ROOT / 'Backend/api/criminal-economy.mjs',
        '015_criminal_economy_pilot.sql': ROOT / 'Backend/database/015_criminal_economy_pilot.sql',
        '016_criminal_economy_runtime.sql': ROOT / 'Backend/database/016_criminal_economy_runtime.sql',
        'verify-criminal-economy-postgres.mjs': ROOT / 'Backend/api/verify-criminal-economy-postgres.mjs',
    }
    for name, source in sources.items():
        assert source.is_file(), 'Pending source: ' + name
        (OUTPUT / name).write_bytes(source.read_bytes())
    for name in ['server.mjs', 'criminal-economy.mjs', 'verify-criminal-economy-postgres.mjs']:
        subprocess.run(['node', '--check', str(OUTPUT / name)], check=True)
    files = {name: sha((OUTPUT / name).read_bytes()) for name in ['server.mjs', *sources]}
    manifest = {
        'sourceServerHash': inspection['files']['server.mjs'],
        'files': files,
        'preparedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'HBRP economy only; preserve existing API modules, credentials and TLS',
    }
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(manifest))

if __name__ == '__main__':
    main()
