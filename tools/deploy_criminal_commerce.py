"""Deploy only the reviewed HBRP commerce delta, preserving the inspected API entrypoint."""
import hashlib
import json
from pathlib import Path
import subprocess
import deploy_criminal_economy_release as deployment

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'tools/criminal-economy-validation/vps-before'
STAGE=ROOT/'tools/criminal-economy-validation/commerce-release'

def digest(data): return hashlib.sha256(data).hexdigest()

def main(extra_migrations=()):
    assert extra_migrations in [(),('018_criminal_production.sql',)]
    inspected=json.loads((BASE/'inspection.json').read_text())
    STAGE.mkdir(parents=True,exist_ok=True)
    server=(BASE/'server.mjs').read_bytes()
    assert digest(server)==inspected['files']['server.mjs']
    assert b'createCriminalEconomyRouteHandler' in server
    (STAGE/'server.mjs').write_bytes(server)
    files={
        'criminal-economy.mjs':ROOT/'Backend/api/criminal-economy.mjs',
        'verify-criminal-economy-postgres.mjs':ROOT/'Backend/api/verify-criminal-economy-postgres.mjs',
        **{name:ROOT/'Backend/database'/name for name in [
            '015_criminal_economy_pilot.sql','016_criminal_economy_runtime.sql','017_criminal_economy_commerce.sql',*extra_migrations]}
    }
    for name,source in files.items(): (STAGE/name).write_bytes(source.read_bytes())
    for name in ['server.mjs','criminal-economy.mjs','verify-criminal-economy-postgres.mjs']:
        subprocess.run(['node','--check',str(STAGE/name)],check=True)
    manifest={'sourceServerHash':inspected['files']['server.mjs'],
        'sourceEconomyHash':inspected['files']['criminal-economy.mjs'],
        'files':{name:digest((STAGE/name).read_bytes()) for name in ['server.mjs',*files]}}
    (STAGE/'manifest.json').write_text(json.dumps(manifest,indent=2))
    replacements={
        "'016_criminal_economy_runtime.sql','verify-criminal-economy-postgres.mjs'}":
        "'016_criminal_economy_runtime.sql','017_criminal_economy_commerce.sql',"+''.join(repr(n)+',' for n in extra_migrations)+"'verify-criminal-economy-postgres.mjs'}",
        "(16,'016_criminal_economy_runtime.sql')]:":
        "(16,'016_criminal_economy_runtime.sql'),(17,'017_criminal_economy_commerce.sql')"+''.join(','+repr((int(n[:3]),n)) for n in extra_migrations)+"]:",
        "reference=(api/'server.mjs').stat()":
        "assert digest(api/'criminal-economy.mjs')==manifest['sourceEconomyHash'],'Economy changed after inspection'\nreference=(api/'server.mjs').stat()"
    }
    remote=deployment.REMOTE
    for old,new in replacements.items():
        assert remote.count(old)==1,old
        remote=remote.replace(old,new)
    deployment.REMOTE=remote
    deployment.main(STAGE)

if __name__=='__main__': main()
