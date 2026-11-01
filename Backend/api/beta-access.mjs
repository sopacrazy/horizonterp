import {randomBytes,createHash,scrypt as scryptCb} from 'node:crypto';
import {promisify} from 'node:util';

export class BetaAccessError extends Error {
 constructor(status,code,message,details={}){super(message);this.status=status;this.code=code;this.details=details;}
}
const reject=(status,code,message,details)=>{throw new BetaAccessError(status,code,message,details);};
const uuid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const digest=value=>createHash('sha256').update(JSON.stringify(value)).digest();
export const generateBetaPassword=()=>randomBytes(18).toString('base64url');
export async function hashBetaPassword(password){
 const salt=randomBytes(16).toString('hex');
 const value=await promisify(scryptCb)(password,salt,64,{N:131072,r:8,p:1,maxmem:192*1024*1024});
 return `scrypt$131072$8$1$${salt}$${value.toString('hex')}`;
}
function exact(body,keys){
 if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(key=>!keys.includes(key)))
  reject(400,'INVALID_BETA_REQUEST','Pedido de acesso beta inválido.');
 if(!uuid(body.requestId))reject(400,'INVALID_BETA_REQUEST','Identificador do pedido inválido.');
}
function label(value){
 if(typeof value!=='string')reject(400,'INVALID_BETA_LABEL','Informe uma identificação para o acesso beta.');
 value=value.normalize('NFC').trim();
 if(!value||Array.from(value).length>64||/[\p{Cc}\p{Cf}\p{Cs}]/u.test(value))reject(400,'INVALID_BETA_LABEL','Use de 1 a 64 caracteres para identificar o acesso.');
 return value;
}
const columns=`a.uid::text,b.label,b.revoked_at,b.created_at,a.last_login_at,a.account_role,a.admin_level,
 EXISTS(SELECT 1 FROM hbrp.characters c WHERE c.account_id=a.id AND c.deleted_at IS NULL) AS has_character`;
function view(row,viewer){
 return {uid:row.uid,label:row.label,active:row.revoked_at===null,createdAt:row.created_at,lastLoginAt:row.last_login_at,
  hasCharacter:row.has_character,accountRole:row.account_role,adminLevel:row.admin_level,
  isSelf:row.uid===viewer.uid,canManage:row.uid!==viewer.uid&&row.admin_level<viewer.admin_level};
}
export async function readBetaManager(client,accountId,{bootstrap=false}={}){
 const row=(await client.query(`SELECT a.id,a.uid::text,a.account_role,a.admin_level,coalesce(b.can_manage,false) AS can_manage,b.revoked_at FROM hbrp.accounts a
  LEFT JOIN hbrp.beta_access b ON b.account_id=a.id
  WHERE a.id=$1 AND a.status='active' AND NOT EXISTS(SELECT 1 FROM hbrp.characters c WHERE c.account_id=a.id
   AND c.deleted_at IS NULL AND c.blocked_at IS NOT NULL AND (c.blocked_until IS NULL OR c.blocked_until>now()))`,[accountId])).rows[0];
 if(row?.account_role!=='admin'||row.admin_level<1||row.admin_level===4||(!bootstrap&&(!row.can_manage||row.revoked_at)))
  reject(403,'BETA_MANAGEMENT_REQUIRED','A emissão de acessos beta exige autorização administrativa específica.');
 return row;
}
async function priorRequest(client,actorId,requestId,action,payload){
 const row=(await client.query('SELECT action,payload_hash,result FROM hbrp.beta_access_requests WHERE actor_account_id=$1 AND request_id=$2',[actorId,requestId])).rows[0];
 if(!row)return null;
 if(row.action!==action||!Buffer.from(row.payload_hash).equals(digest(payload)))reject(409,'BETA_REQUEST_CONFLICT','Este pedido já foi utilizado com outros dados.');
 if(action!=='revoke')reject(409,'CREDENTIAL_ALREADY_ISSUED','As credenciais deste pedido já foram emitidas. Consulte o UID e redefina a senha para gerar outra.',{uids:row.result.uids,uid:row.result.uids?.[0]});
 return {uid:row.result.uids?.[0],revoked:true};
}
async function record(client,actorId,requestId,action,payload,accountId,uids){
 await client.query(`INSERT INTO hbrp.beta_access_requests(actor_account_id,request_id,action,payload_hash,account_id,result)
  VALUES($1,$2,$3,$4,$5,$6::jsonb)`,[actorId,requestId,action,digest(payload),accountId,JSON.stringify({uids})]);
}
async function audit(client,actorId,action,accountId,details){
 await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,$2,'account',$3,$4::jsonb)",[actorId,'beta.'+action,accountId,JSON.stringify(details)]);
}

