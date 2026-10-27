"""HTTPS integration using only one disposable mission account, with ledger cleanup."""
import ast,json,pathlib,uuid,secrets
source=pathlib.Path('/srv/hbrp/api/verify_api.py');prefix=[]
for node in ast.parse(source.read_text()).body:
 if isinstance(node,ast.Try):break
 prefix.append(node)
scope={};exec(compile(ast.Module(body=prefix,type_ignores=[]),str(source),'exec'),scope)
request=scope['request'];admin=scope['admin'];check=scope['check'];created=scope['created']
config=json.loads(pathlib.Path('/srv/hbrp/api/arrival-config.json').read_text())
before=int(admin('SELECT count(*) FROM hbrp.accounts'));suffix=uuid.uuid4().hex[:10];mail='arrival-'+suffix+'@example.invalid'
try:
 check(request('GET','/v1/missions/arrival')[0]==401,'Missão exige sessão autenticada')
 status,reply=request('POST','/v1/auth/register',{'email':mail,'password':secrets.token_urlsafe(24)})
 check(status==201,'Conta temporária criada');scope['remember'](mail);token=reply['token']
 letters=''.join(chr(97+int(c,16)) for c in suffix)
 status,reply=request('POST','/v1/characters',{'firstName':'Lucas','lastName':'Missao'+letters,'gender':'male'},token)
 check(status==201,'Personagem temporário criado');state=reply['state']
 def save(point):
  global state
  status,reply=request('PUT','/v1/player/checkpoint',dict(requestId=str(uuid.uuid4()),version=state['version'],yaw=90,cameraYaw=90,cameraPitch=14,**point),token)
  check(status==200,'Posição da fixture salva pela API');state=reply['state']
 check(request('GET','/v1/missions/arrival',token=token)[1]['mission']['phase']=='available','Missão disponível sem recompensa antecipada')
 sequence=0
 def drive():
  global sequence
  sequence+=1
  return request('POST','/v1/multiplayer/sync',dict(clientId='11111111-1111-4111-8111-111111111111',sequence=sequence,x=state['x'],y=state['y'],z=state['z'],yaw=90,speed=0,jumping=False,jumpPhase=0,driving=True),token)
 check(drive()[1].get('code')=='MISSION_VEHICLE_UNAVAILABLE','Multiplayer recusa carro sem empréstimo ativo')
 check(request('POST','/v1/missions/arrival',{'action':'start','rewardXp':99999},token)[0]==400,'Cliente não escolhe a recompensa')
 save(dict(x=200,y=0,z=200));check(request('POST','/v1/missions/arrival',{'action':'start'},token)[1]['code']=='MISSION_TOO_FAR','Início exige proximidade do NPC')
 save(config['giver']);status,reply=request('POST','/v1/missions/arrival',{'action':'start'},token)
 check(status==200 and reply['mission']['phase']=='borrowed','NPC concede empréstimo temporário');loan=reply['mission']['loanId']
 check(drive()[0]==200,'Multiplayer aceita veículo com empréstimo ativo')
 check(request('POST','/v1/missions/arrival',{'action':'start'},token)[1]['mission']['loanId']==loan,'Repetir início não duplica empréstimo')
 check(request('GET','/v1/missions/arrival',token=token)[1]['mission']['loanId']==loan,'Empréstimo persiste entre consultas')
 check(admin("SELECT count(*) FROM hbrp.player_vehicles WHERE character_id='"+state['characterId']+"'")=='0','Carro não vira propriedade permanente')
 save(config['cityHall']);status,reply=request('POST','/v1/missions/arrival',{'action':'finish'},token)
 check(status==200 and reply['mission']['phase']=='completed' and reply['mission']['loanId'] is None,'Prefeitura conclui missão e devolve carro')
 check(reply['state']['xp']=='100' and reply['state']['cashCents']=='15000','Recompensa exata de 100 XP e R$ 150')
 check(drive()[1].get('code')=='MISSION_VEHICLE_UNAVAILABLE','Devolução revoga autorização do veículo no multiplayer')
 status,again=request('POST','/v1/missions/arrival',{'action':'finish'},token)
 check(status==200 and again['state']['xp']=='100' and again['state']['cashCents']=='15000','Repetir conclusão não repete dinheiro ou XP')
 check(request('GET','/v1/missions/arrival',token=token)[1]['mission']['phase']=='completed','Conclusão permanece salva no personagem')
finally:
 for account in created:
  assert str(uuid.UUID(account))==account
  wallets="SELECT id FROM hbrp.wallets WHERE account_id='"+account+"' OR character_id IN(SELECT id FROM hbrp.characters WHERE account_id='"+account+"')"
  admin("BEGIN; DELETE FROM hbrp.wallet_entries WHERE wallet_id IN ("+wallets+"); DELETE FROM hbrp.financial_operations WHERE source_wallet IN ("+wallets+") OR destination_wallet IN ("+wallets+"); DELETE FROM hbrp.character_progress WHERE character_id IN(SELECT id FROM hbrp.characters WHERE account_id='"+account+"'); COMMIT;")
 scope['cleanup']()
check(int(admin('SELECT count(*) FROM hbrp.accounts'))==before,'Contagem de contas reais preservada')
check(admin("SELECT count(*) FROM hbrp.accounts WHERE email='"+mail+"'")=='0','Conta temporária removida, incluindo recompensa e histórico da fixture')
report={'result':'ARRIVAL_API_VERIFIED','checks':scope['checks'],'accountsBefore':before,'temporaryAccountsRemaining':0}
pathlib.Path('/srv/hbrp/api/arrival-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False))
