// Operator-only named account issuance. Credentials are read from a private
// JSON file; this module never prints passwords, hashes, tokens or DB URLs.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash,scrypt as scryptCb,timingSafeEqual} from 'node:crypto';
import {promisify} from 'node:util';
import pg from 'pg';
import {readBetaManager,hashBetaPassword} from './beta-access.mjs';
import {isBetaLoginName} from './beta-login-name.mjs';

const uuid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
function fail(code){const error=new Error(code);error.code=code;throw error;}
function exact(value,keys){return value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).every(key=>keys.includes(key));}
export function validateNamedBetaInput(input){
 if(!exact(input,['managerUid','batchId','accounts'])||!/^[1-9][0-9]{0,18}$/.test(String(input.managerUid))||!uuid(input.batchId)||
  !Array.isArray(input.accounts)||input.accounts.length<1||input.accounts.length>25)fail('INVALID_NAMED_BETA_INPUT');
 const names=new Set();
 const accounts=input.accounts.map(item=>{
  if(!exact(item,['loginName','password','label'])||!isBetaLoginName(item.loginName)||typeof item.password!=='string'||
   item.password.length<6||item.password.length>128||!item.password.trim())fail('INVALID_NAMED_BETA_ACCOUNT');
  const label=(item.label??item.loginName);
  if(typeof label!=='string')fail('INVALID_NAMED_BETA_LABEL');
  const normalized=label.normalize('NFC').trim();
  if(!normalized||Array.from(normalized).length>64||/[\p{Cc}\p{Cf}\p{Cs}]/u.test(normalized))fail('INVALID_NAMED_BETA_LABEL');
  const key=item.loginName.toLowerCase();if(names.has(key))fail('DUPLICATE_NAMED_BETA_ALIAS');names.add(key);
  return {loginName:item.loginName,password:item.password,label:normalized};
 });
 return {managerUid:String(input.managerUid),batchId:input.batchId,accounts};
}
async function matchesPassword(password,stored){
 const parts=stored?.split('$');
 if(parts?.length!==6||parts[0]!=='scrypt'||parts[1]!=='131072'||parts[2]!=='8'||parts[3]!=='1'||
  !/^[a-f0-9]{32}$/.test(parts[4])||!/^[a-f0-9]{128}$/.test(parts[5]))return false;
 const actual=await promisify(scryptCb)(password,parts[4],64,{N:131072,r:8,p:1,maxmem:192*1024*1024});
 return timingSafeEqual(actual,Buffer.from(parts[5],'hex'));
}
export async function provisionNamedBetaAccounts({pool,input}){
 const value=validateNamedBetaInput(input);
 // Only public issuance metadata is fingerprinted. Even a weak team password
 // must not leave an unsalted digest in the idempotency/audit metadata.
 const payload={kind:'named_accounts',accounts:value.accounts.map(({loginName,label})=>({loginName:loginName.toLowerCase(),label}))};
 const fingerprint=createHash('sha256').update(JSON.stringify(payload)).digest();
 const client=await pool.connect();
 try{
  await client.query('BEGIN');
  const actor=(await client.query("SELECT id,uid::text FROM hbrp.accounts WHERE uid::text=$1 AND status='active' FOR UPDATE",[value.managerUid])).rows[0];
  if(!actor)fail('BETA_MANAGER_NOT_FOUND');
  await readBetaManager(client,actor.id);
  const prior=(await client.query('SELECT action,payload_hash,result FROM hbrp.beta_access_requests WHERE actor_account_id=$1 AND request_id=$2',[actor.id,value.batchId])).rows[0];
  if(prior){
   if(prior.action!=='bootstrap_batch'||!Buffer.from(prior.payload_hash).equals(fingerprint))fail('BETA_REQUEST_CONFLICT');
   const rows=(await client.query(`SELECT a.uid::text,a.status,a.account_role,a.admin_level,b.login_name,b.label,b.password_hash,b.revoked_at
    FROM hbrp.accounts a JOIN hbrp.beta_access b ON b.account_id=a.id WHERE a.uid::text=ANY($1::text[])`,[prior.result.uids])).rows;
   const accounts=[];
   for(const item of value.accounts){
    const row=rows.find(row=>row.login_name?.toLowerCase()===item.loginName.toLowerCase());
    if(!row||row.status!=='active'||row.revoked_at)fail('NAMED_BETA_ACCESS_CHANGED');
    if(!await matchesPassword(item.password,row.password_hash))fail('NAMED_BETA_CREDENTIAL_CHANGED');
    accounts.push({uid:row.uid,loginName:row.login_name,label:row.label,accountRole:row.account_role,adminLevel:row.admin_level});
   }
   await client.query('COMMIT');return {created:0,reused:accounts.length,managerUid:actor.uid,batchId:value.batchId,accounts};
  }
  const collision=await client.query('SELECT account_id FROM hbrp.beta_access WHERE lower(login_name)=ANY($1::text[])',
   [value.accounts.map(item=>item.loginName.toLowerCase())]);
  if(collision.rowCount)fail('NAMED_BETA_ALIAS_EXISTS');
  const accounts=[];
  for(const item of value.accounts){
   const encoded=await hashBetaPassword(item.password);
   // The service role cannot INSERT privileged columns. Keep that restriction
   // and verify the existing normal-account defaults before granting beta.
   const account=(await client.query('INSERT INTO hbrp.accounts(email) VALUES(NULL) RETURNING id,uid::text,account_role,admin_level')).rows[0];
   if(account.account_role!=='user'||account.admin_level!==0)fail('UNSAFE_NEW_ACCOUNT_DEFAULTS');
   await client.query('INSERT INTO hbrp.beta_access(account_id,label,login_name,password_hash,approved_by,can_manage) VALUES($1,$2,$3,$4,$5,false)',
    [account.id,item.label,item.loginName,encoded,actor.id]);
   await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
    VALUES($1,'beta.create_named','account',$2,$3::jsonb)`,[actor.id,account.id,JSON.stringify({uid:account.uid,loginName:item.loginName,label:item.label,batchId:value.batchId})]);
   accounts.push({uid:account.uid,loginName:item.loginName,label:item.label,accountRole:'user',adminLevel:0});
  }
  await client.query(`INSERT INTO hbrp.beta_access_requests(actor_account_id,request_id,action,payload_hash,result)
   VALUES($1,$2,'bootstrap_batch',$3,$4::jsonb)`,[actor.id,value.batchId,fingerprint,JSON.stringify({uids:accounts.map(item=>item.uid)})]);
  await client.query('COMMIT');return {created:accounts.length,reused:0,managerUid:actor.uid,batchId:value.batchId,accounts};
 }catch(error){await client.query('ROLLBACK');throw error;}finally{client.release();}
}

export function readPrivateNamedBetaInput(file){
 if(!file||!path.isAbsolute(file))fail('PRIVATE_INPUT_PATH_REQUIRED');
 const descriptor=fs.openSync(file,fs.constants.O_RDONLY|(fs.constants.O_NOFOLLOW??0));
 try{
  const info=fs.fstatSync(descriptor);
  if(!info.isFile()||info.size>128*1024||(process.platform!=='win32'&&(info.mode&0o077)!==0))fail('PRIVATE_INPUT_PERMISSIONS_REQUIRED');
  return validateNamedBetaInput(JSON.parse(fs.readFileSync(descriptor,'utf8')));
 }finally{fs.closeSync(descriptor);}
}
async function main(){
 let pool;
 try{
  const args=process.argv.slice(2);
  if(args.length!==2||args[0]!=='--input'||!process.env.DATABASE_URL)fail('PRIVATE_INPUT_AND_DATABASE_ENV_REQUIRED');
  const input=readPrivateNamedBetaInput(args[1]);
  pool=new pg.Pool({connectionString:process.env.DATABASE_URL,max:1,connectionTimeoutMillis:10000});
  const result=await provisionNamedBetaAccounts({pool,input});
  console.log(JSON.stringify({event:'named_beta_provisioned',...result}));
 }catch(error){
  const code=typeof error.code==='string'&&/^[A-Z0-9_]{2,64}$/.test(error.code)?error.code:'NAMED_BETA_PROVISION_FAILED';
  console.error(JSON.stringify({event:'named_beta_provision_failed',code}));process.exitCode=1;
 }finally{if(pool)await pool.end();}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)await main();
