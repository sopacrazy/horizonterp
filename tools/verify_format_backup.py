"""Verify GitHub-stored release asset hashes against the recovery manifest."""
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(__file__).resolve().parents[1]
folder=root/'tools/format-backup-20261104'
manifest=json.loads((folder/'manifest.json').read_text())
release=json.loads(subprocess.check_output(['gh','api','repos/sopacrazy/horizonterp/releases/433917700'],text=True))
assets={a['name']:a for a in release['assets']};expected=[]
for archive in manifest['archives']:expected.extend(archive.get('parts',[archive]))
for p in [folder/'manifest.json',root/'tools/Restore-FormatBackup.ps1',root/'reports/RECUPERAR_APOS_FORMATACAO.md']:
 expected.append({'file':p.name,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
missing=[];bad=[]
for item in expected:
 asset=assets.get(item['file'])
 if not asset:missing.append(item['file']);continue
 if asset['state']!='uploaded' or asset['size']!=item['bytes'] or asset.get('digest')!='sha256:'+item['sha256']:bad.append(item['file'])
result={'tag':release['tag_name'],'draft':release['draft'],'expected_assets':len(expected),'verified_assets':len(expected)-len(missing)-len(bad),'missing':missing,'mismatches':bad,'source_files':sum(a['files'] for a in manifest['archives']),'source_archive_bytes':sum(a['bytes'] for a in manifest['archives'])}
(folder/'github-verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result));sys.exit(1 if missing or bad else 0)
