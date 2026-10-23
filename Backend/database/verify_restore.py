import pathlib,subprocess,json
root=pathlib.Path('/srv/hbrp')
backup=sorted((root/'backups').glob('hbrp-*.dump'))[-1]
name='hbrp_restore_check_20261023'
def pg(command,*args,**kwargs):
 return subprocess.run(['runuser','-u','postgres','--',command,'-p','55432',*args],check=True,capture_output=True,**kwargs)
assert name.startswith('hbrp_restore_check_')
pg('createdb','-O','hbrp_owner','-T','template0',name)
try:
 with backup.open('rb') as stream:pg('pg_restore','--no-owner','--role=hbrp_owner','-d',name,stdin=stream)
 count=pg('psql','-X','-At','-d',name,'-c',"SELECT count(*) FROM information_schema.tables WHERE table_schema='hbrp';",text=True).stdout.strip()
 assert count=='27',count
 result=pg('psql','-X','-v','ON_ERROR_STOP=1','-d',name,input=(root/'database/verify.sql').read_text(),text=True)
 assert 'ALL_HBRP_DATABASE_CHECKS_PASSED' in result.stderr
 report={'result':'RESTORE_VERIFIED','tables':int(count),'source_archive':backup.name,'checks':'All schema, privilege and financial checks passed after restoration.'}
 (root/'database/restore-validation.json').write_text(json.dumps(report,indent=2))
 print(json.dumps(report))
finally:
 pg('dropdb',name)
