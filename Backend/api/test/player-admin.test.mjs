import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';
import {scryptSync} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';

class Pool{
 constructor(db){this.db=db;this.tail=Promise.resolve();}
 async connect(){const wait=this.tail;let release;this.tail=new Promise(done=>release=done);await wait;return {query:(sql,args)=>this.db.query(sql,args),release};}
 async query(sql,args){const c=await this.connect();try{return await c.query(sql,args);}finally{c.release();}}
 async admin(fn){const c=await this.connect();try{await this.db.exec('RESET ROLE');return await fn(c);}finally{await this.db.exec('SET ROLE hbrp_app');c.release();}}
}
test('admin player management is authenticated, hierarchical, audited and reversible',{timeout:120000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of ['001_initial.sql','002_transfer_locking.sql','003_api.sql','004_character_creation.sql','005_account_admin.sql','006_player_administration.sql','007_admin_chat.sql'])
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const players=[];
  for(const [index,firstName,level] of [[1,'Adriano',3],[2,'João',0],[3,'Carla',3],[4,'Rafael',3],[5,'Bianca',1],[6,'Joana',0]]){
   const account=(await db.query('INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES($1,$2,$3) RETURNING id,uid::text',['admin-test-'+index+'@example.invalid',level?'admin':'user',level])).rows[0];
   const char=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,'Silva','male') RETURNING id",[account.id,firstName+' Silva',firstName])).rows[0];
   await db.query('INSERT INTO hbrp.character_stats(character_id,health,xp) VALUES($1,76,1234)',[char.id]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,z) SELECT $1,id,3,-2 FROM hbrp.worlds',[char.id]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[char.id]);
   const token=String(index).repeat(43);
   await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account.id,token]);
   players.push({...account,characterId:char.id,firstName,token});
  }
  const loginPassword='LocalBlockTestPassword123',salt='0123456789abcdef0123456789abcdef';
  const digest=scryptSync(loginPassword,salt,64,{N:131072,r:8,p:1,maxmem:192*1024*1024}).toString('hex');
  await db.query('INSERT INTO hbrp.password_credentials(account_id,password_hash) VALUES($1,$2)',[players[5].id,'scrypt$131072$8$1$'+salt+'$'+digest]);
  await db.exec('SET ROLE hbrp_app');const pool=new Pool(db),errors=[];
  server=http.createServer(createApiHandler(pool,{log(){},errorLog:value=>errors.push(value)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));const base='http://127.0.0.1:'+server.address().port;
  async function request(method,path,body,token){const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body?{body:JSON.stringify(body)}:{})});return {status:response.status,...await response.json()};}
  const actor=players[0],normal=players[1],peer=players[2],higher=players[3],lower=players[4],target=players[5];
  const update=(p,action,extra={},who=actor)=>request('POST','/v1/admin/players/'+p.uid,{characterId:p.characterId,action,...extra},who.token);
  await t.test('anonymous and normal accounts cannot read or mutate administration',async()=>{
   assert.equal((await request('GET','/v1/admin/players')).status,401);
   assert.equal((await request('GET','/v1/admin/players',null,normal.token)).status,403);
   assert.equal((await update(target,'set_admin',{adminLevel:1},normal)).code,'ADMIN_REQUIRED');
  });
  await t.test('search is bounded, literal, paginated and exposes no credentials',async()=>{
   const list=await request('GET','/v1/admin/players?q=jo',null,actor.token);
   assert.equal(list.status,200);assert.equal(list.total,2);
   assert.equal(list.players.every(p=>p.firstName&&!p.email&&!p.token&&!p.accountId),true);
   assert.equal((await request('GET','/v1/admin/players?q='+target.uid,null,actor.token)).players[0].uid,target.uid);
   assert.equal((await request('GET','/v1/admin/players?q=%25',null,actor.token)).total,0);
   assert.equal((await request('GET','/v1/admin/players?q='+encodeURIComponent("' OR 1=1 --"),null,actor.token)).total,0);
   assert.equal((await request('GET','/v1/admin/players?offset=-1',null,actor.token)).status,400);
   assert.equal((await request('GET','/v1/admin/players?offset=25',null,actor.token)).players.length,0);
   const all=await request('GET','/v1/admin/players',null,actor.token);
   assert.equal(all.players.find(p=>p.uid===actor.uid).canManage,false);
   assert.equal(all.players.find(p=>p.uid===peer.uid).canManage,false);
   assert.equal(all.players.find(p=>p.uid===target.uid).canManage,true);
  });
  await t.test('only lower levels may be changed and self modification is denied',async()=>{
   assert.equal((await update(actor,'set_admin',{adminLevel:0})).code,'SELF_MODIFICATION');
   assert.equal((await update(actor,'block',{reason:'Teste local'})).code,'SELF_MODIFICATION');
   for(const p of [peer,higher])assert.equal((await update(p,'block',{reason:'Teste local'})).code,'ADMIN_HIERARCHY');
   assert.equal((await update(normal,'set_admin',{adminLevel:4})).status,400);
   assert.equal((await update(normal,'set_admin',{adminLevel:1,actorAccountId:higher.id})).status,400);
   const promoted=await update(normal,'set_admin',{adminLevel:1});assert.equal(promoted.player.adminLevel,1);
   assert.equal((await request('GET','/v1/admin/players',null,normal.token)).status,200);
   assert.equal((await update(actor,'block',{reason:'Teste local'},normal)).code,'ADMIN_LEVEL_REQUIRED');
   assert.equal((await update(lower,'set_admin',{adminLevel:0})).player.accountRole,'user');
   assert.equal((await pool.query("SELECT count(*)::int AS total FROM hbrp.audit_events WHERE action='admin.set_admin'")).rows[0].total,2);
  });
  await t.test('blocking removes sessions, prevents access and preserves character progress',async()=>{
   assert.equal((await request('GET','/v1/player',null,target.token)).status,200);
   assert.equal((await update(target,'set_admin',{adminLevel:1})).status,200);
   const frame={clientId:'11111111-1111-4111-8111-111111111111',sequence:1,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0};
   assert.equal((await request('POST','/v1/multiplayer/sync',frame,target.token)).count,1);
   assert.equal((await request('POST','/v1/multiplayer/sync',frame,actor.token)).count,2);
   assert.equal((await update(target,'block',{reason:''})).code,'INVALID_REASON');
   assert.equal((await update(target,'block',{reason:'Regra de teste'})).player.blocked,true);
   assert.equal((await request('GET','/v1/player',null,target.token)).status,401);
   assert.equal((await request('POST','/v1/multiplayer/sync',{...frame,sequence:2},actor.token)).count,1);
   assert.equal((await request('POST','/v1/auth/login',{email:'admin-test-6@example.invalid',password:loginPassword})).code,'CHARACTER_BLOCKED');
   const fresh='Z'.repeat(43);
   await pool.admin(c=>c.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[target.id,fresh]));
   assert.equal((await request('GET','/v1/player',null,fresh)).code,'CHARACTER_BLOCKED');
   assert.equal((await request('POST','/v1/multiplayer/sync',{},fresh)).code,'CHARACTER_BLOCKED');
   assert.equal((await request('GET','/v1/admin/players',null,fresh)).status,403);
   const progress=(await pool.query('SELECT health::float,xp::text FROM hbrp.character_stats WHERE character_id=$1',[target.characterId])).rows[0];
   assert.equal(progress.health,76);assert.equal(progress.xp,'1234');
   assert.equal((await update(target,'unblock')).player.blocked,false);
   assert.equal((await request('GET','/v1/player',null,fresh)).state.firstName,target.firstName);
   assert.equal((await request('POST','/v1/auth/login',{email:'admin-test-6@example.invalid',password:loginPassword})).status,200);
  });
  await t.test('audit failure rolls back a role change',async()=>{
   await pool.admin(c=>c.query('REVOKE INSERT ON hbrp.audit_events FROM hbrp_app'));
   assert.equal((await update(normal,'set_admin',{adminLevel:0})).status,500);
   assert.equal((await pool.query('SELECT admin_level FROM hbrp.accounts WHERE id=$1',[normal.id])).rows[0].admin_level,1);
   await pool.admin(c=>c.query('GRANT INSERT ON hbrp.audit_events TO hbrp_app'));
   assert.equal(errors.length,1);
  });
 }finally{if(server)await new Promise(done=>server.close(done));await db.close();}
});
