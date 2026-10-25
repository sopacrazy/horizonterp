"""Run only on HBRP host. Mutations target exact temporary account IDs only."""
import ast,json,pathlib,uuid,secrets,concurrent.futures
source=pathlib.Path('/srv/hbrp/api/verify_api.py');prefix=[]
for node in ast.parse(source.read_text()).body:
 if isinstance(node,ast.Try):break
 prefix.append(node)
scope={};exec(compile(ast.Module(body=prefix,type_ignores=[]),str(source),'exec'),scope)
request=scope['request'];admin=scope['admin'];remember=scope['remember'];check=scope['check'];created=scope['created']

def snapshot():
 accounts=json.loads(admin("SELECT coalesce(json_agg(json_build_object('id',id,'role',account_role,'level',admin_level,'status',status)),'[]') FROM hbrp.accounts"))
 characters=json.loads(admin("SELECT coalesce(json_agg(json_build_object('id',id,'name',name,'blocked',blocked_at::text,'by',blocked_by::text,'reason',blocked_reason)),'[]') FROM hbrp.characters"))
 return ({r['id']:r for r in accounts},{r['id']:r for r in characters})

before=snapshot();players=[]
def mutate(index,action,**extra):
 target=players[index];assert target['accountId'] in created
 return request('POST','/v1/admin/players/'+target['uid'],{'characterId':target['characterId'],'action':action,**extra},players[0]['token'])

try:
 check(request('GET','/v1/admin/players')[0]==401,'Lista exige sessão autenticada')
 suffix=''.join(chr(97+int(c,16)) for c in uuid.uuid4().hex[:10])
 for i,(first,level) in enumerate([('Gestor',2),('Lucas',0),('Ribeiro',2),('Helena',3)]):
  mail=f'admin-panel-{suffix}-{i}@example.invalid';password=secrets.token_urlsafe(20)
  status,reply=request('POST','/v1/auth/register',{'email':mail,'password':password});check(status==201,'Conta de teste isolada criada')
  account=remember(mail);token=reply['token']
  status,reply=request('POST','/v1/characters',{'firstName':first,'lastName':'Painel'+suffix+chr(97+i),'gender':'male'},token)
  check(status==201,'Personagem temporário criado')
  state=reply['state'];assert state['accountId']==account
  players.append({'accountId':account,'characterId':state['characterId'],'uid':state['uid'],'token':token,'email':mail,'password':password,'lastName':state['lastName']})
  if level:admin(f"UPDATE hbrp.accounts SET account_role='admin',admin_level={level} WHERE id='{account}' AND email='{mail}'")
 check(request('GET','/v1/admin/players',token=players[1]['token'])[0]==403,'Jogador normal não acessa a lista')
 status,result=request('GET','/v1/admin/players?q='+players[1]['uid'],token=players[0]['token'])
 check(status==200 and len(result['players'])==1 and result['players'][0]['firstName']=='Lucas','Pesquisa por ID retorna somente o jogador esperado')
 check(not any(k in result['players'][0] for k in ['email','token','accountId']),'Lista não expõe credenciais ou e-mail')
 check(mutate(0,'set_admin',adminLevel=0)[1].get('code')=='SELF_MODIFICATION','Própria conta protegida')
 for i in [2,3]:check(mutate(i,'block',reason='Verificação isolada')[1].get('code')=='ADMIN_HIERARCHY','Admin de nível igual ou superior protegido')
 check(mutate(1,'set_admin',adminLevel=1)[1]['player']['adminLevel']==1,'Promoção aplicada apenas ao jogador temporário')
 with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
  rename=executor.submit(request,'PUT','/v1/player/profile',{'firstName':'Lucas','lastName':players[1]['lastName']},players[1]['token'])
  block=executor.submit(mutate,1,'block',reason='Verificação isolada')
  check(block.result()[1]['player']['blocked'] and rename.result()[0] in (200,401,403),'Bloqueio e edição de perfil concorrentes não travam nem geram erro interno')
 check(request('GET','/v1/player',token=players[1]['token'])[0]==401,'Bloqueio revoga sessão do jogador')
 check(request('POST','/v1/auth/login',{'email':players[1]['email'],'password':players[1]['password']})[1].get('code')=='CHARACTER_BLOCKED','Personagem bloqueado não consegue entrar novamente')
 check(mutate(1,'unblock')[1]['player']['blocked'] is False,'Bloqueio é reversível')
 status,reply=request('POST','/v1/auth/login',{'email':players[1]['email'],'password':players[1]['password']})
 check(status==200 and reply['state']['characterId']==players[1]['characterId'],'Desbloqueio preserva o personagem')
 check(mutate(1,'set_admin',adminLevel=0)[1]['player']['adminLevel']==0,'Remoção de admin funciona para nível inferior')
finally:
 if created:
  values=','.join("'"+str(uuid.UUID(value))+"'" for value in created)
  # Clear only fixture references before deleting the temporary moderator account.
  admin('UPDATE hbrp.characters SET blocked_at=NULL,blocked_by=NULL,blocked_reason=NULL WHERE account_id IN ('+values+')')
 scope['cleanup']()

after=snapshot()
check(all(after[i].get(key)==value for i in range(2) for key,value in before[i].items()),'Contas, permissões e personagens reais preservados')
check(admin("SELECT count(*) FROM hbrp.accounts WHERE email LIKE 'admin-panel-"+suffix+"-%@example.invalid'")=='0','Todas as contas temporárias foram removidas')
report={'result':'PLAYER_ADMIN_API_VERIFIED','checks':scope['checks'],'accountsBefore':len(before[0]),'accountsAfter':len(after[0]),'charactersBefore':len(before[1]),'charactersAfter':len(after[1]),'temporaryAccountsRemaining':0}
pathlib.Path('/srv/hbrp/api/player-admin-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False))
