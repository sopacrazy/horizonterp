import fs from 'node:fs';
import {randomUUID} from 'node:crypto';
export const arrivalConfig=Object.freeze(JSON.parse(fs.readFileSync(new URL('./arrival-config.json',import.meta.url),'utf8')));
export class ArrivalError extends Error{constructor(status,code,message){super(message);this.status=status;this.code=code;}}
const deny=(status,code,message)=>{throw new ArrivalError(status,code,message);};
export function createArrivalMission({pool,session,transaction,requireCharacter}){
 const config=arrivalConfig;
 function summary(saved={}){
  let phase=saved.phase||'available';
  // Accounts that finished the original one-mission tutorial continue at the RG mission.
  if(phase==='completed'&&saved.journeyVersion!==2)phase='rg_pending';
  if(phase==='borrowed'&&!(Date.parse(saved.loanExpiresAt)>Date.now()))phase='available';
  if(phase==='driving_school'&&!(Date.parse(saved.loanExpiresAt)>Date.now()))phase='rg_issued';
  const loanActive=['borrowed','rg_pending','rg_processing','rg_issued','driving_school'].includes(phase)&&Date.parse(saved.loanExpiresAt)>Date.now();
  const hasRG=saved.hasRG===true||['rg_issued','driving_school','completed'].includes(phase);
  return {phase,loanId:loanActive?saved.loanId:null,loanExpiresAt:loanActive?saved.loanExpiresAt:null,hasRG,hasCNH:saved.hasCNH===true,config};
 }
 async function read(token){
  const auth=await session(pool,token);const current=await requireCharacter(pool,auth.account_id);
  const row=(await pool.query("SELECT state FROM hbrp.character_progress WHERE character_id=$1 AND category='mission' AND code=$2",[current.characterId,config.code])).rows[0];
  return {mission:summary(row?.state)};
 }
 async function update(token,body){
  const actions=['start','finish','confirm_rg','issue_rg','start_driving_school','finish_driving_school'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>k!=='action')||!actions.includes(body.action))deny(400,'INVALID_MISSION_ACTION','Ação de missão inválida.');
  return transaction(async client=>{
   const auth=await session(client,token,true);const current=await requireCharacter(client,auth.account_id);
   await client.query('SELECT id FROM hbrp.characters WHERE id=$1 FOR UPDATE',[current.characterId]);
   await client.query("INSERT INTO hbrp.character_progress(character_id,category,code) VALUES($1,'mission',$2) ON CONFLICT DO NOTHING",[current.characterId,config.code]);
   const record=(await client.query("SELECT state FROM hbrp.character_progress WHERE character_id=$1 AND category='mission' AND code=$2 FOR UPDATE",[current.characterId,config.code])).rows[0];
   let saved={...record.state};let phase=summary(saved).phase;
   if(saved.phase==='completed'&&saved.journeyVersion!==2)saved={
    ...saved,
    phase:'rg_pending',
    journeyVersion:2,
    hasRG:false,
    mission1CompletedAt:saved.mission1CompletedAt||saved.completedAt||new Date().toISOString(),
    rgRewardKey:saved.rgRewardKey||randomUUID()
   };
   phase=summary(saved).phase;
   if(phase==='completed')return {mission:summary(saved),state:await requireCharacter(client,auth.account_id)};
   if(body.action==='start'&&phase==='borrowed')return {mission:summary(saved),state:current};
   if(body.action==='finish'&&phase==='rg_pending')return {mission:summary(saved),state:current};
   if(body.action==='confirm_rg'&&phase==='rg_processing')return {mission:summary(saved),state:current};
   if(body.action==='issue_rg'&&['rg_issued','driving_school'].includes(phase))return {mission:summary(saved),state:current};
   if(body.action==='start_driving_school'&&phase==='driving_school')return {mission:summary(saved),state:current};
   const point=body.action==='start'?config.giver:body.action==='finish_driving_school'?config.drivingSchool:config.cityHall;
   if(current.world!=='centro_teste'||Math.hypot(current.x-point.x,current.z-point.z)>5||Math.abs(current.y-point.y)>3)
    deny(409,'MISSION_TOO_FAR','Aproxime-se do NPC para interagir.');
   if(body.action==='start'){
    if(phase!=='available')deny(409,'MISSION_INVALID_PHASE','Esta etapa não está disponível.');
    saved={...saved,phase:'borrowed',journeyVersion:2,loanId:randomUUID(),loanExpiresAt:new Date(Date.now()+config.loanMinutes*60000).toISOString(),rewardKey:saved.rewardKey||randomUUID()};
   }else if(body.action==='finish'){
    if(phase!=='borrowed')deny(409,'MISSION_NOT_STARTED','Inicie a missão com o NPC. Se o empréstimo expirou, retorne para renová-lo.');
    const wallet=(await client.query("SELECT id FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[current.characterId])).rows[0];
    if(!wallet)deny(409,'MISSION_WALLET_MISSING','Carteira indisponível. Procure o suporte.');
    await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,config.rewardCashCents,saved.rewardKey,'Missão de boas-vindas: chegada à Prefeitura']);
    await client.query('UPDATE hbrp.character_stats SET xp=xp+$1,version=version+1,updated_at=now() WHERE character_id=$2',[config.rewardXp,current.characterId]);
    saved={...saved,phase:'rg_pending',mission1CompletedAt:new Date().toISOString(),rgRewardKey:saved.rgRewardKey||randomUUID()};
   }else if(body.action==='confirm_rg'){
    if(phase!=='rg_pending')deny(409,'MISSION_INVALID_PHASE','A emissão do RG não está disponível nesta etapa.');
    saved={...saved,phase:'rg_processing',rgConfirmedAt:new Date().toISOString(),confirmedName:current.name};
   }else if(body.action==='issue_rg'){
    if(phase!=='rg_processing')deny(409,'MISSION_INVALID_PHASE','Confirme os dados antes de emitir o RG.');
    const wallet=(await client.query("SELECT id FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[current.characterId])).rows[0];
    if(!wallet)deny(409,'MISSION_WALLET_MISSING','Carteira indisponível. Procure o suporte.');
    await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,config.rgRewardCashCents,saved.rgRewardKey,'Missão 02: emissão do primeiro RG']);
    await client.query('UPDATE hbrp.character_stats SET xp=xp+$1,version=version+1,updated_at=now() WHERE character_id=$2',[config.rgRewardXp,current.characterId]);
    saved={...saved,phase:'rg_issued',hasRG:true,rgIssuedAt:new Date().toISOString(),drivingRewardKey:saved.drivingRewardKey||randomUUID()};
   }else if(body.action==='start_driving_school'){
    if(phase!=='rg_issued')deny(409,'MISSION_INVALID_PHASE','Emita o RG antes de seguir para a autoescola.');
    saved={...saved,phase:'driving_school',loanId:summary(saved).loanId||randomUUID(),loanExpiresAt:new Date(Date.now()+config.loanMinutes*60000).toISOString()};
   }else if(body.action==='finish_driving_school'){
    if(phase!=='driving_school')deny(409,'MISSION_INVALID_PHASE','Inicie o trajeto até a autoescola.');
    await client.query('UPDATE hbrp.character_stats SET xp=xp+$1,version=version+1,updated_at=now() WHERE character_id=$2',[config.drivingRewardXp,current.characterId]);
    saved={...saved,phase:'completed',loanId:null,loanExpiresAt:null,mission3CompletedAt:new Date().toISOString()};
   }
   await client.query("UPDATE hbrp.character_progress SET state=$1::jsonb,version=version+1,updated_at=now() WHERE character_id=$2 AND category='mission' AND code=$3",[JSON.stringify(saved),current.characterId,config.code]);
   await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,$2,'character',$3,$4::jsonb)",[auth.account_id,'mission.arrival.'+body.action,current.characterId,JSON.stringify({code:config.code,phase:saved.phase})]);
   return {mission:summary(saved),state:await requireCharacter(client,auth.account_id)};
  });
 }
 return {read,update};
}