export function createBetaAccess({pool,session,transaction,makePassword=hashBetaPassword,removeSession=()=>{}}){
 async function manager(client,token){const auth=await session(client,token);return readBetaManager(client,auth.account_id);}
 async function list(token,search='',offset=0){
  const actor=await manager(pool,token);
  if(typeof search!=='string'||Array.from(search).length>64||/[\p{Cc}\p{Cf}]/u.test(search)||!Number.isSafeInteger(offset)||offset<0||offset>1000000)
   reject(400,'INVALID_SEARCH','Pesquisa inválida.');
  search=search.normalize('NFC').trim();const pattern='%'+search.replace(/[\\%_]/g,'\\$&')+'%';
  const where="($1='' OR a.uid::text=$1 OR b.label ILIKE $2)";
  const total=(await pool.query('SELECT count(*)::int AS total FROM hbrp.beta_access b JOIN hbrp.accounts a ON a.id=b.account_id WHERE '+where,[search,pattern])).rows[0].total;
  const rows=(await pool.query('SELECT '+columns+' FROM hbrp.beta_access b JOIN hbrp.accounts a ON a.id=b.account_id WHERE '+where+' ORDER BY a.uid DESC LIMIT 25 OFFSET $3',[search,pattern,offset])).rows;
  return {accesses:rows.map(row=>view(row,actor)),total,offset,pageSize:25};
 }
 async function create(token,body){
  exact(body,['requestId','label']);const text=label(body.label);await manager(pool,token);
  const password=generateBetaPassword(),encoded=await makePassword(password);
  return transaction(async client=>{
   const initial=await manager(client,token);
   await client.query('SELECT id FROM hbrp.accounts WHERE id=$1 FOR NO KEY UPDATE',[initial.id]);
   const actor=await manager(client,token);
   await priorRequest(client,actor.id,body.requestId,'create',{label:text});
   const account=(await client.query('INSERT INTO hbrp.accounts(email) VALUES(NULL) RETURNING id,uid::text')).rows[0];
   await client.query('INSERT INTO hbrp.beta_access(account_id,label,password_hash,approved_by) VALUES($1,$2,$3,$4)',[account.id,text,encoded,actor.id]);
   await record(client,actor.id,body.requestId,'create',{label:text},account.id,[account.uid]);
   await audit(client,actor.id,'create',account.id,{uid:account.uid,label:text});
   return {access:{uid:account.uid,label:text,active:true,hasCharacter:false,accountRole:'user',adminLevel:0,canManage:true,isSelf:false},password,credentialAvailable:true};
  });
 }
 async function change(token,uid,action,body){
  if(!/^[1-9][0-9]{0,18}$/.test(uid)||!['approve','reset','revoke'].includes(action))reject(400,'INVALID_BETA_REQUEST','Acesso beta inválido.');
  exact(body,['requestId','label','reason']);
  const text=body.label===undefined?null:label(body.label);
  let reason=body.reason===undefined?'Acesso beta revogado':body.reason;
  if(typeof reason!=='string')reject(400,'INVALID_REASON','Motivo inválido.');
  reason=reason.normalize('NFC').trim();
  if(reason.length<3||Array.from(reason).length>180||/[\p{Cc}\p{Cf}]/u.test(reason))reject(400,'INVALID_REASON','Informe um motivo de 3 a 180 caracteres.');
  const initial=await manager(pool,token);
  const target=(await pool.query("SELECT id,uid::text FROM hbrp.accounts WHERE uid::text=$1 AND status='active'",[uid])).rows[0];
  if(!target)reject(404,'BETA_ACCOUNT_NOT_FOUND','Conta não encontrada.');
  if(initial.id===target.id)reject(409,'SELF_MODIFICATION','Você não pode alterar seu próprio acesso beta.');
  const password=action==='revoke'?null:generateBetaPassword(),encoded=password?await makePassword(password):null;
  const payload={uid,label:text,reason:action==='revoke'?reason:null};
  const result=await transaction(async client=>{
   await client.query('SELECT id FROM hbrp.accounts WHERE id IN($1,$2) ORDER BY id FOR NO KEY UPDATE',[initial.id,target.id]);
   const actor=await manager(client,token);
   const current=(await client.query('SELECT admin_level,status FROM hbrp.accounts WHERE id=$1',[target.id])).rows[0];
   if(current?.status!=='active')reject(404,'BETA_ACCOUNT_NOT_FOUND','Conta não encontrada.');
   if(current.admin_level>=actor.admin_level)reject(403,'ADMIN_HIERARCHY','Só é permitido gerenciar acessos de níveis inferiores.');
   const prior=await priorRequest(client,actor.id,body.requestId,action,payload);if(prior)return {...prior,revokedSessions:[]};
   const old=(await client.query('SELECT * FROM hbrp.beta_access WHERE account_id=$1 FOR UPDATE',[target.id])).rows[0];
   if(action==='approve'&&old?.revoked_at===null)reject(409,'BETA_ACCESS_EXISTS','Esta conta já possui acesso beta. Use redefinir senha.');
   if(action!=='approve'&&!old)reject(404,'BETA_ACCESS_NOT_FOUND','Esta conta ainda não tem acesso beta.');
   if(action==='revoke')await client.query("UPDATE hbrp.beta_access SET revoked_at=coalesce(revoked_at,now()),revoked_reason=$1,credential_version=credential_version+1,updated_at=now() WHERE account_id=$2",[reason,target.id]);
   else await client.query(`INSERT INTO hbrp.beta_access(account_id,label,password_hash,approved_by) VALUES($1,$2,$3,$4)
    ON CONFLICT(account_id) DO UPDATE SET label=excluded.label,password_hash=excluded.password_hash,approved_by=excluded.approved_by,
     credential_version=beta_access.credential_version+1,revoked_at=NULL,revoked_reason='',updated_at=now()`,[target.id,text||old?.label||'Equipe beta · UID '+uid,encoded,actor.id]);
   const revoked=(await client.query('UPDATE hbrp.sessions SET revoked_at=now() WHERE account_id=$1 AND revoked_at IS NULL RETURNING id',[target.id])).rows;
   await record(client,actor.id,body.requestId,action,payload,target.id,[uid]);
   await audit(client,actor.id,action,target.id,{uid,label:text||old?.label||'',reason:action==='revoke'?reason:undefined});
   return action==='revoke'?{uid,revoked:true,revokedSessions:revoked}:{access:{uid,label:text||old?.label||'Equipe beta · UID '+uid,active:true},password,credentialAvailable:true,revokedSessions:revoked};
  });
  for(const row of result.revokedSessions)removeSession(row.id);
  delete result.revokedSessions;return result;
 }
 return {list,create,change};
}

