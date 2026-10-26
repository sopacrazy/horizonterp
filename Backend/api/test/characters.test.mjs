import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler,humanName,characterIdentity} from '../server.mjs';

test('human identity accepts NFC letters and separators; rejects game tags and protected fields',()=>{
 assert.equal(humanName('  Jose\u0301   Luís  '),'José Luís');
 assert.equal(humanName('D\u2019Ávila'),"D'Ávila");
 assert.equal(humanName('Ana-Maria'),'Ana-Maria');
 assert.equal(humanName('𐐀𐐁'),'𐐀𐐁'); // two Unicode points, not four UTF-16 units
 assert.equal(humanName('Á'.repeat(24)).length,24);
 for(const value of [null,'A','A'.repeat(25),'José123','Nome_Usado','-Maria','Ana--Maria','Ana/Clara','Ana\u200bMaria','Ana<script>'])assert.throws(()=>humanName(value));
 for(const gender of ['other','Female',null])assert.throws(()=>characterIdentity({firstName:'Ana',lastName:'Silva',gender}));
 assert.throws(()=>characterIdentity({firstName:'Ana',lastName:'Silva',gender:'female',cashCents:'999'}));
 assert.deepEqual(characterIdentity({firstName:' Jose\u0301 ',lastName:'D\u2019Ávila',gender:'male'}),{firstName:'José',lastName:"D'Ávila",gender:'male',name:"José D'Ávila"});
});

// PGlite runs actual PostgreSQL SQL in memory. Its one physical connection is
// leased for whole transactions; real multi-connection locking is also covered
// by verify_api.py on a dedicated PostgreSQL test database before deployment.
class LocalPool{
 constructor(db){this.db=db;this.tail=Promise.resolve();}
 async connect(){
  const previous=this.tail;let unlock;this.tail=new Promise(resolve=>unlock=resolve);await previous;
  return {query:(text,values)=>this.db.query(text,values),release:unlock};
 }
 async query(text,values){const client=await this.connect();try{return await client.query(text,values);}finally{client.release();}}
 async admin(fn){const client=await this.connect();try{await this.db.exec('RESET ROLE');return await fn(client);}finally{await this.db.exec('SET ROLE hbrp_app');client.release();}}
}

