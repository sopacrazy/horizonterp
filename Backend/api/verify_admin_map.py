"""Run on the authorized HBRP host; fixtures are removed even after failed checks."""
import concurrent.futures,json,urllib.request,urllib.error,ssl,uuid,secrets,pathlib,sys,subprocess,re
root=pathlib.Path('/srv/hbrp');cert=root/'api/tls/server.crt'
context=ssl.create_default_context(cafile=str(cert))
base='https://127.0.0.1:8443'
created=[];checks=[]
def check(value,message):
 if not value:raise AssertionError(message)
 checks.append(message)
def request(method,path,body=None,token=None):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers=headers,method=method)
 try:
  with urllib.request.urlopen(req,context=context,timeout=20) as response:return response.status,json.load(response)
 except urllib.error.HTTPError as error:return error.code,json.load(error)
def admin(query):
 return subprocess.run(['runuser','-u','postgres','--','psql','-X','-At','-p','55432','-d','hbrp','-v','ON_ERROR_STOP=1'],input=query,text=True,capture_output=True,check=True).stdout.strip()

def remember(mail):
 assert re.fullmatch(r'[a-z0-9-]+@example\.invalid',mail)
 account=admin(f"SELECT id FROM hbrp.accounts WHERE email='{mail}';")
 assert str(uuid.UUID(account))==account
 created.append(account)
 return account
def cleanup():
 for account in created:
  assert str(uuid.UUID(account))==account
  admin(f"""BEGIN;
   DELETE FROM hbrp.player_reports WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.audit_events WHERE actor_account_id='{account}';
   DELETE FROM hbrp.sessions WHERE account_id='{account}';
   DELETE FROM hbrp.wallets WHERE account_id='{account}' OR character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.inventories WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.character_appearance WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.character_positions WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.character_stats WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='{account}');
   DELETE FROM hbrp.characters WHERE account_id='{account}';
   DELETE FROM hbrp.password_credentials WHERE account_id='{account}';
   DELETE FROM hbrp.auth_identities WHERE account_id='{account}';
   DELETE FROM hbrp.accounts WHERE id='{account}'; COMMIT;""")

try:
 suffix=uuid.uuid4().hex[:12];mail='maptp-'+suffix+'@example.invalid';token=secrets.token_urlsafe(32)
 account=admin("INSERT INTO hbrp.accounts(email) VALUES('"+mail+"') RETURNING id;").splitlines()[0];created.append(account)
 admin(f"INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES('{account}',sha256(convert_to('{token}','UTF8')),now()+interval '10 minutes');")
 status,reply=request('POST','/v1/characters',{'firstName':'Teste','lastName':'Mapa'+''.join(chr(97+int(c,16)) for c in suffix),'gender':'male'},token)
 check(status==201,'Personagem temporário criado')
 point={'x':2489.3,'z':1668.5}
 check(request('POST','/v1/admin/map-teleport',point)[0]==401,'Anônimo rejeitado')
 check(request('POST','/v1/admin/map-teleport',point,token)[0]==403,'Jogador comum rejeitado')
 admin(f"UPDATE hbrp.accounts SET account_role='admin',admin_level=1 WHERE id='{account}';")
 status,reply=request('POST','/v1/admin/map-teleport',point,token)
 check(status==200 and reply.get('authorized') and reply['x']==point['x'] and reply['z']==point['z'],'Admin nível 1 autorizado')
 check(admin(f"SELECT count(*) FROM hbrp.audit_events WHERE actor_account_id='{account}' AND action='admin.map_teleport';")=='1','Ação auditada')
 for point in [{'x':4201,'z':0},{'x':'12','z':1000},{'x':12,'z':1000,'y':999},{'x':0}]:
  check(request('POST','/v1/admin/map-teleport',point,token)[0]==400,'Coordenada inválida rejeitada')
 admin(f"UPDATE hbrp.accounts SET account_role='user',admin_level=0 WHERE id='{account}';")
 check(request('POST','/v1/admin/map-teleport',{'x':100,'z':1000},token)[0]==403,'Permissão revogada respeitada na mesma sessão')
 check(request('GET','/health')[1]['status']=='ok','Health TLS verificado')
 print(json.dumps({'result':'ADMIN_MAP_VERIFIED','checks':checks},ensure_ascii=False))
finally:
 cleanup()
 print('TEMPORARY_MAP_ACCOUNT_REMOVED')