// Operator bootstrap only: no HTTP endpoint bypasses the UID/password gate.
// Existing manager credentials/saved sessions can be explicitly preserved, while
// all team accounts are new normal-user accounts with no character or progression.
export async function bootstrapBetaBatch({pool,managerUid,count=5,batchId,labelPrefix='Equipe beta'}){
 if(!/^[1-9][0-9]{0,18}$/.test(String(managerUid))||!Number.isSafeInteger(count)||count<1||count>25||!uuid(batchId))throw new Error('Invalid bootstrap parameters');
 const prefix=label(labelPrefix),credentials=[];
 for(let index=1;index<=count;index++){const password=generateBetaPassword();credentials.push({label:label(prefix+' '+String(index).padStart(2,'0')),password,encoded:await hashBetaPassword(password)});}
 const client=await pool.connect();
 try{
  await client.query('BEGIN');
  const manager=(await client.query("SELECT id,uid::text FROM hbrp.accounts WHERE uid::text=$1 AND status='active' FOR UPDATE",[String(managerUid)])).rows[0];
  if(!manager)throw new Error('Manager account not found');await readBetaManager(client,manager.id,{bootstrap:true});
  await priorRequest(client,manager.id,batchId,'bootstrap_batch',{count,labelPrefix:prefix});
  const old=(await client.query('SELECT * FROM hbrp.beta_access WHERE account_id=$1 FOR UPDATE',[manager.id])).rows[0];
  if(old?.revoked_at)throw new Error('Manager beta access was revoked; explicit recovery is required');
  if(!old){
   const password=(await client.query('SELECT password_hash FROM hbrp.password_credentials WHERE account_id=$1',[manager.id])).rows[0];
   if(!password)throw new Error('Manager has no existing password');
   await client.query('INSERT INTO hbrp.beta_access(account_id,label,password_hash,approved_by,can_manage) VALUES($1,$2,$3,$1,true)',[manager.id,'Gestão beta · UID '+manager.uid,password.password_hash]);
   await client.query('UPDATE hbrp.sessions SET beta_access_version=1 WHERE account_id=$1 AND revoked_at IS NULL',[manager.id]);
   await audit(client,manager.id,'bootstrap_manager',manager.id,{uid:manager.uid,preservedExistingCredential:true});
  }
  else if(!old.can_manage){
   await client.query('UPDATE hbrp.beta_access SET can_manage=true,updated_at=now() WHERE account_id=$1',[manager.id]);
   await audit(client,manager.id,'bootstrap_manager',manager.id,{uid:manager.uid,preservedExistingCredential:true,managementCapabilityGranted:true});
  }
  for(const credential of credentials){
   const account=(await client.query('INSERT INTO hbrp.accounts(email) VALUES(NULL) RETURNING id,uid::text')).rows[0];
   await client.query('INSERT INTO hbrp.beta_access(account_id,label,password_hash,approved_by) VALUES($1,$2,$3,$4)',[account.id,credential.label,credential.encoded,manager.id]);
   credential.uid=account.uid;delete credential.encoded;
   await audit(client,manager.id,'create',account.id,{uid:account.uid,label:credential.label,bootstrapBatch:batchId});
  }
  await record(client,manager.id,batchId,'bootstrap_batch',{count,labelPrefix:prefix},null,credentials.map(row=>row.uid));
  await client.query('COMMIT');return {managerUid:manager.uid,credentials};
 }catch(error){await client.query('ROLLBACK');throw error;}finally{client.release();}
}