test('account -> character -> persisted state over local HTTP and real SQL',{timeout:120000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const filename of ['001_initial.sql','002_transfer_locking.sql','003_api.sql'])await db.exec(await fs.readFile(new URL('../../database/'+filename,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const legacyAccount=(await db.query("INSERT INTO hbrp.accounts(email) VALUES('legacy@example.invalid') RETURNING id")).rows[0].id;
  const legacyCharacter=(await db.query("INSERT INTO hbrp.characters(account_id,name) VALUES($1,'Legacy_123') RETURNING id",[legacyAccount])).rows[0].id;
  await db.query('INSERT INTO hbrp.character_stats(character_id,xp,health) VALUES($1,1234,76)',[legacyCharacter]);
  await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,z,version) SELECT $1,id,7,-4,9 FROM hbrp.worlds',[legacyCharacter]);
  await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code,customization) VALUES($1,'bybeyby','{\"legacy\":true}')",[legacyCharacter]);
  await db.query('INSERT INTO hbrp.inventories(character_id) VALUES($1)',[legacyCharacter]);
  await db.query("INSERT INTO hbrp.wallets(character_id,currency,balance) VALUES($1,'cash',4321),($1,'bank',8765)",[legacyCharacter]);
  await db.query("INSERT INTO hbrp.wallets(account_id,currency,balance) VALUES($1,'hz',21)",[legacyAccount]);
  const migration=await fs.readFile(new URL('../../database/004_character_creation.sql',import.meta.url),'utf8');
  await db.exec(migration);
  await db.exec(await fs.readFile(new URL('../../database/005_account_admin.sql',import.meta.url),'utf8'));
  await db.exec(await fs.readFile(new URL('../../database/006_player_administration.sql',import.meta.url),'utf8'));
  const legacyToken='L'.repeat(43);
  await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[legacyAccount,legacyToken]);
  await db.exec('SET ROLE hbrp_app');
  const pool=new LocalPool(db),errors=[];
  server=http.createServer(createApiHandler(pool,{log(){},errorLog(value){errors.push(value);}}));
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base='http://127.0.0.1:'+server.address().port;
  const request=async(method,path,body,token)=>{
   const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body===undefined?{}:{body:JSON.stringify(body)})});
   return {status:response.status,...await response.json()};
  };
  const pass='LocalTestPassword-Only123';let token,accountId,character;
  await t.test('health distinguishes the new API contract',async()=>{
   const health=await request('GET','/health');
   assert.deepEqual(health,{status:'ok',service:'hbrp-api',version:'0.3.0'});
  });
  await t.test('migration preserves legacy saves, funds and model without inventing identity',async()=>{
   const reply=await request('GET','/v1/player',undefined,legacyToken);
   assert.equal(reply.status,200);assert.equal(reply.needsCharacter,false);
   assert.deepEqual([reply.state.name,reply.state.firstName,reply.state.lastName,reply.state.gender],['Legacy_123',null,null,null]);
   assert.deepEqual([reply.state.x,reply.state.z,reply.state.version,reply.state.xp,reply.state.health,reply.state.cashCents,reply.state.bankCents,reply.state.hzCoins,reply.state.model],[7,-4,'9','1234',76,'4321','8765','21','bybeyby']);
   assert.deepEqual((await pool.query('SELECT customization FROM hbrp.character_appearance WHERE character_id=$1',[legacyCharacter])).rows[0].customization,{legacy:true});
  });
  await t.test('registration creates only account credentials/session, and pending account can log in',async()=>{
   const registered=await request('POST','/v1/auth/register',{email:'pending@example.invalid',password:pass});
   assert.equal(registered.status,201);assert.equal(registered.state,null);assert.equal(registered.needsCharacter,true);
   const oldToken=registered.token;
   accountId=(await pool.query("SELECT id FROM hbrp.accounts WHERE email='pending@example.invalid'")).rows[0].id;
   assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.characters WHERE account_id=$1',[accountId])).rows[0].total,0);
   assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.wallets WHERE account_id=$1',[accountId])).rows[0].total,0);
   const pending=await request('GET','/v1/player',undefined,oldToken);assert.equal(pending.needsCharacter,true);assert.equal(pending.state,null);
   const login=await request('POST','/v1/auth/login',{email:'pending@example.invalid',password:pass});
   assert.equal(login.status,200);assert.equal(login.needsCharacter,true);assert.equal(login.state,null);token=login.token;
   assert.equal((await request('GET','/v1/player',undefined,oldToken)).status,401);
   assert.equal((await request('PUT','/v1/player/profile',{firstName:'Ana',lastName:'Silva'},token)).code,'CHARACTER_REQUIRED');
   const beforeCharacterSave={requestId:randomUUID(),version:'1',x:0,y:-.1,z:0,yaw:90,cameraYaw:90,cameraPitch:16};
   assert.equal((await request('PUT','/v1/player/checkpoint',beforeCharacterSave,token)).code,'CHARACTER_REQUIRED');
  });
  await t.test('registration rejects a character name and duplicate email; creation requires session',async()=>{
   assert.equal((await request('POST','/v1/auth/register',{email:'extra@example.invalid',password:pass,name:'Old Flow'})).status,400);
   assert.equal((await request('POST','/v1/auth/register',{email:'pending@example.invalid',password:pass})).status,409);
   assert.equal((await request('POST','/v1/characters',{firstName:'Ana',lastName:'Silva',gender:'female'})).status,401);
  });
  await t.test('disabled city rolls back without character or wallets',async()=>{
   await pool.admin(client=>client.query('UPDATE hbrp.worlds SET enabled=false'));
   const reply=await request('POST','/v1/characters',{firstName:'Ana',lastName:'Silva',gender:'female'},token);
   assert.equal(reply.status,503);assert.equal(reply.code,'WORLD_UNAVAILABLE');
   assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.characters WHERE account_id=$1',[accountId])).rows[0].total,0);
   await pool.admin(client=>client.query('UPDATE hbrp.worlds SET enabled=true'));
  });
  await t.test('failure during wallet creation rolls back all character rows and dependent records',async()=>{
   await pool.admin(async()=>db.exec("CREATE FUNCTION hbrp.test_reject_wallet() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.currency='bank' THEN RAISE EXCEPTION 'isolated test failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_reject_wallet BEFORE INSERT ON hbrp.wallets FOR EACH ROW EXECUTE FUNCTION hbrp.test_reject_wallet();"));
   try{
    const reply=await request('POST','/v1/characters',{firstName:'Ana',lastName:'Silva',gender:'female'},token);
    assert.equal(reply.status,500);assert.equal(reply.code,'INTERNAL');
    assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.characters WHERE account_id=$1',[accountId])).rows[0].total,0);
    for(const table of ['character_stats','character_positions','character_appearance','inventories'])assert.equal((await pool.query(`SELECT count(*)::int AS total FROM hbrp.${table}`)).rows[0].total,1,'only the legacy fixture remains');
    assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.wallets WHERE account_id=$1',[accountId])).rows[0].total,0);
    assert.equal((await request('GET','/v1/player',undefined,token)).needsCharacter,true);
    assert.equal(JSON.parse(errors.pop()).code,'23514');
   }finally{await pool.admin(async()=>db.exec('DROP TRIGGER test_reject_wallet ON hbrp.wallets; DROP FUNCTION hbrp.test_reject_wallet();'));}
  });
  await t.test('both concurrent creation requests leave exactly one complete character',async()=>{
   const replies=await Promise.all([request('POST','/v1/characters',{firstName:'Jose\u0301',lastName:'D\u2019Ávila',gender:'female'},token),request('POST','/v1/characters',{firstName:'Outra',lastName:'Silva',gender:'male'},token)]);
   assert.deepEqual(replies.map(x=>x.status).sort(),[201,409]);
   const created=replies.find(x=>x.status===201);character=created.state;
   assert.equal(created.needsCharacter,false);assert.equal(character.name,"José D'Ávila");assert.equal(character.gender,'female');assert.equal(character.model,'bybeyby');
   for(const table of ['character_stats','character_positions','character_appearance','inventories'])assert.equal((await pool.query(`SELECT count(*)::int AS total FROM hbrp.${table} WHERE character_id=$1`,[character.characterId])).rows[0].total,1);
   assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.wallets WHERE character_id=$1',[character.characterId])).rows[0].total,2);
   assert.equal((await pool.query('SELECT count(*)::int AS total FROM hbrp.wallets WHERE account_id=$1',[accountId])).rows[0].total,1);
  });
  await t.test('checkpoint, rename, logout/login preserve character identity and saved position',async()=>{
   const save={requestId:randomUUID(),version:character.version,x:5,y:-.1,z:2,yaw:130,cameraYaw:140,cameraPitch:20};
   const saved=await request('PUT','/v1/player/checkpoint',save,token);assert.equal(saved.status,200);assert.equal(saved.state.x,5);
   assert.equal((await request('PUT','/v1/player/checkpoint',save,token)).state.version,saved.state.version);
   assert.equal((await request('PUT','/v1/player/checkpoint',{...save,x:6},token)).code,'SAVE_CONFLICT');
   const renamed=await request('PUT','/v1/player/profile',{firstName:'Maria Clara',lastName:'de Souza'},token);
   assert.equal(renamed.status,200);assert.equal(renamed.state.name,'Maria Clara de Souza');assert.equal(renamed.state.firstName,'Maria Clara');assert.equal(renamed.state.gender,'female');
   assert.equal((await request('PUT','/v1/player/profile',{name:'Nickname123'},token)).status,400);
   assert.equal((await request('POST','/v1/auth/logout',{},token)).status,200);
   assert.equal((await request('GET','/v1/player',undefined,token)).status,401);
   const login=await request('POST','/v1/auth/login',{email:'pending@example.invalid',password:pass});
   assert.equal(login.status,200);assert.equal(login.needsCharacter,false);assert.equal(login.state.characterId,character.characterId);
   assert.deepEqual([login.state.name,login.state.firstName,login.state.lastName,login.state.x,login.state.cameraYaw],['Maria Clara de Souza','Maria Clara','de Souza',5,140]);token=login.token;
  });
  await t.test('official city checkpoint and presence share bounds, preserve finances and trust the saved world only',async()=>{
   let current=(await request('GET','/v1/player',undefined,legacyToken)).state;
   const balances=(await pool.query('SELECT currency,balance::text FROM hbrp.wallets WHERE character_id=$1 OR account_id=$2 ORDER BY currency',[legacyCharacter,legacyAccount])).rows;
   const checkpoint=(point,extra={})=>({requestId:randomUUID(),version:current.version,x:0,y:-.1,z:0,yaw:90,cameraYaw:90,cameraPitch:16,...point,...extra});
   let sequence=0;
   const frame=(point,extra={})=>({clientId:'33333333-3333-4333-8333-333333333333',sequence:++sequence,x:0,y:-.1,z:0,yaw:90,speed:0,jumping:false,jumpPhase:0,...point,...extra});
   for(const point of [{x:45,y:6,z:-45},{x:700,y:30,z:-800},{x:-1000,y:-80,z:-1000},{x:1000,y:100,z:1000}]){
    const saved=await request('PUT','/v1/player/checkpoint',checkpoint(point),legacyToken);
    assert.equal(saved.status,200);current=saved.state;
    assert.deepEqual([current.x,current.y,current.z],[point.x,point.y,point.z]);
    const sync=await request('POST','/v1/multiplayer/sync',frame(point),legacyToken);
    assert.equal(sync.status,200);assert.equal(sync.room,'centro_teste');
   }
   const observer=await request('POST','/v1/multiplayer/sync',{clientId:'44444444-4444-4444-8444-444444444444',sequence:1,x:0,y:-.1,z:0,yaw:90,speed:0,jumping:false,jumpPhase:0},token);
   assert.equal(observer.status,200);
   const remote=observer.peers.find(peer=>peer.characterId===legacyCharacter);
   assert.deepEqual([remote.x,remote.y,remote.z],[1000,100,1000]);
   for(const point of [{x:-1000.001},{x:1000.001},{y:-80.001},{y:100.001},{z:-1000.001},{z:1000.001}]){
    assert.equal((await request('PUT','/v1/player/checkpoint',checkpoint(point),legacyToken)).status,400);
    assert.equal((await request('POST','/v1/multiplayer/sync',frame(point),legacyToken)).code,'INVALID_FRAME');
   }
   for(const extra of [{world:'centro_teste'},{accountId},{characterId:character.characterId},{cashCents:'999999'},{health:999}]){
    assert.equal((await request('PUT','/v1/player/checkpoint',checkpoint({},extra),legacyToken)).status,400);
    assert.equal((await request('POST','/v1/multiplayer/sync',frame({},extra),legacyToken)).code,'INVALID_FRAME');
   }
   for(const extra of [{yaw:-1},{yaw:360},{cameraYaw:360},{cameraPitch:-11},{cameraPitch:66}])assert.equal((await request('PUT','/v1/player/checkpoint',checkpoint({},extra),legacyToken)).status,400);
   const unchanged=(await request('GET','/v1/player',undefined,legacyToken)).state;
   assert.deepEqual([unchanged.x,unchanged.y,unchanged.z,unchanged.version,unchanged.health,unchanged.xp],[1000,100,1000,current.version,76,'1234']);
   assert.deepEqual((await pool.query('SELECT currency,balance::text FROM hbrp.wallets WHERE character_id=$1 OR account_id=$2 ORDER BY currency',[legacyCharacter,legacyAccount])).rows,balances);
   await pool.admin(async client=>{
    const other=(await client.query("INSERT INTO hbrp.worlds(code,scene_name) VALUES('other_world','UnconfiguredScene') RETURNING id")).rows[0].id;
    await client.query('UPDATE hbrp.character_positions SET world_id=$1 WHERE character_id=$2',[other,legacyCharacter]);
   });
   try{
    assert.equal((await request('PUT','/v1/player/checkpoint',checkpoint({}),legacyToken)).code,'WORLD_UNAVAILABLE');
    assert.equal((await request('POST','/v1/multiplayer/sync',frame({}),legacyToken)).code,'WORLD_UNAVAILABLE');
   }finally{
    await pool.admin(client=>client.query("UPDATE hbrp.character_positions SET world_id=(SELECT id FROM hbrp.worlds WHERE code='centro_teste') WHERE character_id=$1",[legacyCharacter]));
   }
  });
  await t.test('second male character, rejected duplicate name and injected fields retain isolation',async()=>{
   const second=await request('POST','/v1/auth/register',{email:'second@example.invalid',password:pass});assert.equal(second.status,201);
   const otherToken=second.token;
   assert.equal((await request('POST','/v1/characters',{firstName:'Maria Clara',lastName:'de Souza',gender:'male'},otherToken)).status,409);
   assert.equal((await request('GET','/v1/player',undefined,otherToken)).needsCharacter,true);
   assert.equal((await request('POST','/v1/characters',{firstName:'João',lastName:'Silva',gender:'male',accountId},otherToken)).status,400);
   const male=await request('POST','/v1/characters',{firstName:'João',lastName:'Silva',gender:'male'},otherToken);
   assert.equal(male.status,201);assert.equal(male.state.gender,'male');assert.equal(male.state.model,'bybeyby');assert.notEqual(male.state.accountId,accountId);
   const isolated=await request('GET','/v1/player?accountId='+accountId,undefined,otherToken);assert.equal(isolated.state.accountId,male.state.accountId);
  });
  await t.test('incomplete existing character stays a repair error instead of becoming a new-character prompt',async()=>{
   await pool.admin(client=>client.query('DELETE FROM hbrp.character_appearance WHERE character_id=$1',[character.characterId]));
   const reply=await request('GET','/v1/player',undefined,token);assert.equal(reply.status,409);assert.equal(reply.code,'CHARACTER_INCOMPLETE');
   assert.equal((await request('POST','/v1/characters',{firstName:'Outra',lastName:'Pessoa',gender:'female'},token)).code,'CHARACTER_EXISTS');
  });
  assert.deepEqual(errors,[],'no unexpected server errors');
 }finally{
  if(server)await new Promise(resolve=>server.close(resolve));
  await db.close();
 }
});
