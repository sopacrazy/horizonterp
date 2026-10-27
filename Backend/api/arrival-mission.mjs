import fs from 'node:fs';
import {randomUUID} from 'node:crypto';
export const arrivalConfig=Object.freeze(JSON.parse(fs.readFileSync(new URL('./arrival-config.json',import.meta.url),'utf8')));
export class ArrivalError extends Error{constructor(status,code,message){super(message);this.status=status;this.code=code;}}
const deny=(status,code,message)=>{throw new ArrivalError(status,code,message);};
export function createArrivalMission({pool,session,transaction,requireCharacter}){
 const config=arrivalConfig;
 function summary(saved={}){
  const phase=saved.phase==='completed'?'completed':saved.phase==='borrowed'&&Date.parse(saved.loanExpiresAt)>Date.now()?'borrowed':'available';
  return {phase,loanId:phase==='borrowed'?saved.loanId:null,loanExpiresAt:phase==='borrowed'?saved.loanExpiresAt:null,config};
 }
 async function read(token){
  const auth=await session(pool,token);const current=await requireCharacter(pool,auth.account_id);
  const row=(await pool.query("SELECT state FROM hbrp.character_progress WHERE character_id=$1 AND category='mission' AND code=$2",[current.characterId,config.code])).rows[0];
  return {mission:summary(row?.state)};
 }
 async function update(token,body){
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>k!=='action')||!['start','finish'].includes(body.action))deny(400,'INVALID_MISSION_ACTION','Ação de missão inválida.');
  return transaction(async client=>{
   const auth=await session(client,token,true);const current=await requireCharacter(client,auth.account_id);
   await client.query('SELECT id FROM hbrp.characters WHERE id=$1 FOR UPDATE',[current.characterId]);
   await client.query("INSERT INTO hbrp.character_progress(character_id,category,code) VALUES($1,'mission',$2) ON CONFLICT DO NOTHING",[current.characterId,config.code]);
   const record=(await client.query("SELECT state FROM hbrp.character_progress WHERE character_id=$1 AND category='mission' AND code=$2 FOR UPDATE",[current.characterId,config.code])).rows[0];
   let saved=record.state;
   if(saved.phase==='completed')return {mission:summary(saved),state:await requireCharacter(client,auth.account_id)};
   if(body.action==='start'&&summary(saved).phase==='borrowed')return {mission:summary(saved),state:current};
   const point=body.action==='start'?config.giver:config.cityHall;
   if(current.world!=='centro_teste'||Math.hypot(current.x-point.x,current.z-point.z)>5||Math.abs(current.y-point.y)>3)
    deny(409,'MISSION_TOO_FAR','Aproxime-se do NPC para interagir.');
   if(body.action==='start'){
    saved={phase:'borrowed',loanId:randomUUID(),loanExpiresAt:new Date(Date.now()+config.loanMinutes*60000).toISOString(),rewardKey:saved.rewardKey||randomUUID()};
   }else{
    if(summary(saved).phase!=='borrowed')deny(409,'MISSION_NOT_STARTED','Inicie a missão com o NPC. Se o empréstimo expirou, retorne para renová-lo.');
    const wallet=(await client.query("SELECT id FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[current.characterId])).rows[0];
    if(!wallet)deny(409,'MISSION_WALLET_MISSING','Carteira indisponível. Procure o suporte.');
    await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,config.rewardCashCents,saved.rewardKey,'Missão de boas-vindas: chegada à Prefeitura']);
    await client.query('UPDATE hbrp.character_stats SET xp=xp+$1,version=version+1,updated_at=now() WHERE character_id=$2',[config.rewardXp,current.characterId]);
    saved={...saved,phase:'completed',loanId:null,loanExpiresAt:null,completedAt:new Date().toISOString()};
   }
   await client.query("UPDATE hbrp.character_progress SET state=$1::jsonb,version=version+1,updated_at=now() WHERE character_id=$2 AND category='mission' AND code=$3",[JSON.stringify(saved),current.characterId,config.code]);
   await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,$2,'character',$3,$4::jsonb)",[auth.account_id,'mission.arrival.'+body.action,current.characterId,JSON.stringify({code:config.code,phase:saved.phase})]);
   return {mission:summary(saved),state:await requireCharacter(client,auth.account_id)};
  });
 }
 return {read,update};
}
