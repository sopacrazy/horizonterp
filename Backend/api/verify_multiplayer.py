"""VPS integration checks. --prepare-clients retains only isolated fixtures for Unity;
--cleanup always removes those exact fixture accounts. No tokens/passwords are printed."""
import ast,json,pathlib,sys,uuid,secrets,time,concurrent.futures
source=pathlib.Path('/srv/hbrp/api/verify_api.py')
tree=ast.parse(source.read_text());prefix=[]
for node in tree.body:
 if isinstance(node,ast.Try):break
 prefix.append(node)
scope={}
exec(compile(ast.Module(body=prefix,type_ignores=[]),str(source),'exec'),scope)
request=scope['request'];admin=scope['admin'];remember=scope['remember'];cleanup=scope['cleanup']
store=pathlib.Path('/srv/hbrp/deployments/multiplayer-fixtures.json')
if '--cleanup' in sys.argv:
 if store.exists():
  data=json.loads(store.read_text());scope['created'].extend(data['accountIds']);cleanup();store.unlink()
 print(json.dumps({'result':'MULTIPLAYER_FIXTURES_REMOVED'}));sys.exit(0)
checks=[];accounts=[];keep=False
def check(value,message):
 if not value:raise AssertionError(message)
 checks.append(message)
def frame(index,sequence,**extra):
 return dict(clientId=accounts[index]['clientId'],sequence=sequence,x=-2 if index==0 else 2,y=-.1,z=0,
             yaw=90 if index==0 else 270,speed=0,jumping=False,jumpPhase=0,**extra)
def send(index,sequence,updates=None):
 body=frame(index,sequence);body.update(updates or {})
 return request('POST','/v1/multiplayer/sync',body,accounts[index]['token'])
def leave(index):
 return request('POST','/v1/multiplayer/leave',{'clientId':accounts[index]['clientId']},accounts[index]['token'])
