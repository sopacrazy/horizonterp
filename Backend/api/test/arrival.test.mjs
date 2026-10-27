import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';
import {arrivalConfig as config} from '../arrival-mission.mjs';
class Pool{
 constructor(db){this.db=db;this.tail=Promise.resolve();}
 async connect(){const wait=this.tail;let release;this.tail=new Promise(done=>release=done);await wait;return {query:(sql,args)=>this.db.query(sql,args),release};}
 async query(sql,args){const c=await this.connect();try{return await c.query(sql,args);}finally{c.release();}}
 async admin(fn){const c=await this.connect();try{await this.db.exec('RESET ROLE');return await fn(c);}finally{await this.db.exec('SET ROLE hbrp_app');c.release();}}
}
test('arrival mission has authenticated loans and exactly-once server rewards',{timeout:120000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of ['001_initial.sql','002_transfer_locking.sql','003_api.sql','004_character_creation.sql','005_account_admin.sql','006_player_administration.sql','007_admin_chat.sql'])await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');const players=[];
  for(let i=1;i<=2;i++){
   const account=(await db.query('INSERT INTO hbrp.accounts(email) VALUES($1) RETURNING id',['arrival-'+i+'@example.invalid'])).rows[0].id;
   const char=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,'Lucas '||$2,'Lucas',$2,'male') RETURNING id",[account,'Teste'+String.fromCharCode(64+i)])).rows[0].id;
   await db.query('INSERT INTO hbrp.character_stats(character_id,xp) VALUES($1,30)',[char]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id) SELECT $1,id FROM hbrp.worlds',[char]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[char]);
   await db.query("INSERT INTO hbrp.wallets(character_id,currency,balance) VALUES($1,'cash',2000),($1,'bank',5000)",[char]);
   const token=String(i).repeat(43);await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account,token]);players.push({account,char,token});
  }
  await db.exec('SET ROLE hbrp_app');const pool=new Pool(db);const errors=[];
  server=http.createServer(createApiHandler(pool,{log(){},errorLog:error=>errors.push(error)}));await new Promise(done=>server.listen(0,'127.0.0.1',done));
  const base='http://127.0.0.1:'+server.address().port;
  async function req(method,body,who=players[0]){const response=await fetch(base+'/v1/missions/arrival',{method,headers:{'Content-Type':'application/json',...(who?{Authorization:'Bearer '+who.token}:{})},...(body?{body:JSON.stringify(body)}:{})});return {status:response.status,...await response.json()};}
  let sequence=0;
  async function drive(){const response=await fetch(base+'/v1/multiplayer/sync',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+players[0].token},body:JSON.stringify({clientId:'11111111-1111-4111-8111-111111111111',sequence:++sequence,x:10,y:.3,z:0,yaw:90,speed:4,jumping:false,jumpPhase:0,driving:true})});return {status:response.status,...await response.json()};}
  async function move(point,who=players[0]){await pool.admin(c=>c.query('UPDATE hbrp.character_positions SET x=$1,y=$2,z=$3 WHERE character_id=$4',[point.x,point.y,point.z,who.char]));}
  await t.test('session and exact request shape protect the mission',async()=>{
   assert.equal((await req('GET',null,null)).status,401);
   assert.equal((await req('GET')).mission.phase,'available');
   assert.equal((await drive()).code,'MISSION_VEHICLE_UNAVAILABLE');
   assert.equal((await req('POST',{action:'start',cashCents:999999})).status,400);
   await move({x:100,y:0,z:100});assert.equal((await req('POST',{action:'start'})).code,'MISSION_TOO_FAR');
  });
  await t.test('talk/start creates only a temporary loan, resumes and isolates accounts',async()=>{
   await move(config.giver);const start=await req('POST',{action:'start'});assert.equal(start.mission.phase,'borrowed');assert.ok(start.mission.loanId);
   assert.equal(start.state.cashCents,'2000');assert.equal(start.state.xp,'30');
   assert.equal((await drive()).status,200);
   assert.equal((await req('POST',{action:'start'})).mission.loanId,start.mission.loanId);
   assert.equal((await req('GET')).mission.loanId,start.mission.loanId);
   assert.equal((await req('GET',null,players[1])).mission.phase,'available');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.player_vehicles')).rows[0].n,0);
   assert.equal((await req('POST',{action:'finish'})).code,'MISSION_TOO_FAR');
  });
  await t.test('parallel completion grants exactly 100 XP / 150 reais once and returns the car',async()=>{
   await move(config.cityHall);const replies=await Promise.all([req('POST',{action:'finish'}),req('POST',{action:'finish'})]);
   for(const reply of replies){assert.equal(reply.status,200);assert.equal(reply.mission.phase,'completed');assert.equal(reply.mission.loanId,null);assert.equal(reply.state.cashCents,'17000');assert.equal(reply.state.xp,'130');assert.equal(reply.state.bankCents,'5000');}
   assert.equal((await req('POST',{action:'start'})).mission.phase,'completed');
   assert.equal((await drive()).code,'MISSION_VEHICLE_UNAVAILABLE');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.financial_operations')).rows[0].n,1);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.wallet_entries')).rows[0].n,1);
  });
  assert.deepEqual(errors,[]);
  await t.test('expired loan cannot reward; returning to the NPC renews it',async()=>{
   const who=players[1];await move(config.giver,who);const started=await req('POST',{action:'start'},who);
   await pool.admin(c=>c.query("UPDATE hbrp.character_progress SET state=jsonb_set(state,'{loanExpiresAt}','\"2000-01-01T00:00:00Z\"') WHERE character_id=$1",[who.char]));
   assert.equal((await req('GET',null,who)).mission.phase,'available');await move(config.cityHall,who);
   assert.equal((await req('POST',{action:'finish'},who)).code,'MISSION_NOT_STARTED');await move(config.giver,who);
   const renewed=await req('POST',{action:'start'},who);assert.notEqual(renewed.mission.loanId,started.mission.loanId);assert.equal(renewed.state.cashCents,'2000');
  });
  await t.test('audit failure rolls back both XP and wallet reward',async()=>{
   const who=players[1];await move(config.cityHall,who);
   await pool.admin(c=>c.query("ALTER TABLE hbrp.audit_events ADD CONSTRAINT fail_arrival_audit CHECK(action<>'mission.arrival.finish') NOT VALID"));
   assert.equal((await req('POST',{action:'finish'},who)).status,500);
   const stats=(await pool.query("SELECT s.xp::text,w.balance::text FROM hbrp.character_stats s JOIN hbrp.wallets w ON w.character_id=s.character_id AND w.currency='cash' WHERE s.character_id=$1",[who.char])).rows[0];
   assert.deepEqual(stats,{xp:'30',balance:'2000'});assert.equal((await req('GET',null,who)).mission.phase,'borrowed');
   await pool.admin(c=>c.query('ALTER TABLE hbrp.audit_events DROP CONSTRAINT fail_arrival_audit'));
   assert.equal((await req('POST',{action:'finish'},who)).state.cashCents,'17000');assert.equal(errors.length,1);
  });
 }finally{if(server)await new Promise(done=>server.close(done));await db.close();}
});
