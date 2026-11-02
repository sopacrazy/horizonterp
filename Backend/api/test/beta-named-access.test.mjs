import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';
import {hashBetaPassword} from '../beta-access.mjs';
import {isBetaLoginName,isBetaLoginIdentifier} from '../beta-login-name.mjs';
import {provisionNamedBetaAccounts,validateNamedBetaInput} from '../provision_named_beta_accounts.mjs';

class Pool{
 constructor(db){this.db=db;this.tail=Promise.resolve();}
 async connect(){const pending=this.tail;let release;this.tail=new Promise(done=>release=done);await pending;return {query:(sql,args)=>this.db.query(sql,args),release};}
 async query(sql,args){const client=await this.connect();try{return await client.query(sql,args);}finally{client.release();}}
}
test('named beta aliases validate without shadowing numeric UIDs',()=>{
 for(const alias of ['fixture_admin','fixture_dev','user01','Ab3','A'.repeat(32)]){
  assert.equal(isBetaLoginName(alias),true);assert.equal(isBetaLoginIdentifier(' '+alias+' '),true);
 }
 for(const alias of ['ab','A'.repeat(33),'Fixture Admin','Ádmin','_Admin','12Admin','123','name@host.invalid','a/b','abc\n'])assert.equal(isBetaLoginName(alias),false,alias);
 assert.equal(isBetaLoginIdentifier('18'),true);assert.equal(isBetaLoginIdentifier('00018'),false);
 assert.throws(()=>validateNamedBetaInput({managerUid:'18',batchId:randomUUID(),accounts:[
  {loginName:'Admin_Test',password:'654321'},{loginName:'admin_test',password:'654321'}]}),{code:'DUPLICATE_NAMED_BETA_ALIAS'});
 assert.throws(()=>validateNamedBetaInput({managerUid:'18',batchId:randomUUID(),accounts:[
  {loginName:'Admin_Test',password:'654321',adminLevel:3}]}),{code:'INVALID_NAMED_BETA_ACCOUNT'});
});

