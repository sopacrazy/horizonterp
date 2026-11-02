import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';

test('authenticated rifle damage and5s respawn persist stats and position atomically',{timeout:120000},async()=>{
 const db=new PGlite();let server,now=Date.now();
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(n=>/^\d{3}_.*\.sql$/.test(n)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');const actors=[];
  for(let i=1;i<=2;i++){
   const account=(await db.query('INSERT INTO hbrp.accounts(email) VALUES($1) RETURNING id,uid::text',[`rifle-${i}@example.invalid`])).rows[0];
   const character=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,'Rifle','male') RETURNING id",[account.id,(i===1?'Lucas':'Breno')+' Rifle',i===1?'Lucas':'Breno'])).rows[0].id;
   await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[character]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z) SELECT $1,id,0,0,$2 FROM hbrp.worlds',[character,i===1?0:10]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character]);
   const token=String(i).repeat(43);await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account.id,token]);
   actors.push({character,uid:account.uid,token,clientId:randomUUID(),sequence:0,z:i===1?0:10});
  }
  await db.exec('SET ROLE hbrp_app');const errors=[];
  const pool={query:(sql,args)=>db.query(sql,args),connect:async()=>({query:(sql,args)=>db.query(sql,args),release(){}})};
  server=http.createServer(createApiHandler(pool,{authMode:'legacy',combatClock:()=>now,log(){},errorLog:line=>errors.push(line)}));
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const base='http://127.0.0.1:'+server.address().port;
  async function request(actor,path,body){const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json',...(actor?{Authorization:'Bearer '+actor.token}:{})},body:JSON.stringify(body)});return {status:response.status,...await response.json()};}
  const [a,b]=actors;
  const move=actor=>request(actor,'/v1/multiplayer/sync',{clientId:actor.clientId,sequence:++actor.sequence,x:0,y:0,z:actor.z,yaw:0,speed:0,jumping:false,jumpPhase:0,combatClient:true,afterRespawn:0,weaponEquipped:true,weaponAiming:true});
  let result;await move(a);await move(b);
  for(let i=0;i<4;i++){
   now+=150;await move(a);await move(b);
   result=await request(a,'/v1/multiplayer/shoot',{clientId:a.clientId,shotId:randomUUID(),targetUid:b.uid,originX:0,originY:1.3,originZ:.3,directionX:0,directionY:0,directionZ:1});
   assert.equal(result.status,200);assert.equal(result.damage,25);assert.equal(result.combatPeers[0].combat.health,Math.max(0,100-(i+1)*25));
  }
  const dead=result.combatPeers[0].combat,body={clientId:b.clientId,requestId:randomUUID(),epoch:dead.epoch,deathSequence:dead.deathSequence};
  assert.equal((await request(null,'/v1/multiplayer/respawn',body)).status,401);
  assert.equal((await request(b,'/v1/multiplayer/respawn',body)).code,'RESPAWN_WAIT');
  assert.equal((await db.query('SELECT health::float FROM hbrp.character_stats WHERE character_id=$1',[b.character])).rows[0].health,0);
  now+=5000;const revived=await request(b,'/v1/multiplayer/respawn',body);assert.equal(revived.status,200);assert.equal(revived.selfCombat.health,100);
  const stored=(await db.query('SELECT s.health::float,p.x,p.y,p.z,p.yaw FROM hbrp.character_stats s JOIN hbrp.character_positions p ON p.character_id=s.character_id WHERE s.character_id=$1',[b.character])).rows[0];
  assert.deepEqual(stored,{health:100,x:2489.3,y:12.37,z:1668.5,yaw:40});
  assert.equal((await request(b,'/v1/multiplayer/respawn',body)).selfCombat.respawnSequence,revived.selfCombat.respawnSequence);
  assert.equal(errors.length,0);
 }finally{if(server)await new Promise(resolve=>server.close(resolve));await db.close();}
});
