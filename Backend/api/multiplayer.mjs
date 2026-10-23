// Small, authenticated two-player presence room. Positions are client simulated:
// this prototype does not grant authority over inventory, money or progression.
import {randomUUID} from 'node:crypto';
export class RoomError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new RoomError(status,code,message);};
export const ADMIN_COMMANDS=Object.freeze(['a','ans','alist','inf','myastats','goto','mute','unmute','muterep','unmuterep','jail','unjail','re','reoff','kick']);
export function createRoom({clock=Date.now,ttl=10000,capacity=2}={}){
 const members=new Map();
 const messages=[];const chatEpoch=randomUUID();let messageSequence=0;
 function prune(){for(const [key,member] of members)if(clock()-member.seen>=ttl)members.delete(key);
  while(messages.length&&clock()-messages[0].sentAt>900000)messages.shift();}
 function sync(identity,sessionId,body){
  prune();
  const keys=['clientId','sequence','x','y','z','yaw','speed','jumping','jumpPhase','afterChat'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!keys.includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.clientId||'')||!Number.isSafeInteger(body.sequence)||body.sequence<0||body.sequence>2147483647)
   reject(400,'INVALID_FRAME','Atualização de movimento inválida.');
  if(body.afterChat!==undefined&&(!Number.isSafeInteger(body.afterChat)||body.afterChat<0))reject(400,'INVALID_FRAME','Cursor de chat inválido.');
  for(const key of ['x','y','z','yaw','speed','jumpPhase'])if(typeof body[key]!=='number'||!Number.isFinite(body[key]))reject(400,'INVALID_FRAME','Movimento inválido.');
  if(Math.abs(body.x)>22.5||Math.abs(body.z)>22.5||body.y< -1||body.y>5||body.yaw<0||body.yaw>=360||
    body.speed<0||body.speed>8||typeof body.jumping!=='boolean'||body.jumpPhase<0||body.jumpPhase>1)
   reject(400,'INVALID_FRAME','Movimento fora da área de teste.');
  if(identity.world!=='centro_teste')reject(409,'WORLD_UNAVAILABLE','Esta área não participa do teste online.');
  const previous=members.get(identity.characterId);
  if(previous&&previous.sessionId===sessionId&&previous.clientId!==body.clientId)
   reject(409,'SESSION_IN_USE','Esta conta já está conectada em outro jogo.');
  if(previous&&previous.sessionId===sessionId&&previous.clientId===body.clientId&&body.sequence<=previous.sequence)
   reject(409,'STALE_FRAME','Atualização de movimento antiga.');
  if(!previous&&members.size>=capacity)reject(409,'ROOM_FULL','A sala de teste já tem dois jogadores.');
  members.set(identity.characterId,{...body,lastChatAt:previous?.sessionId===sessionId?previous.lastChatAt:undefined,sessionId,seen:clock(),characterId:identity.characterId,
   uid:identity.uid,name:identity.name,model:identity.model,world:identity.world});
  return {room:'centro_teste',capacity,count:members.size,serverTime:clock(),sequence:body.sequence,chatEpoch,
   messages:messages.filter(m=>m.id>(body.afterChat??0)).slice(-20).map(publicMessage),
   peers:[...members.values()].filter(m=>m.characterId!==identity.characterId).map(m=>({
    characterId:m.characterId,uid:m.uid,name:m.name,model:m.model,x:m.x,y:m.y,z:m.z,
    yaw:m.yaw,speed:m.speed,jumping:m.jumping,jumpPhase:m.jumpPhase,sequence:m.sequence
   }))};
 }
 function publicMessage({characterId,clientId,sessionId,...message}){return message;}
 function chat(identity,sessionId,body){
  prune();
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!['clientId','messageId','text'].includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.messageId||'')||typeof body.text!=='string')reject(400,'INVALID_MESSAGE','Mensagem inválida.');
  const text=body.text.normalize('NFC').trim();
  if(!text||Array.from(text).length>180||/[\p{Cc}\p{Cf}\p{Cs}]/u.test(text))reject(400,'INVALID_MESSAGE','Use de 1 a 180 caracteres em uma linha.');
  const member=members.get(identity.characterId);
  if(!member||member.sessionId!==sessionId||member.clientId!==body.clientId)reject(409,'NOT_IN_ROOM','Conecte-se à sala antes de conversar.');
  const previous=messages.find(m=>m.characterId===identity.characterId&&m.sessionId===sessionId&&m.messageId===body.messageId);
  if(previous){if(previous.text!==text)reject(409,'MESSAGE_CONFLICT','Mensagem repetida com outro texto.');return {chatEpoch,message:publicMessage(previous)};}
  if(member.lastChatAt!==undefined&&clock()-member.lastChatAt<1200)reject(429,'CHAT_RATE_LIMITED','Aguarde um instante antes de enviar outra mensagem.');
  member.lastChatAt=clock();
  if(text.startsWith('/')){
   if(identity.accountRole!=='admin'||!Number.isInteger(identity.adminLevel)||identity.adminLevel<1)
    reject(403,'ADMIN_ONLY','Este comando é exclusivo da administração.');
   const name=text.slice(1).split(/\s+/u)[0].toLowerCase();
   if(!ADMIN_COMMANDS.includes(name))reject(400,'UNKNOWN_COMMAND','Comando administrativo desconhecido.');
   return {chatEpoch,command:{name,status:'not_implemented',message:'/'+name+': comando reconhecido. A função ainda não está disponível.'}};
  }
  const message={id:++messageSequence,messageId:body.messageId,characterId:identity.characterId,sessionId,uid:identity.uid,name:identity.name,text,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();
  return {chatEpoch,message:publicMessage(message)};
 }
 function leave(characterId,sessionId,clientId){
  const member=members.get(characterId);
  if(member&&member.sessionId===sessionId&&(!clientId||member.clientId===clientId))members.delete(characterId);
  prune();return {ok:true};
 }
 function removeSession(sessionId){for(const [id,m] of members)if(m.sessionId===sessionId)members.delete(id);}
 return {sync,chat,leave,removeSession};
}
