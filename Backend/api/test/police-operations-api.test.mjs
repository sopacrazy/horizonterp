import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';
import {POLICE_CELLS,POLICE_ENTRY,POLICE_HOLDING,POLICE_LEGACY_HOLDING,POLICE_STATION} from '../police-operations.mjs';

test('police workflow persists recruitment, dispatch, fines, seizure and jail across sessions',{timeout:120000},async()=>{
 const db=new PGlite();
 let server;
 let now=Date.now();
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  const folder=new URL('../../database/',import.meta.url);
  for(const name of (await fs.readdir(folder)).filter(name=>/^\d{3}_.*\.sql$/.test(name)).sort())
   await db.exec(await fs.readFile(new URL(name,folder),'utf8'));
  await db.exec('RESET ROLE');
  async function session(account,letter){
   const token=letter.repeat(43);
   await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account,token]);
   return {token,clientId:randomUUID(),sequence:0};
  }
  async function player(letter,firstName,admin=false){
   const {rows:[a]}=await db.query('INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES($1,$2,$3) RETURNING id,uid::text',
    [letter+'@example.invalid',admin?'admin':'user',admin?1:0]);
   const {rows:[c]}=await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,'Teste','male') RETURNING id",
    [a.id,firstName+' Teste',firstName]);
   await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[c.id]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[c.id]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z) SELECT $1,id,$2,$3,$4 FROM hbrp.worlds',
    [c.id,POLICE_STATION.x,POLICE_STATION.y,POLICE_STATION.z]);
   return {...a,characterId:c.id,...await session(a.id,letter)};
  }
  const staff=await player('a','Ana',true);
  const officer=await player('p','Paulo');
  const citizen=await player('c','Caio');
  const secondPrisoner=await player('d','Dora');
  const oldClient=await player('o','Olavo');
  const reconnected={...citizen,...await session(citizen.id,'r')};
  await db.query("INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code) SELECT id,$1,'comando' FROM hbrp.organizations WHERE code='pm'",[staff.characterId]);
  const {rows:[wallet]}=await db.query("INSERT INTO hbrp.wallets(character_id,currency,balance) VALUES($1,'cash',5000) RETURNING id",[citizen.characterId]);
  const {rows:[inventory]}=await db.query('INSERT INTO hbrp.inventories(character_id) VALUES($1) RETURNING id',[citizen.characterId]);
  await db.query("INSERT INTO hbrp.inventory_items(inventory_id,item_code,quantity,slot) VALUES($1,'pilot_scrap',5,0)",[inventory.id]);
  await db.exec('SET ROLE hbrp_app');
  const pool={query:(sql,args)=>db.query(sql,args),connect:async()=>({query:(sql,args)=>db.query(sql,args),release(){}})};
  const errors=[];
  server=http.createServer(createApiHandler(pool,{authMode:'legacy',combatClock:()=>now,log(){},errorLog:line=>errors.push(line)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));
  const base='http://127.0.0.1:'+server.address().port;
  async function request(user,path,body,method=body===undefined?'GET':'POST'){
   const reply=await fetch(base+path,{method,headers:{Authorization:'Bearer '+user.token,'Content-Type':'application/json'},
    body:body===undefined?undefined:JSON.stringify(body)});
   return {http:reply.status,...await reply.json()};
  }
  const move=(user,extra={})=>request(user,'/v1/multiplayer/sync',{
   clientId:user.clientId,sequence:++user.sequence,x:POLICE_STATION.x,y:POLICE_STATION.y,z:POLICE_STATION.z,
   yaw:0,speed:0,jumping:false,jumpPhase:0,prisonReady:true,...extra});
  async function send(user,text){
   now+=1300;
   await move(user);
   return request(user,'/v1/multiplayer/chat',{clientId:user.clientId,messageId:randomUUID(),text});
  }
  const command=async(user,text)=>{
   const reply=await send(user,text);
   assert.equal(reply.http,200,JSON.stringify({text,reply,errors}));
   return reply.command;
  };
  assert.equal((await send(officer,'/servico on')).http,403);
  await command(staff,'/pmrecrutar '+officer.uid);
  await command(officer,'/servico on');
  const onDuty=await move(officer);
  assert.equal(onDuty.selfPoliceDuty,true);
  assert.equal(onDuty.selfAdminLevel,0);
  assert.equal(onDuty.selfOrganization.rank,'recruta');
  assert.equal((await send(officer,'/arsenal rifle')).code,'POLICE_RANK_REQUIRED');
  await command(staff,'/pmpatente '+officer.uid+' oficial');
  assert.equal((await command(officer,'/arsenal rifle')).weapon,'rifle');
  const entrance=await command(officer,'/delegacia');
  assert.equal(entrance.status,'teleport');
  assert.equal(entrance.x,POLICE_ENTRY.x);
  await move(officer,{x:POLICE_ENTRY.x,y:POLICE_ENTRY.y,z:POLICE_ENTRY.z});
  now+=1300;
  const exit=await request(officer,'/v1/multiplayer/chat',{clientId:officer.clientId,messageId:randomUUID(),text:'/delegacia'});
  assert.equal(exit.command.status,'teleport');
  assert.equal(exit.command.x,POLICE_STATION.x);

  await command(citizen,'/190 Ajuda na delegacia');
  const dispatch=await command(officer,'/chamados');
  assert.deepEqual(dispatch.policeCalls,[{uid:citizen.uid,message:'Ajuda na delegacia',status:'open'}]);
  await command(officer,'/proc '+citizen.uid);
  const point=await command(officer,'/gpsocorrencia');
  assert.equal(point.x,POLICE_STATION.x);
  await command(officer,'/encerrar190');
  assert.equal((await command(officer,'/chamados')).policeCalls.length,0);
  await command(citizen,'/190 Chamado de teste');
  await command(citizen,'/cancelar190');
  await command(citizen,'/190 Despacho de teste');
  await command(staff,'/servico on');
  await command(staff,'/despachar190 '+citizen.uid+' '+officer.uid);
  assert.equal((await command(officer,'/chamados')).policeCalls[0].status,'accepted');
  await command(officer,'/encerrar190');

  const ticket=await command(officer,'/ticket '+citizen.uid+' 20 Estacionamento');
  const ticketId=ticket.lines[0].split(': ')[1];
  assert.equal((await send(officer,'/pagarmulta '+ticketId)).code,'TICKET_NOT_FOUND');
  assert.equal((await command(citizen,'/pagarmulta '+ticketId)).cashCents,'3000');
  await command(citizen,'/pagarmulta '+ticketId);
  assert.equal((await db.query('SELECT balance::text FROM hbrp.wallets WHERE id=$1',[wallet.id])).rows[0].balance,'3000');
  const voidTicket=await command(officer,'/ticket '+citizen.uid+' 10 Teste de cancelamento');
  await command(officer,'/cancelarmulta '+voidTicket.lines[0].split(': ')[1]);
  assert.match((await command(citizen,'/minhasmultas')).lines.join(' '),/cancelada/);

  assert.equal((await send(officer,'/revistar '+citizen.uid)).code,'NOT_YOUR_DETAINEE');
  await command(officer,'/cuff '+citizen.uid);
  assert.match((await command(officer,'/revistar '+citizen.uid)).lines[0],/5 x/);
  await command(officer,'/apreender '+citizen.uid+' pilot_scrap 2');
  assert.match((await command(officer,'/revistar '+citizen.uid)).lines[0],/3 x/);
  await command(officer,'/follow '+citizen.uid);
  await command(officer,'/delegacia');
  const escorted=await move(citizen);
  assert.equal(escorted.teleport.y,POLICE_ENTRY.y);
  await move(officer,{x:POLICE_ENTRY.x,y:POLICE_ENTRY.y,z:POLICE_ENTRY.z});
  now+=1300;
  const placed=await request(officer,'/v1/multiplayer/chat',{clientId:officer.clientId,messageId:randomUUID(),text:'/cela 2 Ocorrência registrada'});
  assert.equal(placed.http,200,JSON.stringify(placed));
  assert.match((await command(officer,'/ocorrencias '+citizen.uid)).lines[0],/em andamento/);
  const jailed=await move(reconnected,{x:100,z:100,speed:8});
  assert.ok(jailed.selfJailUntil>now);
  assert.equal(jailed.selfX,POLICE_HOLDING.x);
  assert.equal((await request(reconnected,'/v1/player')).state.x,POLICE_HOLDING.x);
  await move(secondPrisoner);
  await command(staff,'/jail '+secondPrisoner.uid+' 2 Segunda cela');
  assert.equal((await move(secondPrisoner)).selfX,POLICE_CELLS[1].x);
  await command(staff,'/unjail '+secondPrisoner.uid);
  await move(oldClient,{prisonReady:false});
  await command(staff,'/jail '+oldClient.uid+' 1 Cliente antigo');
  assert.equal((await move(oldClient,{prisonReady:false})).selfX,POLICE_LEGACY_HOLDING.x);
  await command(staff,'/unjail '+oldClient.uid);
  await command(officer,'/soltar '+citizen.uid);
  assert.equal((await move(reconnected)).selfJailUntil,0);
  await command(staff,'/jail '+citizen.uid+' 2 Pena administrativa');
  await command(staff,'/unjail '+citizen.uid);
  const freed=await move(reconnected,{x:100,z:100});
  assert.equal(freed.selfJailUntil,0);
  assert.equal(freed.selfX,100);
  await command(staff,'/jail '+citizen.uid+' 1 Pena de teste');
  await db.query("UPDATE hbrp.police_sentences SET started_at=now()-interval '2 minutes',ends_at=now()-interval '1 minute' WHERE character_id=$1 AND released_at IS NULL",[citizen.characterId]);
  assert.equal((await move(reconnected)).selfJailUntil,0);
  assert.equal((await send(officer,'/sirene')).code,'POLICE_CAR_REQUIRED');
  await command(officer,'/carpolicia');
  const car=(await move(officer)).policeCars[0];
  assert.equal((await request(officer,'/v1/police/car',{action:'enter',carId:car.id})).http,200);
  await command(officer,'/giroflex');
  await command(officer,'/sirene');
  const equipped=(await move(officer)).policeCars[0];
  assert.equal(equipped.lightbar,true);
  assert.equal(equipped.siren,true);
  await command(staff,'/servico on');
  await command(staff,'/bot1');
  await command(staff,'/cuff BOT1');
  await command(staff,'/follow BOT1');
  await command(staff,'/delegacia');
  await move(staff,{x:POLICE_ENTRY.x,y:POLICE_ENTRY.y,z:POLICE_ENTRY.z});
  now+=1300;
  const botPlaced=await request(staff,'/v1/multiplayer/chat',{clientId:staff.clientId,messageId:randomUUID(),text:'/cela 2 Teste de custódia'});
  assert.equal(botPlaced.http,200,JSON.stringify(botPlaced));
  assert.match(botPlaced.command.message,/BOT1 colocado/);
  const botInCell=(await move(staff,{x:POLICE_ENTRY.x,y:POLICE_ENTRY.y,z:POLICE_ENTRY.z})).peers.find(peer=>peer.uid==='BOT1');
  assert.equal(botInCell.y,POLICE_HOLDING.y);
  assert.equal((await command(staff,'/soltar BOT1')).status,'ok');
  await move(officer);
  await command(staff,'/pmdemitir '+officer.uid);
  assert.equal((await move(officer)).selfPoliceDuty,false);
  assert.deepEqual(errors,[]);
 }finally{
  if(server)await new Promise(resolve=>server.close(resolve));
  await db.close();
 }
});
