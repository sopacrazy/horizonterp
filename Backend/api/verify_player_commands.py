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

import time
players=[]
created.extend(admin("SELECT id FROM hbrp.accounts WHERE email ~ '^commands-[a-f0-9]{12}-[0-2]@example[.]invalid$';").splitlines())
cleanup();created.clear()
try:
 check(request('GET','/health')[1].get('status')=='ok','Health com TLS validado')
 suffix=uuid.uuid4().hex[:12]
 for n in range(3):
  mail=f'commands-{suffix}-{n}@example.invalid'
  token=secrets.token_urlsafe(32)
  account=admin("INSERT INTO hbrp.accounts(email) VALUES('"+mail+"') RETURNING id;").splitlines()[0]
  created.append(account)
  admin(f"INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES('{account}',sha256(convert_to('{token}','UTF8')),now()+interval '10 minutes');")
  status,reply=request('POST','/v1/characters',{'firstName':'Teste','lastName':'Comandos'+''.join(chr(97+int(c,16)) for c in suffix)+chr(65+n),'gender':'male'},token)
  check(status==201,'Personagem normal criado: '+str(status)+' '+str(reply.get('code','')))
  players.append({'token':token,'uid':reply['state']['uid'],'clientId':str(uuid.uuid4()),'sequence':0,'x':[0,10,80][n]})
 def sync(n):
  player=players[n];player['sequence']+=1
  status,reply=request('POST','/v1/multiplayer/sync',dict(clientId=player['clientId'],sequence=player['sequence'],x=player['x'],y=0,z=0,yaw=0,speed=0,jumping=False,jumpPhase=0),player['token'])
  check(status==200,'Sync autenticado');return reply
 def command(n,text,expected=200):
  time.sleep(1.3)
  for i in range(3):sync(i)
  player=players[n]
  status,reply=request('POST','/v1/multiplayer/chat',{'clientId':player['clientId'],'messageId':str(uuid.uuid4()),'text':text},player['token'])
  check(status==expected,'Resposta '+text.split()[0]);return reply
 for i in range(3):sync(i)
 result=command(0,'/ajuda')['command'];check(result['status']=='open_help' and len(result['help'])==21,'Guia contém 21 comandos de jogador')
 for name in ['gps','phone','stats','quests','anim']:
  check(command(0,'/'+name)['command']['status']=='player_panel','Atalho '+name)
 check(command(0,'/headmove')['command']['status']=='headmove','Controle de cabeça')
 for name in ['me','do','try','j','v','mask','healme','house','biz','daily']:
  check(command(0,'/'+name)['command']['status']=='not_implemented','Pendente explícito '+name)
 command(0,'/s grito-'+suffix)
 check(any(m['text']=='grito-'+suffix for m in sync(1)['messages']),'Grito entregue próximo')
 check(not any(m['text']=='grito-'+suffix for m in sync(2)['messages']),'Grito não entregue distante')
 command(0,'/w '+players[1]['uid']+' segredo-'+suffix)
 check(any(m['text']=='segredo-'+suffix for m in sync(1)['messages']),'Sussurro entregue ao alvo')
 check(not any(m['text']=='segredo-'+suffix for m in sync(2)['messages']),'Sussurro não entregue a terceiros')
 command(0,'/report Teste temporário de atendimento '+suffix)
 check(len(command(0,'/myreports')['command']['lines'])==1,'Histórico persistido')
 check(not command(1,'/myreports')['command'].get('lines'),'Histórico isolado por conta')
 check(command(0,'/report Repetido temporário',429).get('code')=='REPORT_RATE_LIMITED','Antispam de relatórios')
 print(json.dumps({'result':'PLAYER_COMMANDS_VERIFIED','checks':len(checks)},ensure_ascii=False))
finally:
 cleanup()
 print('TEMPORARY_PLAYER_COMMAND_ACCOUNTS_REMOVED')
