import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';

test('combat HTTP route authenticates the actor and persists server damage without accepting forged health',{timeout:120000},async()=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(n=>/^\d{3}_.*\.sql$/.test(n)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const actors=[];
  for(let i=1;i<=2;i++){
   const account=(await db.query('INSERT INTO hbrp.accounts(email) VALUES($1) RETURNING id',[`melee-${i}@example.invalid`])).rows[0].id;
   const firstName=i===1?'Lucas':'Breno';
   const character=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,'Combate','male') RETURNING id",[account,firstName+' Combate',firstName])).rows[0].id;
   await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[character]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z) SELECT $1,id,0,0,$2 FROM hbrp.worlds',[character,i===1?0:1.5]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character]);
   const token=String(i).repeat(43);
   await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account,token]);
   actors.push({account,character,token,clientId:randomUUID(),sequence:0,z:i===1?0:1.5});
  }
  await db.exec('SET ROLE hbrp_app');
  const pool={query:(sql,args)=>db.query(sql,args),connect:async()=>({query:(sql,args)=>db.query(sql,args),release(){}})},errors=[];
  server=http.createServer(createApiHandler(pool,{authMode:'legacy',log(){},errorLog:line=>errors.push(line)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));
  const base='http://127.0.0.1:'+server.address().port;
  async function request(actor,path,body,method='POST'){
   const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(actor?{Authorization:'Bearer '+actor.token}:{})},...(body?{body:JSON.stringify(body)}:{})});
   return {status:response.status,...await response.json()};
  }
  const [a,b]=actors,action={clientId:a.clientId,attackId:randomUUID()};
  const move=(actor,combatClient=true)=>request(actor,'/v1/multiplayer/sync',{clientId:actor.clientId,sequence:++actor.sequence,x:0,y:0,z:actor.z,yaw:0,speed:0,jumping:false,jumpPhase:0,...(combatClient===undefined?{}:{combatClient})});
  assert.equal((await request(null,'/v1/multiplayer/attack',action)).status,401);
  assert.equal((await request(a,'/v1/multiplayer/attack',action)).code,'NOT_IN_ROOM');
  const capability=await move(a,false);assert.equal(capability.combatAvailable,true);
  assert.equal((await request(a,'/v1/multiplayer/attack',action)).code,'COMBAT_CLIENT_REQUIRED');
  await move(a,true);await move(b,true);
  assert.equal((await request(a,'/v1/multiplayer/attack',{...action,health:0})).code,'INVALID_ATTACK');
  const hit=await request(a,'/v1/multiplayer/attack',action);assert.equal(hit.status,200);assert.equal(hit.damage,20);
  assert.equal(hit.combatPeers[0].combat.health,80);
  const retry=await request(a,'/v1/multiplayer/attack',action);assert.equal(retry.status,200);assert.deepEqual(retry.selfCombat,hit.selfCombat);
  let stored=await request(b,'/v1/player',undefined,'GET');assert.equal(stored.state.health,80);
  const checkpoint=await request(b,'/v1/player/checkpoint',{requestId:randomUUID(),version:stored.state.version,x:0,y:0,z:1.5,yaw:0,cameraYaw:0,cameraPitch:20},'PUT');
  assert.equal(checkpoint.status,200);assert.equal(checkpoint.state.health,80,'checkpoint cannot replace authoritative combat health');
  const observed=await move(b);assert.equal(observed.selfCombat.health,80);assert.equal(observed.peers[0].combat.attackId,action.attackId);
  assert.equal((await db.query('SELECT health::float FROM hbrp.character_stats WHERE character_id=$1',[b.character])).rows[0].health,80);
  // A fresh API instance obtains persisted damage from the existing character row.
  server.removeAllListeners('request');server.on('request',createApiHandler(pool,{authMode:'legacy',log(){},errorLog:line=>errors.push(line)}));
  const rejoined=await move(b);assert.equal(rejoined.selfCombat.health,80);
  assert.equal(errors.length,0);
 }finally{
  if(server)await new Promise(done=>server.close(done));
  await db.close();
 }
});
