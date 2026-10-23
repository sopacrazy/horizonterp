"""Tests only its own UUID-tagged fixtures; removes them in finally."""
import os,pathlib,subprocess,uuid,concurrent.futures,json
root=pathlib.Path('/srv/hbrp')
account,character,first,second,key=[str(uuid.uuid4()) for _ in range(5)]
def admin(query):
 return subprocess.run(['runuser','-u','postgres','--','psql','-X','-At','-p','55432','-d','hbrp','-v','ON_ERROR_STOP=1'],input=query,text=True,capture_output=True,check=True).stdout.strip()
env=os.environ.copy();env['PGPASSFILE']=str(root/'credentials/app.pgpass')
def app(query):
 result=subprocess.run(['psql','-X','-At','-h','127.0.0.1','-p','55432','-U','hbrp_app','-d','hbrp','-v','ON_ERROR_STOP=1'],input=query,text=True,capture_output=True,env=env)
 if result.returncode:raise RuntimeError(result.stderr)
 return result.stdout.strip()
admin(f"BEGIN; SET LOCAL ROLE hbrp_owner; INSERT INTO hbrp.accounts(id) VALUES('{account}'); INSERT INTO hbrp.characters(id,account_id,name) VALUES('{character}','{account}','T{character[:20]}'); INSERT INTO hbrp.wallets(id,character_id,currency) VALUES('{first}','{character}','cash'),('{second}','{character}','bank'); COMMIT;")
try:
 with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
  ids=list(pool.map(lambda _:app(f"SELECT hbrp.change_balance('{first}',100,'{key}','Teste concorrente');"),range(8)))
 assert len(set(ids))==1
 assert app(f"SELECT balance FROM hbrp.wallets WHERE id='{first}';")=='100'
 with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
  list(pool.map(lambda _:app(f"SELECT hbrp.change_balance('{first}',100,'{uuid.uuid4()}','Créditos concorrentes');"),range(8)))
 assert app(f"SELECT balance FROM hbrp.wallets WHERE id='{first}';")=='900'
 with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
  list(pool.map(lambda _:app(f"SELECT hbrp.transfer_balance('{first}','{second}',10,'{uuid.uuid4()}','Transferências concorrentes');"),range(8)))
 assert app(f"SELECT balance FROM hbrp.wallets WHERE id='{first}';")=='820'
 assert app(f"SELECT balance FROM hbrp.wallets WHERE id='{second}';")=='80'
 result={'result':'CONCURRENCY_VERIFIED','same_key_requests':8,'independent_credits':8,'parallel_transfers':8,'final_total':900,'duplicate_credit':False}
 (root/'database/concurrency-validation.json').write_text(json.dumps(result,indent=2))
 print(json.dumps(result))
finally:
 admin(f"BEGIN; DELETE FROM hbrp.wallet_entries WHERE wallet_id IN ('{first}','{second}'); DELETE FROM hbrp.financial_operations WHERE source_wallet IN ('{first}','{second}') OR destination_wallet IN ('{first}','{second}'); DELETE FROM hbrp.wallets WHERE id IN ('{first}','{second}'); DELETE FROM hbrp.characters WHERE id='{character}'; DELETE FROM hbrp.accounts WHERE id='{account}'; COMMIT;")