try:
 check(request('POST','/v1/multiplayer/sync',{})[0]==401,'Sala exige sessão autenticada')
 suffix=''.join(chr(97+int(c,16)) for c in uuid.uuid4().hex[:10])
 for index in range(3):
  mail=f'mp-{suffix}-{index}@example.invalid';password=secrets.token_urlsafe(20)
  code,reply=request('POST','/v1/auth/register',{'email':mail,'password':password});check(code==201,'Conta temporária criada')
  account=remember(mail);token=reply['token']
  accounts.append({'email':mail,'password':password,'token':token,'accountId':account,'clientId':str(uuid.uuid4()),'role':index})
  if index==0:check(request('POST','/v1/multiplayer/sync',{},token)[0]==409,'Conta sem personagem não entra na sala')
  code,reply=request('POST','/v1/characters',{'firstName':'Teste','lastName':'Rede'+suffix+chr(97+index),'gender':'male' if index!=1 else 'female'},token)
  check(code==201,'Personagem de teste criado');accounts[-1]['characterId']=reply['state']['characterId']
  body={'requestId':str(uuid.uuid4()),'version':reply['state']['version'],'x':-2 if index==0 else 2,'y':-.1,'z':0,
        'yaw':90 if index==0 else 270,'cameraYaw':90 if index==0 else 270,'cameraPitch':14}
  check(request('PUT','/v1/player/checkpoint',body,token)[0]==200,'Spawn de teste persistido')
 check(send(0,1)[1]['count']==1,'Primeiro jogador entra sozinho')
 code,reply=send(1,1);check(code==200 and reply['count']==2 and reply['peers'][0]['characterId']==accounts[0]['characterId'],'Segundo jogador enxerga o primeiro')
 code,reply=send(0,2);check(code==200 and reply['peers'][0]['characterId']==accounts[1]['characterId'],'Primeiro jogador enxerga o segundo')
 check(send(2,1)[1].get('code')=='ROOM_FULL','Terceiro jogador recebe sala cheia')
 check(send(0,1)[1].get('code')=='STALE_FRAME','Pacotes antigos não substituem o movimento atual')
 check(send(0,3,{'clientId':str(uuid.uuid4())})[1].get('code')=='SESSION_IN_USE','Dois jogos não controlam a mesma conta')
 check(send(0,3,{'cashCents':'999'})[0]==400,'Movimento não aceita alterações de saldo')
 check(send(0,3,{'x':100})[0]==400,'Posição fora do mapa é recusada')
 check(send(1,2,{'x':2.5,'speed':4.5,'jumping':True,'jumpPhase':.4})[0]==200,'Corrida e pulo são aceitos')
 peer=send(0,3)[1]['peers'][0]
 check(peer['x']==2.5 and peer['speed']==4.5 and peer['jumping'] and peer['jumpPhase']==.4,'Movimento, corrida e fase do pulo chegam ao outro cliente')
 check('token' not in peer and 'sessionId' not in peer and 'accountId' not in peer and 'email' not in peer,'Snapshot não expõe sessão ou dados da conta')
 message={'clientId':accounts[0]['clientId'],'messageId':str(uuid.uuid4()),'text':'Olá, João! <b>Texto literal</b>'}
 check(request('POST','/v1/multiplayer/chat',message)[0]==401,'Chat exige autenticação')
 code,sent=request('POST','/v1/multiplayer/chat',message,accounts[0]['token'])
 check(code==200 and sent['message']['text']==message['text'],'Chat recebe texto Unicode da conta conectada')
 check(request('POST','/v1/multiplayer/chat',message,accounts[0]['token'])[1]['message']['id']==sent['message']['id'],'Reenvio da mesma mensagem não duplica')
 altered=dict(message,messageId=str(uuid.uuid4()))
 check(request('POST','/v1/multiplayer/chat',altered,accounts[0]['token'])[1].get('code')=='CHAT_RATE_LIMITED','Chat limita mensagens repetidas em sequência')
 for updates in [{'text':''},{'text':'a'*181},{'text':'linha\nnova'},{'name':'Nome falso'}]:
  check(request('POST','/v1/multiplayer/chat',dict(message,**updates),accounts[0]['token'])[0]==400,'Chat rejeita texto inválido ou identidade forjada')
 snapshot=send(1,3)[1]
 check(snapshot['messages'][0]['text']==message['text'] and snapshot['messages'][0]['name'].startswith('Teste '),'Outro jogador recebe a mensagem e o nome autenticado')
 check('sessionId' not in snapshot['messages'][0] and 'accountId' not in snapshot['messages'][0],'Mensagem não expõe credenciais ou ID privado')
 check(send(1,4,{'afterChat':sent['message']['id']})[1]['messages']==[],'Cursor evita repetir mensagens recebidas')
 leave(0);check(send(1,5)[1]['count']==1,'Saída remove o personagem da sala')
 check(request('POST','/v1/multiplayer/chat',message,accounts[0]['token'])[1].get('code')=='NOT_IN_ROOM','Jogador que saiu não consegue enviar para a sala')
 leave(1)
 with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:codes=list(executor.map(lambda i:send(i,10)[0],range(3)))
 check(sorted(codes)==[200,200,409],'Entradas concorrentes respeitam duas vagas')
 for i in range(3):leave(i)
 send(0,20);send(1,20);time.sleep(10.2)
 check(send(2,20)[1]['count']==1,'Conexão perdida libera a vaga após timeout')
 for i in range(3):leave(i)
 if '--prepare-clients' in sys.argv:
  store.write_text(json.dumps({'accountIds':scope['created'],'clients':[{k:a[k] for k in ['email','password','role']} for a in accounts[:2]]}));store.chmod(0o600);keep=True
 report={'result':'MULTIPLAYER_API_VERIFIED','checks':checks,'fixture_accounts_retained':len(accounts) if keep else 0}
 pathlib.Path('/srv/hbrp/api/multiplayer-validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
finally:
 if not keep:cleanup()
