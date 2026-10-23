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
 health_status,health=request('GET','/health')
 check(health_status==200 and health.get('version')=='0.3.0','TLS verificado, API 0.3.0 e banco saudável')
 suffix=''.join(chr(97+int(c,16)) for c in uuid.uuid4().hex[:10]);mail=f'api-{suffix}@example.invalid';password=secrets.token_urlsafe(18)
 status,reply=request('POST','/v1/auth/register',{'email':mail,'password':password})
 check(status==201,'Cadastro cria conta sem personagem')
 account=remember(mail);old_token=reply['token']
 check(reply['state'] is None and reply['needsCharacter'],'Cadastro retorna criação de personagem pendente')
 check(admin(f"SELECT count(*) FROM hbrp.characters WHERE account_id='{account}';")=='0','Cadastro não cria personagem implicitamente')
 check(admin(f"SELECT password_hash LIKE 'scrypt$131072$8$1$%' FROM hbrp.password_credentials WHERE account_id='{created[0]}';")=='t','Senha persistida como scrypt, não texto puro')
 check(request('POST','/v1/auth/register',{'email':mail,'password':password})[0]==409,'Cadastro duplicado rejeitado')
 check(request('POST','/v1/auth/login',{'email':mail,'password':'WrongPassword123'})[0]==401,'Senha incorreta rejeitada')
 status,reply=request('POST','/v1/auth/login',{'email':mail,'password':password})
 check(status==200 and reply['state'] is None and reply['needsCharacter'],'Login de conta sem personagem não falha')
 token=reply['token']
 check(request('GET','/v1/player',token=old_token)[0]==401,'Login novo encerra sessão antiga')
 check(request('GET','/v1/player')[0]==401,'Estado não é público')
 check(request('GET','/v1/player',token=token)[1]['needsCharacter'],'GET player aceita conta pendente')
 check(request('PUT','/v1/player/profile',{'firstName':'Ana','lastName':'Silva'},token)[1]['code']=='CHARACTER_REQUIRED','Conta pendente não pode renomear personagem inexistente')
 identity={'firstName':'Ana','lastName':'Teste'+suffix,'gender':'female'}
 check(request('POST','/v1/characters',dict(identity,gender='other'),token)[0]==400,'Gênero inválido rejeitado')
 check(request('POST','/v1/characters',dict(identity,firstName='Ana123'),token)[0]==400,'Nome não humano rejeitado')
 check(request('POST','/v1/characters',dict(identity,cashCents='999'),token)[0]==400,'Criação não aceita atributos protegidos')
 with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
  replies=list(executor.map(lambda _:request('POST','/v1/characters',identity,token),range(2)))
 check(sorted(code for code,_ in replies)==[201,409],'Duas criações concorrentes produzem somente um personagem')
 first=next(value['state'] for code,value in replies if code==201);state=first
 check(first['uid'].isdigit() and first['world']=='centro_teste','UID permanente e cenário correto')
 check(first['firstName']=='Ana' and first['lastName']==identity['lastName'] and first['gender']=='female','Nome, sobrenome e gênero retornados no estado')
 check(admin(f"SELECT count(*) FROM hbrp.characters WHERE account_id='{account}' AND deleted_at IS NULL;")=='1','Banco mantém um personagem ativo por conta')
 for table in ('character_stats','character_positions','character_appearance','inventories'):
  check(admin(f"SELECT count(*) FROM hbrp.{table} WHERE character_id='{first['characterId']}';")=='1',f'Criação inicializa {table}')
 save={'requestId':str(uuid.uuid4()),'version':state['version'],'x':5.0,'y':-.1,'z':2.0,'yaw':130.0,'cameraYaw':140.0,'cameraPitch':20.0}
 status,reply=request('PUT','/v1/player/checkpoint',save,token)
 check(status==200 and reply['state']['x']==5 and reply['state']['z']==2,'Checkpoint salva posição e câmera')
 version=reply['state']['version']
 check(request('PUT','/v1/player/checkpoint',save,token)[1]['state']['version']==version,'Repetir salvamento não duplica operação')
 changed=dict(save,x=6)
 check(request('PUT','/v1/player/checkpoint',changed,token)[0]==409,'Chave de salvamento não pode ser reutilizada com outros dados')
 tamper=dict(save,requestId=str(uuid.uuid4()),version=version,cashCents='999999')
 check(request('PUT','/v1/player/checkpoint',tamper,token)[0]==400,'Cliente não pode enviar dinheiro')
 check(request('PUT','/v1/player/checkpoint',dict(save,requestId=str(uuid.uuid4()),version=version,health=999),token)[0]==400,'Cliente não pode enviar atributos protegidos')
 check(request('PUT','/v1/player/checkpoint',dict(save,requestId=str(uuid.uuid4()),version=version,x=9999),token)[0]==400,'Posição fora do mapa rejeitada')
 check(request('PUT','/v1/player/checkpoint',dict(save,requestId=str(uuid.uuid4())),token)[0]==409,'Versão antiga não sobrescreve progresso')
 new_identity={'firstName':'Maria','lastName':'Teste'+suffix};newname='Maria '+new_identity['lastName']
 status,renamed=request('PUT','/v1/player/profile',new_identity,token)
 check(status==200 and renamed['state']['name']==newname,'Renomear atualiza nome completo e identidade juntos')
 check(request('POST','/v1/auth/logout',{},token)[0]==200,'Logout encerra sessão')
 check(request('GET','/v1/player',token=token)[0]==401,'Token encerrado não acessa os dados')
 status,reply=request('POST','/v1/auth/login',{'email':mail,'password':password})
 check(status==200 and not reply['needsCharacter'] and reply['state']['uid']==first['uid'] and reply['state']['characterId']==first['characterId'] and reply['state']['name']==newname and reply['state']['x']==5 and reply['state']['cameraYaw']==140,'Novo login recupera personagem salvo para o lobby')
 check(reply['state']['cashCents']=='0' and reply['state']['health']==100,'Saldo e vida protegidos foram preservados')
 second_mail=f'api2-{suffix}@example.invalid'
 status,second=request('POST','/v1/auth/register',{'email':second_mail,'password':password})
 check(status==201,'Segunda conta independente');second_account=remember(second_mail)
 status,male=request('POST','/v1/characters',{'firstName':'João','lastName':'Outro'+suffix,'gender':'male'},second['token'])
 check(status==201 and male['state']['gender']=='male' and male['state']['model']=='bybeyby','Seleção masculina persistida sem inventar modelo 3D')
 check(request('GET','/v1/player?accountId='+created[0],token=second['token'])[1]['state']['accountId']==created[1],'Conta só recebe seu próprio personagem')
 report={'result':'API_VERIFIED','checks':checks}
 (root/'api/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
 print(json.dumps(report,ensure_ascii=False))
finally:cleanup()
