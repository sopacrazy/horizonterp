import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID,createHash} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';
import {bootstrapBetaBatch,hashBetaPassword} from '../beta-access.mjs';
import {verifyLiveBeta} from '../../../tools/beta-access/verify_live_beta.mjs';

class Pool{
 constructor(db){this.db=db;this.tail=Promise.resolve();}
 async connect(){const pending=this.tail;let release;this.tail=new Promise(done=>release=done);await pending;return {query:(sql,args)=>this.db.query(sql,args),release};}
 async query(sql,args){const client=await this.connect();let result;try{result=await client.query(sql,args);}finally{client.release();}if(this.afterQuery)await this.afterQuery(sql,args);return result;}
 async admin(fn){const client=await this.connect();try{await this.db.exec('RESET ROLE');return await fn(client);}finally{await this.db.exec('SET ROLE hbrp_app');client.release();}}
}

test('closed beta requires issued UID credentials and explicit active session grants',{timeout:240000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(name=>/^\d{3}_.*\.sql$/.test(name)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const managerPassword='ExistingPrivateManagerPassword123',encoded=await hashBetaPassword(managerPassword),actors=[];
  for(const [index,level] of [[1,1],[2,0],[3,4]]){
   const row=(await db.query('INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES($1,$2,$3) RETURNING id,uid::text',[`beta-old-${index}@example.invalid`,level?'admin':'user',level])).rows[0];
   const token=String(index).repeat(43);await db.query('INSERT INTO hbrp.password_credentials(account_id,password_hash) VALUES($1,$2)',[row.id,encoded]);
   await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,$2,now()+interval '1 day')",[row.id,createHash('sha256').update(token).digest()]);
   actors.push({...row,token});
  }
  await db.exec('SET ROLE hbrp_app');const pool=new Pool(db),errors=[],manager=actors[0],legacy=actors[1],rank4=actors[2];
  const batchId=randomUUID();const initial=await bootstrapBetaBatch({pool,managerUid:manager.uid,count:5,batchId});
  server=http.createServer(createApiHandler(pool,{log(){},errorLog:line=>errors.push(line),servicesEnv:{AUTH_MODE:'legacy'}}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));const base='http://127.0.0.1:'+server.address().port;
  async function request(method,path,body,token){
   const result=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body?{body:JSON.stringify(body)}:{})});
   return {...await result.json(),status:result.status};
  }
  const create=(label,requestId=randomUUID(),token=manager.token)=>request('POST','/v1/admin/beta-access',{label,requestId},token);
  const change=(uid,action,body={requestId:randomUUID()},token=manager.token)=>request('POST',`/v1/admin/beta-access/${uid}/${action}`,body,token);
  await t.test('schema and bootstrap do not authorize old accounts or create characters',async()=>{
   assert.equal(initial.credentials.length,5);assert.equal(new Set(initial.credentials.map(row=>row.uid)).size,5);
   for(const credential of initial.credentials)assert.ok(credential.password.length>=18);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.characters')).rows[0].n,0);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.beta_access')).rows[0].n,6);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts WHERE email IS NULL AND account_role=\'user\' AND admin_level=0')).rows[0].n,5);
   await assert.rejects(bootstrapBetaBatch({pool,managerUid:manager.uid,count:5,batchId}),error=>error.code==='CREDENTIAL_ALREADY_ISSUED');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,8);
  });
  await t.test('public registration and email login have no beta or environment bypass',async()=>{
   assert.deepEqual(await request('GET','/v1/auth/config'),{status:200,authMode:'beta',registrationOpen:false});
   assert.equal((await request('POST','/v1/auth/register',{email:'new@example.invalid',password:managerPassword,authMode:'legacy'})).code,'REGISTRATION_CLOSED');
   assert.equal((await request('POST','/v1/auth/login',{email:'beta-old-1@example.invalid',password:managerPassword})).status,400);
   assert.equal((await request('POST','/v1/auth/login',{uid:manager.uid,password:managerPassword,authMode:'legacy'})).status,400);
   assert.equal((await request('POST','/v1/auth/login',{uid:legacy.uid,password:managerPassword})).code,'INVALID_CREDENTIALS');
   assert.equal((await request('GET','/v1/player',undefined,legacy.token)).code,'BETA_ACCESS_REQUIRED');
  });
  await t.test('approved manager keeps its existing credential and explicitly preserved vault token',async()=>{
   const login=await request('POST','/v1/auth/login',{uid:manager.uid,password:managerPassword});
   assert.equal(login.status,200);assert.ok(login.token);assert.equal(login.account.uid,manager.uid);
   assert.equal(login.account.adminLevel,1);assert.equal(login.account.canManageBeta,true);
   assert.equal((await request('GET','/v1/player',undefined,manager.token)).status,200);
   assert.equal((await request('GET','/v1/player',undefined,manager.token)).needsCharacter,true);
  });
  await t.test('team alias and six-character beta password work without changing permanent UID',async()=>{
   const issued=initial.credentials[0],alias='user01';
   const account=(await pool.query('SELECT id FROM hbrp.accounts WHERE uid::text=$1',[issued.uid])).rows[0];
   await pool.query('UPDATE hbrp.beta_access SET login_name=$1,password_hash=$2 WHERE account_id=$3',[alias,await hashBetaPassword(alias),account.id]);
   const login=await request('POST','/v1/auth/login',{uid:alias,password:alias});
   assert.equal(login.status,200);assert.equal(login.account.uid,issued.uid);assert.equal(login.needsCharacter,true);
   assert.equal((await request('POST','/v1/auth/login',{uid:'USER01',password:alias})).status,200);
   assert.equal((await request('POST','/v1/auth/login',{uid:alias,password:'wrong1'})).code,'INVALID_CREDENTIALS');
   await pool.query('UPDATE hbrp.beta_access SET login_name=NULL,password_hash=$1 WHERE account_id=$2',[await hashBetaPassword(issued.password),account.id]);
  });
  await t.test('credential changes between password read and login locks cannot issue a refreshed session',async()=>{
   const target=initial.credentials[2];
   const before=(await pool.query('SELECT count(*)::int AS n FROM hbrp.sessions')).rows[0].n;
   const replacement=await hashBetaPassword('ReplacementPrivatePassword123');
   pool.afterQuery=async sql=>{
    if(!sql.includes('a.status,b.password_hash'))return;
    pool.afterQuery=null;
    await pool.query('UPDATE hbrp.beta_access SET password_hash=$1,credential_version=credential_version+1 WHERE account_id=(SELECT id FROM hbrp.accounts WHERE uid::text=$2)',[replacement,target.uid]);
   };
   assert.equal((await request('POST','/v1/auth/login',{uid:target.uid,password:target.password})).code,'INVALID_CREDENTIALS');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.sessions')).rows[0].n,before);
   pool.afterQuery=async sql=>{
    if(!sql.includes('a.status,b.password_hash'))return;
    pool.afterQuery=null;
    await pool.query('UPDATE hbrp.beta_access SET revoked_at=now() WHERE account_id=(SELECT id FROM hbrp.accounts WHERE uid::text=$1)',[target.uid]);
   };
   assert.equal((await request('POST','/v1/auth/login',{uid:target.uid,password:'ReplacementPrivatePassword123'})).code,'INVALID_CREDENTIALS');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.sessions')).rows[0].n,before);
  });
  let issued,login;
  await t.test('each team UID logs in separately and creates its own character after authentication',async()=>{
   issued=initial.credentials[0];login=await request('POST','/v1/auth/login',{uid:issued.uid,password:issued.password});
   assert.equal(login.status,200);assert.equal(login.state,null);assert.equal(login.needsCharacter,true);assert.equal(login.account.uid,issued.uid);
   const made=await request('POST','/v1/characters',{firstName:'Beta',lastName:'Integrante',gender:'male'},login.token);
   assert.equal(made.status,201);assert.equal(made.state.uid,issued.uid);assert.equal(made.needsCharacter,false);
   assert.equal((await request('POST','/v1/auth/login',{uid:initial.credentials[1].uid,password:issued.password})).code,'INVALID_CREDENTIALS');
  });
  await t.test('only eligible approved managers can issue or inspect credentials',async()=>{
   assert.equal((await create('Sem autenticação',randomUUID(),null)).status,401);
   assert.equal((await create('Jogador normal',randomUUID(),login.token)).code,'BETA_MANAGEMENT_REQUIRED');
   await pool.admin(client=>client.query('INSERT INTO hbrp.beta_access(account_id,label,password_hash,approved_by,can_manage) VALUES($1,$2,$3,$4,true)',[rank4.id,'Cargo 4',encoded,manager.id]));
   await pool.admin(client=>client.query('UPDATE hbrp.sessions SET beta_access_version=1 WHERE account_id=$1',[rank4.id]));
   assert.equal((await create('Cargo 4',randomUUID(),rank4.token)).code,'BETA_MANAGEMENT_REQUIRED');
   assert.equal((await request('GET','/v1/admin/beta-access',undefined,rank4.token)).status,403);
   assert.equal((await request('GET','/v1/player',undefined,rank4.token)).account.canManageBeta,false);
   const ordinaryAdmin=initial.credentials[3];
   await pool.admin(client=>client.query("UPDATE hbrp.accounts SET account_role='admin',admin_level=3 WHERE uid::text=$1",[ordinaryAdmin.uid]));
   const ordinaryLogin=await request('POST','/v1/auth/login',{uid:ordinaryAdmin.uid,password:ordinaryAdmin.password});
   assert.equal(ordinaryLogin.account.canManageBeta,false);
   assert.equal((await request('GET','/v1/admin/beta-access',undefined,ordinaryLogin.token)).code,'BETA_MANAGEMENT_REQUIRED');
   assert.equal((await change(manager.uid,'revoke')).code,'SELF_MODIFICATION');
  });
  let created;
  await t.test('panel creates normal accounts with one-time passwords and retry-safe metadata',async()=>{
   const requestId=randomUUID();created=await create('Equipe teste',requestId);
   assert.equal(created.status,201);assert.equal(created.access.hasCharacter,false);assert.equal(created.access.adminLevel,0);assert.equal(created.access.accountRole,'user');
   assert.equal(created.password.length,24);assert.equal(created.credentialAvailable,true);
   const duplicate=await create('Equipe teste',requestId);assert.equal(duplicate.code,'CREDENTIAL_ALREADY_ISSUED');assert.equal(duplicate.uid,created.access.uid);assert.equal(duplicate.password,undefined);
   assert.equal((await create('Outro rótulo',requestId)).code,'BETA_REQUEST_CONFLICT');
   const list=await request('GET','/v1/admin/beta-access',undefined,manager.token);
   assert.equal(list.status,200);assert.equal(list.accesses.find(row=>row.uid===manager.uid).canManage,false);
   assert.equal(list.accesses.find(row=>row.uid===created.access.uid).canManage,true);
   for(const item of list.accesses)assert.ok(!item.password&&!item.password_hash&&!item.token&&!item.email);
   const stored=(await pool.query('SELECT b.password_hash,r.result::text FROM hbrp.beta_access b JOIN hbrp.accounts a ON a.id=b.account_id JOIN hbrp.beta_access_requests r ON r.account_id=a.id WHERE a.uid::text=$1',[created.access.uid])).rows[0];
   assert.ok(stored.password_hash.startsWith('scrypt$'));assert.ok(!stored.password_hash.includes(created.password));assert.ok(!stored.result.includes(created.password));
  });
  await t.test('reset and revoke invalidate previous sessions and passwords without deleting progression',async()=>{
   const reset=await change(issued.uid,'reset');assert.equal(reset.status,200);assert.ok(reset.password);
   assert.equal((await request('GET','/v1/player',undefined,login.token)).status,401);
   assert.equal((await request('POST','/v1/auth/login',{uid:issued.uid,password:issued.password})).code,'INVALID_CREDENTIALS');
   const refreshed=await request('POST','/v1/auth/login',{uid:issued.uid,password:reset.password});assert.equal(refreshed.status,200);assert.equal(refreshed.state.name,'Beta Integrante');
   const revoked=await change(issued.uid,'revoke');assert.equal(revoked.status,200);assert.equal(revoked.revoked,true);
   assert.equal((await request('GET','/v1/player',undefined,refreshed.token)).status,401);
   assert.equal((await request('POST','/v1/auth/login',{uid:issued.uid,password:reset.password})).code,'INVALID_CREDENTIALS');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.characters')).rows[0].n,1);
  });
  await t.test('audit failure rolls issuance back rather than granting an unaudited account',async()=>{
   const before=(await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n;
   await pool.admin(client=>client.query('REVOKE INSERT ON hbrp.audit_events FROM hbrp_app'));
   assert.equal((await create('Falha transacional')).status,500);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,before);
   await pool.admin(client=>client.query('GRANT INSERT ON hbrp.audit_events TO hbrp_app'));
   assert.equal(errors.length,1);
  });
  await t.test('production smoke verifier removes every tracked fixture after its real API checks',async()=>{
   const before=(await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n;
   server.removeAllListeners('request');server.on('request',createApiHandler(pool,{log(){},errorLog:line=>errors.push(line)}));
   const adminPool={query:(sql,args)=>pool.admin(client=>client.query(sql,args)),async connect(){const client=await pool.connect();await client.query('RESET ROLE');return {query:client.query,async release(){await client.query('SET ROLE hbrp_app');client.release();}};}};
   const result=await verifyLiveBeta({pool:adminPool,request,hashPassword:hashBetaPassword});
   assert.equal(result.result,'BETA_ACCESS_VERIFIED',JSON.stringify(result));
   assert.equal(result.cleanup.remaining,0);assert.equal(result.cleanup.complete,true);
   assert.equal(result.temporaryAccountsTracked,5);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,before);
   const failingRequest=(method,path,...args)=>path==='/v1/characters'?Promise.resolve({status:500}):request(method,path,...args);
   const failed=await verifyLiveBeta({pool:adminPool,request:failingRequest,hashPassword:hashBetaPassword});
   assert.equal(failed.result,'BETA_ACCESS_FAILED');assert.equal(failed.failure.check,'separate_character_creation');
   assert.equal(failed.cleanup.complete,true);assert.equal(failed.cleanup.remaining,0);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,before);
  });
 }finally{if(server)await new Promise(done=>server.close(done));await db.close();}
});
