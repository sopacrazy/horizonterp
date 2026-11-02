import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createPlayerAdmin} from '../player-admin.mjs';
import {createApiHandler} from '../server.mjs';

class Pool {
 constructor(db){this.db=db;this.tail=Promise.resolve();this.onAccountLock=null;}
 async connect(){
  const wait=this.tail;let release;this.tail=new Promise(done=>release=done);await wait;
  return {query:async(sql,args)=>{
   const result=await this.db.query(sql,args);
   if(this.onAccountLock&&sql.includes('ORDER BY id FOR ')&&sql.includes('hbrp.accounts')){
    const callback=this.onAccountLock;this.onAccountLock=null;await callback(this.db);
   }
   return result;
  },release};
 }
 async query(sql,args){const client=await this.connect();try{return await client.query(sql,args);}finally{client.release();}}
 async admin(fn){const client=await this.connect();try{await this.db.exec('RESET ROLE');return await fn(client);}finally{await this.db.exec('SET ROLE hbrp_app');client.release();}}
}

test('rank 4 can only promote lower accounts to admins 1 or 2, with persistent audited permissions',{timeout:180000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const file of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(file=>/^\d{3}_.*\.sql$/.test(file)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+file,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const people=[];
  const fixtures=[['Gestor','Almeida',4],['Bruno','Silva',0],['Lia','Santos',1],['Carla','Costa',2],
   ['Supervisor','Antunes',3],['Gestora','Ribeiro',4],['João','Silva',0],['João','Souza',0],
   ['José','Nunes',0],['Telma','Lima',0],['Patricia','Lopes',0],['Antigo','Supervisor',5]];
  for(let index=0;index<fixtures.length;index++){
   const [firstName,lastName,level]=fixtures[index];
   const account=(await db.query('INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES($1,$2,$3) RETURNING id,uid::text',
    ['rank-four-'+index+'@example.invalid',level?'admin':'user',level])).rows[0];
   const character=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,$4,'male') RETURNING id",
    [account.id,firstName+' '+lastName,firstName,lastName])).rows[0];
   await db.query('INSERT INTO hbrp.character_stats(character_id,health,xp) VALUES($1,76,1234)',[character.id]);
   await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,z) SELECT $1,id,3,-2 FROM hbrp.worlds',[character.id]);
   await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character.id]);
   const token=String.fromCharCode(65+index).repeat(43);
   await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account.id,token]);
   people.push({...account,characterId:character.id,firstName,lastName,token,clientId:randomUUID(),sequence:0,lastChatAt:0});
  }
  await db.exec('SET ROLE hbrp_app');
  const pool=new Pool(db),errors=[];
  const session=async(client,token)=>{
   const value=(await client.query("SELECT id,account_id FROM hbrp.sessions WHERE token_hash=sha256(convert_to($1,'UTF8')) AND revoked_at IS NULL AND expires_at>now()",[token])).rows[0];
   if(!value)throw Object.assign(new Error('Unauthenticated'),{status:401,code:'UNAUTHORIZED'});return value;
  };
  const transaction=async callback=>{const client=await pool.connect();try{await client.query('BEGIN');const value=await callback(client);await client.query('COMMIT');return value;}catch(error){await client.query('ROLLBACK');throw error;}finally{client.release();}};
  const administration=createPlayerAdmin({pool,session,transaction,removeSession(){}});
  server=http.createServer(createApiHandler(pool,{authMode:'legacy',log(){},errorLog:message=>errors.push(message)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));const base='http://127.0.0.1:'+server.address().port;
  async function request(who,method,path,body){
   const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(who?{Authorization:'Bearer '+who.token}:{})},...(body?{body:JSON.stringify(body)}:{})});
   return {status:response.status,...await response.json()};
  }
  const promote=(who,target,level)=>administration.command(who.token,{name:'adminpromov',args:target+' '+level},null);
  const role=async who=>(await pool.query('SELECT account_role,admin_level FROM hbrp.accounts WHERE id=$1',[who.id])).rows[0];
  const [manager,normal,lower,two,three,peer,joao,joaoOther,jose,concurrent,rollback,older]=people;
  const sync=who=>request(who,'POST','/v1/multiplayer/sync',{clientId:who.clientId,sequence:++who.sequence,x:3,y:0,z:-2,yaw:0,speed:0,jumping:false,jumpPhase:0});
  async function chat(who,text){
   const wait=1250-(Date.now()-who.lastChatAt);if(wait>0)await new Promise(done=>setTimeout(done,wait));
   await sync(who);who.lastChatAt=Date.now();
   return request(who,'POST','/v1/multiplayer/chat',{clientId:who.clientId,messageId:randomUUID(),text});
  }
  const progressBefore=(await pool.query('SELECT row_to_json(s) AS state FROM hbrp.character_stats s ORDER BY character_id')).rows;

  await t.test('only exact level 4 may execute the command',async()=>{
   for(const who of [normal,lower,two,three,older])await assert.rejects(promote(who,rollback.uid,1),error=>error.status===403);
   assert.equal((await request(null,'POST','/v1/multiplayer/chat',{})).status,401);
  });
  await t.test('chat promotes by ID and the existing session sees the new role immediately',async()=>{
   const result=await chat(manager,'/adminpromov '+normal.uid+' 1');
   assert.equal(result.status,200);assert.equal(result.command.name,'adminpromov');assert.equal(result.command.value,1);
   assert.deepEqual(await role(normal),{account_role:'admin',admin_level:1});
   const current=await sync(normal);assert.equal(current.selfAccountRole,'admin');assert.equal(current.selfAdminLevel,1);
  });
  await t.test('exact Unicode nicknames and complete names are normalized and case insensitive',async()=>{
   assert.equal((await promote(manager,'LIA',2)).value,2);
   assert.equal((await promote(manager,'Jose\u0301 NUNES',1)).uid,jose.uid);
   await assert.rejects(promote(manager,'João',1),error=>error.code==='AMBIGUOUS_PLAYER');
   assert.equal((await promote(manager,'joão silva',1)).uid,joao.uid);
   assert.equal((await role(joaoOther)).admin_level,0);
   await assert.rejects(promote(manager,"' OR 1=1 --",1),error=>error.code==='PLAYER_NOT_FOUND');
   await assert.rejects(promote(manager,'不存在',1),error=>error.code==='PLAYER_NOT_FOUND');
  });
  await t.test('invalid levels, self changes, hierarchy and demotion are rejected',async()=>{
   for(const level of ['0','3','4','-1','1.5','01'])await assert.rejects(promote(manager,rollback.uid,level),error=>error.status===400);
   await assert.rejects(promote(manager,manager.uid,1),error=>error.code==='SELF_MODIFICATION');
   for(const target of [three,peer,older])await assert.rejects(promote(manager,target.uid,2),error=>error.code==='ADMIN_HIERARCHY');
   await assert.rejects(promote(manager,two.uid,1),error=>error.code==='ADMIN_PROMOTION_ONLY');
   const unchanged=await promote(manager,two.uid,2);assert.equal(unchanged.value,2);assert.match(unchanged.message,/já é/);
  });
  await t.test('rank 4 cannot bypass the single-command restriction through other endpoints',async()=>{
   assert.equal((await request(manager,'GET','/v1/admin/players')).status,403);
   assert.equal((await request(manager,'POST','/v1/admin/players/'+rollback.uid,{characterId:rollback.characterId,action:'set_admin',adminLevel:1})).status,403);
   assert.equal((await request(manager,'POST','/v1/admin/players/'+rollback.uid,{characterId:rollback.characterId,action:'block',reason:'Test restriction'})).status,403);
   assert.equal((await request(manager,'POST','/v1/admin/map-teleport',{x:0,z:0})).status,403);
   assert.equal((await request(manager,'POST','/v1/police/duty',{enabled:true})).status,403);
   for(const text of ['/speed 4','/a test','/makeadmin '+rollback.uid+' 1','/helpadmin'])
    assert.equal((await chat(manager,text)).status,403,text);
   const ordinary=await chat(manager,'/stats');assert.equal(ordinary.status,200);
   assert.equal((await role(rollback)).admin_level,0);
  });
  await t.test('admin can change only their own gender, visible immediately and persisted across reconnection',async()=>{
   const originalStats=(await pool.query('SELECT row_to_json(s) AS state FROM hbrp.character_stats s WHERE character_id=$1',[normal.characterId])).rows[0];
   const originalAppearance=(await pool.query('SELECT row_to_json(a) AS state FROM hbrp.character_appearance a WHERE character_id=$1',[normal.characterId])).rows[0];
   assert.equal((await chat(manager,'/trocarsexo feminino')).status,403,'level 4 has only /adminpromov');
   assert.equal((await chat(rollback,'/trocarsexo feminino')).status,403,'regular players cannot change gender');
   assert.equal((await chat(normal,'/trocarsexo 18 feminino')).status,400,'another account cannot be targeted');
   assert.equal((await chat(normal,'/trocarsexo feminino')).command.status,'gender');
   const persisted=await request(normal,'GET','/v1/player');
   assert.equal(persisted.status,200);assert.equal(persisted.state.gender,'female');
   assert.equal(persisted.state.name,normal.firstName+' '+normal.lastName);
   assert.equal((await pool.query('SELECT gender FROM hbrp.characters WHERE id=$1',[three.characterId])).rows[0].gender,'male');
   const peer=await sync(three);
   assert.equal(peer.peers.find(item=>item.characterId===normal.characterId)?.gender,'female','online peers see the new gender without a fresh login');
   const alias=await administration.command(normal.token,{name:'trocarsexo',args:'male'},null);
   assert.equal(alias.status,'gender');assert.equal(alias.gender,'male');
   assert.equal((await request(normal,'GET','/v1/player')).state.gender,'male');
   await assert.rejects(administration.command(normal.token,{name:'trocarsexo',args:'invalid'},null),error=>error.code==='COMMAND_USAGE');
   assert.deepEqual((await pool.query('SELECT row_to_json(s) AS state FROM hbrp.character_stats s WHERE character_id=$1',[normal.characterId])).rows[0],originalStats);
   assert.deepEqual((await pool.query('SELECT row_to_json(a) AS state FROM hbrp.character_appearance a WHERE character_id=$1',[normal.characterId])).rows[0],originalAppearance);
   const audits=(await pool.query("SELECT details FROM hbrp.audit_events WHERE action='admin.trocarsexo' AND entity_id=$1 ORDER BY id",[normal.characterId])).rows;
   assert.deepEqual(audits.map(row=>row.details.gender),['female','male']);
  });
  await t.test('the ordinary administration API cannot mint the restricted rank',async()=>{
   const reply=await request(older,'POST','/v1/admin/players/'+rollback.uid,{characterId:rollback.characterId,action:'set_admin',adminLevel:4});
   assert.equal(reply.status,400);assert.equal(reply.code,'INVALID_ADMIN_LEVEL');
  });
  await t.test('switching to rank 4 cannot replay a formerly authorized cached command',async()=>{
   await sync(three);
   const body={clientId:three.clientId,messageId:randomUUID(),text:'/speed 4'};
   const original=await request(three,'POST','/v1/multiplayer/chat',body);
   assert.equal(original.status,200);assert.equal(original.command.status,'speed');
   await pool.admin(client=>client.query('UPDATE hbrp.accounts SET admin_level=4 WHERE id=$1',[three.id]));
   const replay=await request(three,'POST','/v1/multiplayer/chat',body);
   assert.equal(replay.status,403);assert.equal(replay.code,'ADMIN_ROLE_RESTRICTED');
   await pool.admin(client=>client.query('UPDATE hbrp.accounts SET admin_level=3 WHERE id=$1',[three.id]));
  });
  await t.test('concurrent promotion calls produce only permitted final levels and audit records',async()=>{
   const result=await Promise.allSettled([promote(manager,concurrent.uid,1),promote(peer,concurrent.uid,2)]);
   assert.equal(result.some(value=>value.status==='fulfilled'),true);
   assert.equal((await role(concurrent)).admin_level,2);
   const audits=(await pool.query("SELECT details FROM hbrp.audit_events WHERE action='admin.adminpromov' AND entity_id=$1",[concurrent.characterId])).rows;
   assert.equal(audits.length>=1,true);assert.equal(audits.every(row=>[1,2].includes(row.details.adminLevel)),true);
  });
  await t.test('authorization is rechecked after waiting for account locks',async()=>{
   pool.onAccountLock=database=>database.query('UPDATE hbrp.accounts SET admin_level=3 WHERE id=$1',[manager.id]);
   await assert.rejects(promote(manager,rollback.uid,1),error=>error.code==='ADMIN_LEVEL_REQUIRED');
   assert.equal((await role(rollback)).admin_level,0);
   assert.equal((await role(manager)).admin_level,4,'the injected fixture mutation rolls back with the transaction');
  });
  await t.test('audit failure rolls back promotion and all unrelated progress remains intact',async()=>{
   await pool.admin(client=>client.query('REVOKE INSERT ON hbrp.audit_events FROM hbrp_app'));
   await assert.rejects(promote(manager,rollback.uid,1));assert.equal((await role(rollback)).admin_level,0);
   await pool.admin(client=>client.query('GRANT INSERT ON hbrp.audit_events TO hbrp_app'));
   assert.deepEqual((await pool.query('SELECT row_to_json(s) AS state FROM hbrp.character_stats s ORDER BY character_id')).rows,progressBefore);
   assert.equal(errors.length,0);
  });
 }finally{if(server)await new Promise(done=>server.close(done));await db.close();}
});
