import {createPlayerCommands} from './player-commands.mjs';
import https from 'node:https';
import fs from 'node:fs';
import { randomBytes, randomUUID, createHash, scrypt as scryptCb, timingSafeEqual } from 'node:crypto';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';
import pg from 'pg';
import {createRoom,RoomError,POLICE_COMMANDS,SERVICE_COMMANDS,POLICE_PUBLIC,POLICE_OPERATIONS} from './multiplayer.mjs';
import {createPlayerAdmin,AdminError} from './player-admin.mjs';
import {getWorldBounds,isPositionWithinWorld} from './world-bounds.mjs';
import {createArrivalMission,ArrivalError,arrivalConfig} from './arrival-mission.mjs';
import {createPoliceCommands} from './police-commands.mjs';
import {createPoliceOperations} from './police-operations.mjs';
import {createRealtimeAccess,RealtimeError} from './realtime-access.mjs';
import {createBetaAccess,BetaAccessError} from './beta-access.mjs';
import {isBetaLoginIdentifier} from './beta-login-name.mjs';
import {createCriminalEconomy,createCriminalEconomyRouteHandler,CriminalEconomyError} from './criminal-economy.mjs';

const scrypt=promisify(scryptCb);
const hash=x=>createHash('sha256').update(x).digest();
const uuid=x=>typeof x==='string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x);
class ApiError extends Error{constructor(status,message,code='INVALID_REQUEST'){super(message);this.status=status;this.code=code;}}
const fail=(status,message,code)=>{throw new ApiError(status,message,code);};
function exact(body,keys){if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!keys.includes(k)))fail(400,'Dados inválidos.');}
function email(value){if(typeof value!=='string'||value.length>254||! /^[^\s@]{1,64}@[^\s@]+\.[^\s@]+$/.test(value.trim()))fail(400,'Informe um e-mail válido.');return value.trim().toLowerCase();}
export function humanName(value,label='Nome'){
 if(typeof value!=='string')fail(400,`Informe ${label.toLowerCase()}.`);
 value=value.normalize('NFC').trim().replace(/\s+/gu,' ').replace(/\u2019/gu,"'");
 const size=Array.from(value).length;
 if(size<2||size>24||!/^\p{L}[\p{L}\p{M}]*(?:[ '-]\p{L}[\p{L}\p{M}]*)*$/u.test(value))fail(400,`${label}: use de 2 a 24 caracteres, com letras, espaços, apóstrofo ou hífen.`);
 return value;
}
export function characterIdentity(body){
 exact(body,['firstName','lastName','gender']);
 const firstName=humanName(body.firstName,'Nome'),lastName=humanName(body.lastName,'Sobrenome');
 if(body.gender!=='female'&&body.gender!=='male')fail(400,'Selecione feminino ou masculino.');
 return {firstName,lastName,gender:body.gender,name:firstName+' '+lastName};
}
function password(value){if(typeof value!=='string'||value.length<10||value.length>128||!value.trim())fail(400,'A senha precisa ter entre 10 e 128 caracteres.');return value;}
export function createApiHandler(pool,{log=console.log,errorLog=console.error,servicesEnv=process.env,authMode='beta',combatClock=Date.now}={}){
if(!['beta','legacy'].includes(authMode))throw new Error('Invalid authentication mode');
const minimumClientVersionCode=Number(servicesEnv.HBRP_MIN_CLIENT_VERSION_CODE||0);
if(!Number.isSafeInteger(minimumClientVersionCode)||minimumClientVersionCode<0)throw new Error('Invalid minimum client version');
// Production invokes this factory without an override. Legacy mode exists only
// as an explicit code-level fixture option, never an HTTP or environment switch.
const betaRequired=authMode==='beta';
const room=createRoom({capacity:10,civilianCars:true,clock:combatClock,persistDamage:async({characterId,damage})=>{
 // Combat owns health. Checkpoints only store position and cannot undo a hit.
 const result=await pool.query(`UPDATE hbrp.character_stats SET health=GREATEST(0,health-$2),
  version=version+1,updated_at=now() WHERE character_id=$1 RETURNING health::float`,[characterId,damage]);
 if(!result.rowCount)fail(409,'Dados do personagem incompletos.','CHARACTER_INCOMPLETE');
 return result.rows[0].health;
},persistRespawn:async({characterId,world,x,y,z,yaw})=>transaction(async client=>{
 const result=await client.query(`UPDATE hbrp.character_stats SET health=100,version=version+1,updated_at=now()
  WHERE character_id=$1 AND health<=0 RETURNING health`,[characterId]);
 if(!result.rowCount)fail(409,'O personagem já foi recuperado.','RESPAWN_STALE');
 const position=await client.query(`UPDATE hbrp.character_positions SET x=$2,y=$3,z=$4,yaw=$5,camera_yaw=$5,
  version=version+1,updated_at=now() WHERE character_id=$1 AND world_id=(SELECT id FROM hbrp.worlds WHERE code=$6) RETURNING character_id`,[characterId,x,y,z,yaw,world]);
 if(!position.rowCount)fail(409,'Posição do personagem indisponível.','CHARACTER_INCOMPLETE');
})});
const realtime=createRealtimeAccess({room,env:servicesEnv});
let followerReady;
function loadFollower(){
 return followerReady??=pool.query(`SELECT f.owner_character_id AS owner_id,c.id AS bot_id,
  a.uid::text AS uid,c.name,c.first_name,v.model_code,s.health::float
  FROM hbrp.follow_test_bots f JOIN hbrp.characters c ON c.id=f.bot_character_id AND c.deleted_at IS NULL
  JOIN hbrp.accounts a ON a.id=c.account_id AND a.status='active'
  LEFT JOIN hbrp.character_appearance v ON v.character_id=c.id
  JOIN hbrp.character_stats s ON s.character_id=c.id
  WHERE f.enabled=true LIMIT 1`).then(({rows})=>{
   if(rows[0])room.setFollowerBot({ownerCharacterId:rows[0].owner_id,characterId:rows[0].bot_id,
    uid:rows[0].uid,name:rows[0].name,firstName:rows[0].first_name,model:rows[0].model_code,health:rows[0].health});
  });
}
const arrival=createArrivalMission({pool,session,transaction,requireCharacter});
const administration=createPlayerAdmin({pool,session,transaction,removeSession:id=>room.removeSession(id)});
const police=createPoliceCommands({pool,transaction,room});
const operations=createPoliceOperations({pool,transaction,room,setDuty});
async function setDuty(identity,sessionId,body){
 const result=room.duty(identity,sessionId,body);
 if(!body?.enabled||identity.organization?.code==='pm')return {...result,organization:identity.organization};
 try{
  // Legacy administrators may still join through their existing panel.
  await pool.query(`INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code)
   SELECT id,$1,'oficial' FROM hbrp.organizations WHERE code='pm'
   ON CONFLICT(character_id) DO NOTHING`,[identity.characterId]);
  const current=await requireCharacter(pool,identity.accountId);
  if(current.organization?.code!=='pm')fail(409,'O personagem pertence a outra organização.','ORGANIZATION_CONFLICT');
  return {...room.duty(current,sessionId,body),organization:current.organization};
 }catch(error){room.revokePoliceDuty(identity.characterId);throw error;}
}
const playerCommands=createPlayerCommands({pool,transaction,room});
const betaAccess=createBetaAccess({pool,session,transaction,makePassword,removeSession:id=>room.removeSession(id)});
const criminalEconomy=createCriminalEconomy({pool,transaction,getPresence:identity=>{
 const member=room.findOnline(identity.uid);
 if(!member||member.characterId!==identity.characterId||Date.now()-member.seen>10000)return null;
 return {x:member.x,y:member.y,z:member.z,world:member.world||identity.world,driving:member.driving===true};
}});
const handleCriminalEconomy=createCriminalEconomyRouteHandler(criminalEconomy);
let cryptoJobs=0;
async function derive(pass,salt){
 if(cryptoJobs>=2)fail(429,'Aguarde alguns segundos e tente novamente.','BUSY');
 cryptoJobs++;
 try{return await scrypt(pass,salt,64,{N:131072,r:8,p:1,maxmem:192*1024*1024});}finally{cryptoJobs--;}
}
async function makePassword(pass){const salt=randomBytes(16).toString('hex');return `scrypt$131072$8$1$${salt}$${(await derive(pass,salt)).toString('hex')}`;}
async function checkPassword(pass,stored){
 const parts=stored?.split('$');const valid=parts?.length===6&&parts[0]==='scrypt'&&parts[1]==='131072'&&parts[2]==='8'&&parts[3]==='1'&&/^[a-f0-9]{32}$/.test(parts[4])&&/^[a-f0-9]{128}$/.test(parts[5]);
 const actual=await derive(pass,valid?parts[4]:'00000000000000000000000000000000');
 return valid&&timingSafeEqual(actual,Buffer.from(parts[5],'hex'));
}
const limits=new Map();
function rate(ip,group,max,windowMs){
 const key=group+':'+ip;const now=Date.now();let bucket=limits.get(key);
 if(!bucket||bucket.until<now){bucket={count:0,until:now+windowMs};limits.set(key,bucket);}
 if(++bucket.count>max)fail(429,'Muitas tentativas. Aguarde para tentar novamente.','RATE_LIMITED');
}
setInterval(()=>{for(const [key,value] of limits)if(value.until<Date.now())limits.delete(key);},60000).unref();
async function transaction(fn){const client=await pool.connect();try{await client.query('BEGIN');const result=await fn(client);await client.query('COMMIT');return result;}catch(e){await client.query('ROLLBACK');throw e;}finally{client.release();}}
async function session(client,token,lock=false){
 if(typeof token!=='string'||! /^[A-Za-z0-9_-]{43}$/.test(token))fail(401,'Entre novamente na sua conta.','UNAUTHORIZED');
 const result=await client.query(`SELECT s.id,s.account_id,s.last_checkpoint_at${betaRequired?',s.beta_access_version::text,b.credential_version::text AS permitted_beta_version,b.revoked_at AS beta_revoked_at':''}
  FROM hbrp.sessions s JOIN hbrp.accounts a ON a.id=s.account_id
  ${betaRequired?'LEFT JOIN hbrp.beta_access b ON b.account_id=a.id':''}
  WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND a.status='active' ${lock?'FOR UPDATE OF s':''}`,[hash(token)]);
 if(!result.rowCount)fail(401,'Sua sessão expirou ou foi encerrada. Entre novamente.','UNAUTHORIZED');
 if(betaRequired&&(!result.rows[0].permitted_beta_version||result.rows[0].beta_revoked_at||result.rows[0].beta_access_version!==result.rows[0].permitted_beta_version))
  fail(401,'Este acesso não está autorizado para a equipe beta. Solicite um UID e senha emitidos pelo painel.','BETA_ACCESS_REQUIRED');
 return result.rows[0];
}
async function issueSession(client,accountId,betaVersion=null){
 const token=randomBytes(32).toString('base64url');
 if(betaRequired){
  if(!betaVersion)fail(401,'Acesso beta não autorizado.','BETA_ACCESS_REQUIRED');
  await client.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at,beta_access_version) VALUES($1,$2,now()+interval '30 days',$3)",[accountId,hash(token),betaVersion]);
 }else await client.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,$2,now()+interval '30 days')",[accountId,hash(token)]);
 await client.query('UPDATE hbrp.accounts SET last_login_at=now() WHERE id=$1',[accountId]);return token;
}
async function state(client,accountId){
 const result=await client.query(`SELECT a.id AS account_id,a.uid::text,a.account_role,a.admin_level,${betaRequired?"a.account_role='admin' AND (a.admin_level=1 OR (a.admin_level>1 AND a.admin_level<>4 AND coalesce(ba.can_manage,false) AND ba.revoked_at IS NULL))":"false"} AS can_manage_beta,c.id AS character_id,c.name,c.first_name,c.last_name,c.gender,c.blocked_at,c.blocked_until,c.blocked_reason,
 s.character_id AS stats_id,p.character_id AS position_id,v.character_id AS appearance_id,
 s.health::float,s.energy::float,s.hunger::float,s.thirst::float,s.level,s.xp::text,s.play_seconds::text,
 p.x,p.y,p.z,p.yaw,p.camera_yaw,p.camera_pitch,p.version::text,w.code AS world,
 v.model_code,org.code AS organization_code,org.name AS organization_name,org.rank_code AS organization_rank,
 coalesce((SELECT (extract(epoch FROM max(j.ends_at))*1000)::bigint FROM hbrp.police_sentences j
  WHERE j.character_id=c.id AND j.released_at IS NULL AND j.ends_at>now()),0)::text AS jail_until,
 coalesce((SELECT balance FROM hbrp.wallets WHERE character_id=c.id AND currency='cash'),0)::text AS cash_cents,
 coalesce((SELECT balance FROM hbrp.wallets WHERE character_id=c.id AND currency='bank'),0)::text AS bank_cents,
 coalesce((SELECT balance FROM hbrp.wallets WHERE account_id=a.id AND currency='hz'),0)::text AS hz_coins
 FROM hbrp.accounts a LEFT JOIN hbrp.characters c ON c.account_id=a.id AND c.deleted_at IS NULL
 ${betaRequired?'LEFT JOIN hbrp.beta_access ba ON ba.account_id=a.id':''}
 LEFT JOIN hbrp.character_stats s ON s.character_id=c.id LEFT JOIN hbrp.character_positions p ON p.character_id=c.id
 LEFT JOIN hbrp.worlds w ON w.id=p.world_id LEFT JOIN hbrp.character_appearance v ON v.character_id=c.id
 LEFT JOIN LATERAL (SELECT o.code,o.name,m.rank_code FROM hbrp.organization_members m
  JOIN hbrp.organizations o ON o.id=m.organization_id WHERE m.character_id=c.id LIMIT 1) org ON true
 WHERE a.id=$1`,[accountId]);
 if(!result.rowCount)fail(401,'Entre novamente na sua conta.','UNAUTHORIZED');
 const r=result.rows[0];
 if(!r.character_id)return null;
 if(r.blocked_at&&(!r.blocked_until||new Date(r.blocked_until).getTime()>Date.now()))
  fail(403,'Este personagem está bloqueado. Motivo: '+(r.blocked_reason||'Procure a administração.'),'CHARACTER_BLOCKED');
 if(!r.stats_id||!r.position_id||!r.appearance_id||!r.world)fail(409,'Dados do personagem incompletos. Procure o suporte.','CHARACTER_INCOMPLETE');
 return {accountId:r.account_id,uid:r.uid,accountRole:r.account_role,adminLevel:r.admin_level,canManageBeta:r.can_manage_beta,characterId:r.character_id,name:r.name,firstName:r.first_name,lastName:r.last_name,gender:r.gender,organization:r.organization_code?{code:r.organization_code,name:r.organization_name,rank:r.organization_rank}:null,health:r.health,energy:r.energy,hunger:r.hunger,thirst:r.thirst,level:r.level,xp:r.xp,playSeconds:r.play_seconds,cashCents:r.cash_cents,bankCents:r.bank_cents,hzCoins:r.hz_coins,model:r.model_code,world:r.world,version:r.version,jailUntil:Number(r.jail_until),x:r.x,y:r.y,z:r.z,yaw:r.yaw,cameraYaw:r.camera_yaw,cameraPitch:r.camera_pitch};
}
async function playerReply(client,accountId){const current=await state(client,accountId);const account=(await client.query(`SELECT a.uid::text,a.account_role AS "accountRole",a.admin_level AS "adminLevel",${betaRequired?"a.account_role='admin' AND (a.admin_level=1 OR (a.admin_level>1 AND a.admin_level<>4 AND coalesce(b.can_manage,false) AND b.revoked_at IS NULL))":"false"} AS "canManageBeta" FROM hbrp.accounts a ${betaRequired?'LEFT JOIN hbrp.beta_access b ON b.account_id=a.id':''} WHERE a.id=$1`,[accountId])).rows[0];return {state:current,needsCharacter:current===null,account};}
async function requireCharacter(client,accountId){const current=await state(client,accountId);if(!current)fail(409,'Crie seu personagem antes de entrar na cidade.','CHARACTER_REQUIRED');return current;}
async function register(body){
 if(betaRequired)fail(403,'O cadastro público está fechado. Use o UID e a senha emitidos pelo painel da equipe beta.','REGISTRATION_CLOSED');
 exact(body,['email','password']);const mail=email(body.email),pass=password(body.password);
 const encoded=await makePassword(pass);
 return transaction(async client=>{
  const account=(await client.query('INSERT INTO hbrp.accounts(email) VALUES($1) RETURNING id',[mail])).rows[0].id;
  await client.query("INSERT INTO hbrp.auth_identities(account_id,provider,provider_subject) VALUES($1,'email',$2)",[account,mail]);
  await client.query('INSERT INTO hbrp.password_credentials(account_id,password_hash) VALUES($1,$2)',[account,encoded]);
  await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id) VALUES($1,'auth.register','account',$1::uuid::text)",[account]);
  const token=await issueSession(client,account);return {token,...await playerReply(client,account)};
 });
}
async function login(body){
 if(betaRequired)return betaLogin(body);
 exact(body,['email','password']);const mail=email(body.email),pass=password(body.password);
 const result=await pool.query('SELECT a.id,a.status,p.password_hash FROM hbrp.accounts a JOIN hbrp.password_credentials p ON p.account_id=a.id WHERE lower(a.email)=$1',[mail]);
 const account=result.rows[0];const valid=await checkPassword(pass,account?.password_hash);
 if(!valid||account.status!=='active')fail(401,'E-mail ou senha inválidos.','INVALID_CREDENTIALS');
 return transaction(async client=>{
  const active=await client.query("SELECT id FROM hbrp.accounts WHERE id=$1 AND status='active' FOR UPDATE",[account.id]);
  if(!active.rowCount)fail(401,'E-mail ou senha inválidos.','INVALID_CREDENTIALS');
  const token=await issueSession(client,account.id);return {token,...await playerReply(client,account.id)};
 });
}
async function betaLogin(body){
 exact(body,['uid','password']);
 if(!isBetaLoginIdentifier(body.uid))fail(400,'Informe seu UID ou usuário fornecido pela equipe.','INVALID_UID');
 const uid=body.uid.trim(),pass=typeof body.password==='string'&&body.password.length>=6&&body.password.length<=128?body.password:fail(400,'A senha precisa ter entre 6 e 128 caracteres.');
 const result=await pool.query(`SELECT a.id,a.status,b.password_hash,b.credential_version::text,b.revoked_at
  FROM hbrp.accounts a LEFT JOIN hbrp.beta_access b ON b.account_id=a.id WHERE a.uid::text=$1 OR lower(b.login_name)=lower($1)`,[uid]);
 const account=result.rows[0];const valid=await checkPassword(pass,account?.password_hash);
 if(!valid||account.status!=='active'||account.revoked_at||!account.credential_version)
  fail(401,'UID ou senha inválidos, ou acesso beta não autorizado.','INVALID_CREDENTIALS');
 return transaction(async client=>{
  const active=await client.query("SELECT id FROM hbrp.accounts WHERE id=$1 AND status='active' FOR UPDATE",[account.id]);
  if(!active.rowCount)fail(401,'UID ou senha inválidos, ou acesso beta não autorizado.','INVALID_CREDENTIALS');
  const grant=(await client.query('SELECT password_hash,credential_version::text,revoked_at FROM hbrp.beta_access WHERE account_id=$1 FOR UPDATE',[account.id])).rows[0];
  if(!grant||grant.revoked_at||grant.password_hash!==account.password_hash||grant.credential_version!==account.credential_version)
   fail(401,'As credenciais foram alteradas. Use o UID e senha atuais.','INVALID_CREDENTIALS');
  const token=await issueSession(client,account.id,grant.credential_version);return {token,...await playerReply(client,account.id)};
 });
}
async function createCharacter(body,token){
 const identity=characterIdentity(body);
 return transaction(async client=>{
  const auth=await session(client,token);
  // Match login's account -> session lock order, then revalidate revocation.
  const owner=await client.query("SELECT id FROM hbrp.accounts WHERE id=$1 AND status='active' FOR UPDATE",[auth.account_id]);
  if(!owner.rowCount)fail(401,'Entre novamente na sua conta.','UNAUTHORIZED');
  await session(client,token,true);
  const existing=await client.query('SELECT id FROM hbrp.characters WHERE account_id=$1 AND deleted_at IS NULL',[auth.account_id]);
  if(existing.rowCount)fail(409,'Esta conta já possui um personagem.','CHARACTER_EXISTS');
  const world=await client.query("SELECT id FROM hbrp.worlds WHERE code='centro_teste' AND enabled");
  if(!world.rowCount)fail(503,'A cidade está indisponível. Tente novamente mais tarde.','WORLD_UNAVAILABLE');
  const character=(await client.query('INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,$4,$5) RETURNING id',[auth.account_id,identity.name,identity.firstName,identity.lastName,identity.gender])).rows[0].id;
  await client.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[character]);
  await client.query('INSERT INTO hbrp.character_positions(character_id,world_id) VALUES($1,$2)',[character,world.rows[0].id]);
  // Both selections use the currently imported 3D model; gender remains explicit metadata.
  await client.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character]);
  await client.query('INSERT INTO hbrp.inventories(character_id) VALUES($1)',[character]);
  await client.query("INSERT INTO hbrp.wallets(character_id,currency) VALUES($1,'cash'),($1,'bank')",[character]);
  await client.query("INSERT INTO hbrp.wallets(account_id,currency) VALUES($1,'hz') ON CONFLICT(account_id,currency) WHERE account_id IS NOT NULL DO NOTHING",[auth.account_id]);
  await client.query('UPDATE hbrp.sessions SET last_checkpoint_at=now() WHERE id=$1',[auth.id]);
  await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id) VALUES($1,'character.create','character',$2)",[auth.account_id,character]);
  return playerReply(client,auth.account_id);
 });
}
async function checkpoint(body,token){
 exact(body,['requestId','version','x','y','z','yaw','cameraYaw','cameraPitch']);
 if(!uuid(body.requestId)||! /^[1-9][0-9]{0,17}$/.test(String(body.version)))fail(400,'Salvamento inválido.');
 for(const key of ['x','y','z','yaw','cameraYaw','cameraPitch'])if(typeof body[key]!=='number'||!Number.isFinite(body[key]))fail(400,'Posição inválida.');
 if(body.yaw<0||body.yaw>=360||body.cameraYaw<0||body.cameraYaw>=360||body.cameraPitch< -10||body.cameraPitch>65)fail(400,'Posição fora da área de teste.');
 const digest=hash(JSON.stringify([String(body.version),body.x,body.y,body.z,body.yaw,body.cameraYaw,body.cameraPitch]));
 return transaction(async client=>{
  const auth=await session(client,token,true);
  const current=await requireCharacter(client,auth.account_id);
  if(current.jailUntil>Date.now())return {state:current};
  if(!getWorldBounds(current.world))fail(409,'Esta área não está disponível para salvamento.','WORLD_UNAVAILABLE');
  if(!isPositionWithinWorld(current.world,body))fail(400,'Posição fora da área de teste.');
  const previous=await client.query('SELECT payload_hash FROM hbrp.save_requests WHERE session_id=$1 AND request_id=$2',[auth.id,body.requestId]);
  if(previous.rowCount){if(!timingSafeEqual(previous.rows[0].payload_hash,digest))fail(409,'Pedido repetido com dados diferentes.','SAVE_CONFLICT');return {state:await state(client,auth.account_id)};}
  if(current.version!==String(body.version))fail(409,'Os dados foram atualizados em outra sessão. Entre novamente.','SAVE_CONFLICT');
  const result=await client.query(`UPDATE hbrp.character_positions SET x=$1,y=$2,z=$3,yaw=$4,camera_yaw=$5,camera_pitch=$6,version=version+1,updated_at=now() WHERE character_id=$7 AND version=$8 RETURNING version::text`,[body.x,body.y,body.z,body.yaw,body.cameraYaw,body.cameraPitch,current.characterId,body.version]);
  if(!result.rowCount)fail(409,'Os dados foram atualizados. Entre novamente.','SAVE_CONFLICT');
  await client.query("UPDATE hbrp.character_stats SET play_seconds=play_seconds+LEAST(120,GREATEST(0,floor(extract(epoch FROM(now()-$1::timestamptz)))))::bigint,version=version+1,updated_at=now() WHERE character_id=$2",[auth.last_checkpoint_at,current.characterId]);
  await client.query('UPDATE hbrp.sessions SET last_checkpoint_at=now() WHERE id=$1',[auth.id]);
  await client.query('INSERT INTO hbrp.save_requests(session_id,request_id,character_id,payload_hash,resulting_version) VALUES($1,$2,$3,$4,$5)',[auth.id,body.requestId,current.characterId,digest,result.rows[0].version]);
  return {state:await state(client,auth.account_id)};
 });
}
async function rename(body,token){
 exact(body,['name','firstName','lastName']);
 let firstName,lastName;
 if('name' in body){
  if('firstName' in body||'lastName' in body||typeof body.name!=='string')fail(400,'Envie nome e sobrenome.');
  const full=body.name.normalize('NFC').trim().replace(/\s+/gu,' '),space=full.indexOf(' ');
  if(space<0)fail(400,'Informe nome e sobrenome.');
  firstName=humanName(full.slice(0,space),'Nome');lastName=humanName(full.slice(space+1),'Sobrenome');
 }else{firstName=humanName(body.firstName,'Nome');lastName=humanName(body.lastName,'Sobrenome');}
 const display=firstName+' '+lastName;
 return transaction(async client=>{const auth=await session(client,token,true);const current=await requireCharacter(client,auth.account_id);
  await client.query('UPDATE hbrp.characters SET name=$1,first_name=$2,last_name=$3 WHERE id=$4',[display,firstName,lastName,current.characterId]);
  await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id) VALUES($1,'character.rename','character',$2)",[auth.account_id,current.characterId]);
  return {state:await state(client,auth.account_id)};});
}
async function readBody(req){
 if(!String(req.headers['content-type']||'').startsWith('application/json'))fail(415,'Envie dados em JSON.');
 const chunks=[];let size=0;
 for await(const chunk of req){size+=chunk.length;if(size>4096)fail(413,'Pedido muito grande.');chunks.push(chunk);}
 try{return JSON.parse(Buffer.concat(chunks).toString('utf8'));}catch{fail(400,'JSON inválido.');}
}
return async(req,res)=>{
 const started=Date.now(),requestId=randomUUID();let status=500,path='/';
 const send=(code,value)=>{status=code;res.writeHead(code,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff','X-Request-ID':requestId});res.end(JSON.stringify(value));};
 try{
  path=new URL(req.url,'https://hbrp.local').pathname;
  if(limits.size>10000)fail(503,'Serviço ocupado. Tente novamente.');
  rate(req.socket.remoteAddress,path==='/v1/multiplayer/sync'?'multiplayer':'all',path==='/v1/multiplayer/sync'?6000:240,60000);
  if(req.method==='GET'&&path==='/health'){await pool.query('SELECT 1');return send(200,{status:'ok',service:'hbrp-api',version:'0.3.0'});}
  if(req.method==='GET'&&path==='/v1/auth/config')return send(200,{authMode:betaRequired?'beta':'legacy',registrationOpen:!betaRequired,minimumClientVersionCode});
  if(minimumClientVersionCode>0&&path.startsWith('/v1/')){
   const clientCode=Number(req.headers['x-horizonte-version-code']);
   if(!Number.isSafeInteger(clientCode)||clientCode<minimumClientVersionCode)
    fail(426,'Esta versão do Horizonte está desatualizada. Atualize o jogo para entrar.','CLIENT_OUTDATED');
  }
  const token=req.headers.authorization?.startsWith('Bearer ')?req.headers.authorization.substring(7):null;
  if(path==='/v1/admin/beta-access'){
   if(req.method==='GET'){
    const query=new URL(req.url,'https://hbrp.local').searchParams;
    return send(200,await betaAccess.list(token,query.get('q')??'',Number(query.get('offset')??0)));
   }
   if(req.method==='POST')return send(201,await betaAccess.create(token,await readBody(req)));
  }
  const betaTarget=path.match(/^\/v1\/admin\/beta-access\/([1-9][0-9]{0,18})\/(approve|reset|revoke)$/);
  if(req.method==='POST'&&betaTarget)return send(200,await betaAccess.change(token,betaTarget[1],betaTarget[2],await readBody(req)));
  if((req.method==='GET'&&path==='/v1/services')||(req.method==='POST'&&['/v1/realtime/ticket','/v1/voice/token'].includes(path))){
   const auth=await session(pool,token);const identity=await requireCharacter(pool,auth.account_id);
   if(path==='/v1/services')return send(200,realtime.config(identity));
   return send(200,path==='/v1/realtime/ticket'?realtime.ticket(identity,auth,await readBody(req)):realtime.voiceToken(identity,auth,await readBody(req)));
  }
  if(path==='/v1/criminal-economy'||path.startsWith('/v1/criminal-economy/')){
   const auth=await session(pool,token);const identity=await requireCharacter(pool,auth.account_id);
   const result=await handleCriminalEconomy({method:req.method,path,identity,
    body:req.method==='POST'?await readBody(req):{}});
   if(result!==null)return send(200,result);
  }
  if(req.method==='GET'&&path==='/v1/missions/arrival')return send(200,await arrival.read(token));
  if(req.method==='POST'&&path==='/v1/missions/arrival')return send(200,await arrival.update(token,await readBody(req)));
  if(req.method==='POST'&&path==='/v1/admin/map-teleport')return send(200,await administration.mapTeleport(token,await readBody(req)));
  if(req.method==='GET'&&path==='/v1/admin/players'){
   const query=new URL(req.url,'https://hbrp.local').searchParams;
   return send(200,await administration.list(token,query.get('q')??'',Number(query.get('offset')??0)));
  }
  const adminTarget=path.match(/^\/v1\/admin\/players\/([1-9][0-9]{0,18})$/);
  if(req.method==='POST'&&adminTarget)return send(200,await administration.update(token,adminTarget[1],await readBody(req)));
  if((req.method==='GET'||req.method==='POST')&&path==='/v1/police/duty'){
   const auth=await session(pool,token);
   const identity=await requireCharacter(pool,auth.account_id);
   const body=req.method==='POST'?await readBody(req):undefined;
   return send(200,await setDuty(identity,auth.id,body));
  }
  if((req.method==='GET'||req.method==='POST')&&path==='/v1/police/radio'){
   const auth=await session(pool,token);
   const identity=await requireCharacter(pool,auth.account_id);
   return send(200,room.radio(identity,auth.id,req.method==='POST'?await readBody(req):undefined));
  }
  if(req.method==='POST'&&path==='/v1/police/car'){
   const auth=await session(pool,token);
   const identity=await requireCharacter(pool,auth.account_id);
   return send(200,room.vehicle(identity,auth.id,await readBody(req)));
  }
  if(req.method==='POST'&&path==='/v1/multiplayer/attack'){
   const auth=await session(pool,token);
   const identity=await requireCharacter(pool,auth.account_id);
   return send(200,await room.attack(identity,auth.id,await readBody(req)));
  }
  if(req.method==='POST'&&(path==='/v1/multiplayer/shoot'||path==='/v1/multiplayer/respawn')){
   const auth=await session(pool,token),identity=await requireCharacter(pool,auth.account_id),body=await readBody(req);
   return send(200,await(path.endsWith('/shoot')?room.shoot(identity,auth.id,body):room.respawn(identity,auth.id,body)));
  }
  if(req.method==='POST'&&(path==='/v1/multiplayer/sync'||path==='/v1/multiplayer/leave'||path==='/v1/multiplayer/chat')){
   const auth=await session(pool,token);
   const body=await readBody(req);
   const identity=await requireCharacter(pool,auth.account_id);
   if(path.endsWith('/sync')&&!identity.jailUntil&&body.driving===true&&!body.policeCarId){
    const loan=await pool.query("SELECT 1 FROM hbrp.character_progress WHERE character_id=$1 AND category='mission' AND code=$2 AND state->>'phase' IN ('borrowed','rg_pending','rg_processing','rg_issued','driving_school') AND (state->>'loanExpiresAt')::timestamptz>now()",[identity.characterId,arrivalConfig.code]);
    if(!loan.rowCount)fail(403,'O empréstimo do veículo não está ativo.','MISSION_VEHICLE_UNAVAILABLE');
   }
   if(path.endsWith('/chat'))return send(200,await room.chat(identity,auth.id,body,
    request=>{
     if(['report','myreports'].includes(request.name))return playerCommands.command(request);
     if([...POLICE_PUBLIC,...POLICE_OPERATIONS,'jail','unjail'].includes(request.name))return operations.command(request);
     if(POLICE_COMMANDS.includes(request.name)||SERVICE_COMMANDS.includes(request.name))return police.command(request);
     return administration.command(token,request,room);
    }));
   if(path.endsWith('/leave')){exact(body,['clientId']);return send(200,room.leave(identity.characterId,auth.id,body.clientId));}
   await loadFollower();
   return send(200,room.sync(identity,auth.id,body));
  }
  if(req.method==='POST'&&path==='/v1/auth/register'){rate(req.socket.remoteAddress,'register',5,3600000);return send(201,await register(await readBody(req)));}
  if(req.method==='POST'&&path==='/v1/auth/login'){rate(req.socket.remoteAddress,'login',15,900000);return send(200,await login(await readBody(req)));}
  if(req.method==='POST'&&path==='/v1/characters'){rate(req.socket.remoteAddress,'character',30,60000);return send(201,await createCharacter(await readBody(req),token));}
  if(req.method==='GET'&&path==='/v1/player'){const auth=await session(pool,token);return send(200,await playerReply(pool,auth.account_id));}
  if(req.method==='PUT'&&path==='/v1/player/checkpoint')return send(200,await checkpoint(await readBody(req),token));
  if(req.method==='PUT'&&path==='/v1/player/profile')return send(200,await rename(await readBody(req),token));
  if(req.method==='POST'&&path==='/v1/auth/logout'){
   await transaction(async client=>{const auth=await session(client,token,true);await client.query('UPDATE hbrp.sessions SET revoked_at=now() WHERE id=$1',[auth.id]);room.removeSession(auth.id);});return send(200,{ok:true});
  }
  fail(404,'Rota não encontrada.','NOT_FOUND');
 }catch(error){
  if(error.code==='23505')send(409,{error:'E-mail ou nome de personagem já cadastrado.',code:'ALREADY_EXISTS'});
  else if(error instanceof ApiError||error instanceof RoomError||error instanceof AdminError||error instanceof ArrivalError||error instanceof RealtimeError||error instanceof BetaAccessError||error instanceof CriminalEconomyError)send(error.status,{error:error.message,code:error.code,...(error instanceof BetaAccessError?error.details:{})});
  else{errorLog(JSON.stringify({event:'request_error',requestId,code:error.code||'INTERNAL'}));send(500,{error:'Não foi possível concluir. Tente novamente.',code:'INTERNAL'});}
 }finally{if(path!=='/v1/multiplayer/sync'||status!==200)log(JSON.stringify({requestId,method:req.method,path,status,ms:Date.now()-started}));}
};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
const pool=new pg.Pool({connectionString:process.env.DATABASE_URL,max:6,connectionTimeoutMillis:5000,idleTimeoutMillis:30000});
pool.on('error',()=>console.error(JSON.stringify({event:'database_connection_error'})));
const server=https.createServer({key:fs.readFileSync(process.env.TLS_KEY),cert:fs.readFileSync(process.env.TLS_CERT),minVersion:'TLSv1.2'},createApiHandler(pool));
server.requestTimeout=15000;server.headersTimeout=10000;server.keepAliveTimeout=5000;
await pool.query('SELECT 1');
server.listen(Number(process.env.PORT||8443),'0.0.0.0',()=>console.log(JSON.stringify({event:'hbrp_api_started',port:Number(process.env.PORT||8443)})));
setInterval(()=>pool.query("DELETE FROM hbrp.sessions WHERE expires_at<now() OR revoked_at<now()-interval '1 day'").catch(()=>console.error('{"event":"session_cleanup_error"}')),3600000).unref();
process.on('SIGTERM',()=>server.close(async()=>{await pool.end();process.exit(0);}));
}
