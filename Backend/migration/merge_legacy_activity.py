"""Merge a frozen legacy activity snapshot, keeping newer saves and all revocations.

Run as root on the HBRP destination after both APIs stop and backups complete.
The input contains private data and must stay outside Git and tool output.
"""
import json,pathlib,subprocess,sys
def sql(query):
 return subprocess.run(['runuser','-u','postgres','--','psql','-X','-At','-p','55432','-d','hbrp','-v','ON_ERROR_STOP=1'],input=query,text=True,capture_output=True,check=True).stdout.strip()
data=json.loads(pathlib.Path(sys.argv[1]).read_text())
assert set(data)=={'accountsDigest','character_stats','character_positions','sessions','save_requests','audit_events'}
digest=sql("SELECT md5(string_agg(md5(to_jsonb(t)::text),'' ORDER BY id)) FROM hbrp.accounts t;")
assert digest==data['accountsDigest'],'Account identities differ; manual reconciliation required'
statements=['BEGIN;']
for table in ['character_stats','character_positions','sessions','save_requests','audit_events']:
 rows=data[table]
 if not rows:continue
 columns=sql(f"SELECT column_name FROM information_schema.columns WHERE table_schema='hbrp' AND table_name='{table}' ORDER BY ordinal_position;").splitlines()
 assert all(set(row)==set(columns) for row in rows)
 payload=json.dumps(rows).replace("'","''")
 if table=='audit_events':
  names=','.join('"'+c+'"' for c in columns if c!='id')
  values=','.join('r."'+c+'"' for c in columns if c!='id')
  statements.append(f"INSERT INTO hbrp.audit_events({names}) SELECT {values} FROM jsonb_populate_recordset(NULL::hbrp.audit_events,'{payload}'::jsonb) r WHERE NOT EXISTS(SELECT 1 FROM hbrp.audit_events e WHERE to_jsonb(e)-'id'=to_jsonb(r)-'id');")
  continue
 insert=f"INSERT INTO hbrp.{table} AS existing SELECT * FROM jsonb_populate_recordset(NULL::hbrp.{table},'{payload}'::jsonb) WHERE true "
 if table in ('character_stats','character_positions'):
  setters=','.join(f'"{c}"=EXCLUDED."{c}"' for c in columns if c!='character_id')
  insert+=f'ON CONFLICT(character_id) DO UPDATE SET {setters} WHERE EXCLUDED.updated_at>existing.updated_at;'
 elif table=='sessions':
  insert+="ON CONFLICT(id) DO UPDATE SET revoked_at=COALESCE(existing.revoked_at,EXCLUDED.revoked_at),expires_at=GREATEST(existing.expires_at,EXCLUDED.expires_at),last_checkpoint_at=GREATEST(existing.last_checkpoint_at,EXCLUDED.last_checkpoint_at);"
 else:insert+='ON CONFLICT DO NOTHING;'
 statements.append(insert)
statements.append('COMMIT;')
sql('\n'.join(statements))
print('LEGACY_ACTIVITY_MERGED '+json.dumps({name:len(data[name]) for name in data if name!='accountsDigest'}))