test('operator named issuance is atomic, retry-safe, secret-free and never grants roles',{timeout:240000},async t=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(name=>/^\d{3}_.*\.sql$/.test(name)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const manager=(await db.query("INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES('named-manager@example.invalid','admin',3) RETURNING id,uid::text")).rows[0];
  await db.query('INSERT INTO hbrp.beta_access(account_id,label,password_hash,can_manage) VALUES($1,$2,$3,true)',[manager.id,'Named account operator',await hashBetaPassword('FixtureManagerPassword')]);
  await db.exec('SET ROLE hbrp_app');const pool=new Pool(db),errors=[];
  const input={managerUid:manager.uid,batchId:randomUUID(),accounts:[
   {loginName:'fixture_admin',password:'654321'}, {loginName:'fixture_dev',password:'654321'},
   ...Array.from({length:12},(_,index)=>({loginName:'Fixture_'+String(index+1).padStart(2,'0'),password:'654321'}))]};
  let issued;
  await t.test('fourteen aliases create normal accounts and no characters',async()=>{
   issued=await provisionNamedBetaAccounts({pool,input});
   assert.equal(issued.created,14);assert.equal(issued.reused,0);assert.equal(new Set(issued.accounts.map(account=>account.uid)).size,14);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,15);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.characters')).rows[0].n,0);
   for(const account of issued.accounts){assert.equal(account.accountRole,'user');assert.equal(account.adminLevel,0);}
   assert.ok(!JSON.stringify(issued).includes('654321'));assert.ok(!JSON.stringify(issued).includes('scrypt$'));
   const metadata=(await pool.query('SELECT result::text FROM hbrp.beta_access_requests')).rows.map(row=>row.result).join('');
   const audits=(await pool.query('SELECT details::text FROM hbrp.audit_events')).rows.map(row=>row.details).join('');
   assert.ok(!metadata.includes('654321')&&!audits.includes('654321'));
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.beta_access WHERE can_manage=true')).rows[0].n,1);
  });
  await t.test('retry returns exactly the same UIDs without changing hashes or sessions',async()=>{
   const before=(await pool.query('SELECT a.uid::text,b.password_hash,b.credential_version::text FROM hbrp.accounts a JOIN hbrp.beta_access b ON b.account_id=a.id ORDER BY a.uid')).rows;
   const repeat=await provisionNamedBetaAccounts({pool,input});assert.equal(repeat.created,0);assert.equal(repeat.reused,14);
   assert.deepEqual(repeat.accounts,issued.accounts);
   assert.deepEqual((await pool.query('SELECT a.uid::text,b.password_hash,b.credential_version::text FROM hbrp.accounts a JOIN hbrp.beta_access b ON b.account_id=a.id ORDER BY a.uid')).rows,before);
   await assert.rejects(provisionNamedBetaAccounts({pool,input:{...input,accounts:[...input.accounts,{loginName:'Extra_User',password:'654321'}]}}),{code:'BETA_REQUEST_CONFLICT'});
   await assert.rejects(provisionNamedBetaAccounts({pool,input:{...input,accounts:input.accounts.map((account,index)=>index?account:{...account,password:'111111'})}}),{code:'NAMED_BETA_CREDENTIAL_CHANGED'});
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,15);
  });
  await t.test('case-insensitive alias collision rolls back a whole different batch',async()=>{
   await assert.rejects(provisionNamedBetaAccounts({pool,input:{...input,batchId:randomUUID(),accounts:[
    {loginName:'Would_Be_New',password:'654321'},{loginName:'FIXTURE_admin',password:'654321'}]}}),{code:'NAMED_BETA_ALIAS_EXISTS'});
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.accounts')).rows[0].n,15);
  });
  server=http.createServer(createApiHandler(pool,{log(){},errorLog:line=>errors.push(line)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));const base='http://127.0.0.1:'+server.address().port;
  async function request(method,path,body,token){
   const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body?{body:JSON.stringify(body)}:{})});
   return {...await response.json(),status:response.status};
  }
  let token;
  await t.test('mixed-case named login and original numeric UID log in the same normal account',async()=>{
   const account=issued.accounts[0];
   for(const uid of ['fixture_admin','fIxTuRe_aDmIn',account.uid]){
    const result=await request('POST','/v1/auth/login',{uid,password:'654321'});
    assert.equal(result.status,200);assert.equal(result.account.uid,account.uid);assert.equal(result.account.accountRole,'user');
    assert.equal(result.account.adminLevel,0);assert.equal(result.account.canManageBeta,false);assert.equal(result.needsCharacter,true);token=result.token;
   }
   const sessions=(await pool.query('SELECT count(*)::int AS n FROM hbrp.sessions WHERE account_id=(SELECT id FROM hbrp.accounts WHERE uid::text=$1)',[account.uid])).rows[0].n;
   await provisionNamedBetaAccounts({pool,input});
   assert.equal((await request('GET','/v1/player',undefined,token)).status,200);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.sessions WHERE account_id=(SELECT id FROM hbrp.accounts WHERE uid::text=$1)',[account.uid])).rows[0].n,sessions);
   assert.equal((await request('POST','/v1/auth/login',{uid:account.loginName,password:'111111'})).code,'INVALID_CREDENTIALS');
   assert.equal((await request('POST','/v1/auth/login',{uid:'Not_Issued',password:'654321'})).code,'INVALID_CREDENTIALS');
  });
  await t.test('aliases preserve registration closure and administrative authorization',async()=>{
   assert.equal((await request('POST','/v1/auth/register',{uid:'Another_Admin',password:'654321'})).code,'REGISTRATION_CLOSED');
   assert.equal((await request('GET','/v1/admin/beta-access',undefined,token)).code,'BETA_MANAGEMENT_REQUIRED');
   for(const uid of ['ab','A'.repeat(33),'Fixture Admin','Ádmin','_Admin'])assert.equal((await request('POST','/v1/auth/login',{uid,password:'654321'})).code,'INVALID_UID');
   await assert.rejects(provisionNamedBetaAccounts({pool,input:{...input,managerUid:issued.accounts[0].uid,batchId:randomUUID()}}),error=>error.code==='BETA_MANAGEMENT_REQUIRED');
  });
  await t.test('revoked named access cannot log in or be silently reactivated by a retry',async()=>{
   await pool.query('UPDATE hbrp.beta_access SET revoked_at=now(),credential_version=credential_version+1 WHERE account_id=(SELECT id FROM hbrp.accounts WHERE uid::text=$1)',[issued.accounts[0].uid]);
   assert.equal((await request('POST','/v1/auth/login',{uid:'fixture_admin',password:'654321'})).code,'INVALID_CREDENTIALS');
   assert.equal((await request('GET','/v1/player',undefined,token)).status,401);
   await assert.rejects(provisionNamedBetaAccounts({pool,input}),{code:'NAMED_BETA_ACCESS_CHANGED'});
   assert.equal(errors.length,0);
  });
 }finally{if(server)await new Promise(done=>server.close(done));await db.close();}
});
