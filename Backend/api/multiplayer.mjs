// Small, authenticated two-player presence room. Positions are client simulated:
// this prototype does not grant authority over inventory, money or progression.
import {randomUUID} from 'node:crypto';
import {isPositionWithinWorld} from './world-bounds.mjs';
import {ADMIN_COMMANDS as COMMAND_CATALOG,adminCommand,adminHelp} from './admin-command-catalog.mjs';
export class RoomError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new RoomError(status,code,message);};
export const ADMIN_COMMANDS=Object.freeze(COMMAND_CATALOG.map(command=>command.name));
export function createRoom({clock=Date.now,ttl=10000,capacity=2}={}){
 const members=new Map();
 const messages=[];const chatEpoch=randomUUID();let messageSequence=0,teleportSequence=0;
 const mutes=new Map(),adminUsage=new Map(),commandResults=new Map();let serverHour=-1;
 function prune(){for(const [key,member] of members)if(clock()-member.seen>=ttl)members.delete(key);
  while(messages.length&&clock()-messages[0].sentAt>900000)messages.shift();
  for(const [key,mute] of mutes)if(mute.until<=clock())mutes.delete(key);
  for(const [key,result] of commandResults)if(clock()-result.sentAt>900000)commandResults.delete(key);}
 function sync(identity,sessionId,body){
  prune();
  const keys=['clientId','sequence','x','y','z','yaw','speed','jumping','jumpPhase','afterChat','afterTeleport'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!keys.includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.clientId||'')||!Number.isSafeInteger(body.sequence)||body.sequence<0||body.sequence>2147483647)
   reject(400,'INVALID_FRAME','Atualização de movimento inválida.');
  if(body.afterChat!==undefined&&(!Number.isSafeInteger(body.afterChat)||body.afterChat<0))reject(400,'INVALID_FRAME','Cursor de chat inválido.');
  if(body.afterTeleport!==undefined&&(!Number.isSafeInteger(body.afterTeleport)||body.afterTeleport<0))reject(400,'INVALID_FRAME','Cursor de teleporte inválido.');
  for(const key of ['x','y','z','yaw','speed','jumpPhase'])if(typeof body[key]!=='number'||!Number.isFinite(body[key]))reject(400,'INVALID_FRAME','Movimento inválido.');
  if(body.yaw<0||body.yaw>=360||
    body.speed<0||body.speed>8||typeof body.jumping!=='boolean'||body.jumpPhase<0||body.jumpPhase>1)
   reject(400,'INVALID_FRAME','Movimento fora da área de teste.');
  if(identity.world!=='centro_teste')reject(409,'WORLD_UNAVAILABLE','Esta área não participa do teste online.');
  if(!isPositionWithinWorld(identity.world,body))reject(400,'INVALID_FRAME','Movimento fora da área de teste.');
  const previous=members.get(identity.characterId);
  if(previous&&previous.sessionId===sessionId&&previous.clientId!==body.clientId)
   reject(409,'SESSION_IN_USE','Esta conta já está conectada em outro jogo.');
  if(previous&&previous.sessionId===sessionId&&previous.clientId===body.clientId&&body.sequence<=previous.sequence)
   reject(409,'STALE_FRAME','Atualização de movimento antiga.');
  if(!previous&&members.size>=capacity)reject(409,'ROOM_FULL','A sala de teste já tem dois jogadores.');
  const member={...body,lastChatAt:previous?.sessionId===sessionId?previous.lastChatAt:undefined,sessionId,seen:clock(),characterId:identity.characterId,
   uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],model:identity.model,world:identity.world,
   adminLevel:identity.accountRole==='admin'?identity.adminLevel:0,
   invisible:identity.accountRole==='admin'&&identity.adminLevel>=3&&(previous?.invisible||false),teleport:previous?.teleport||null};
  members.set(identity.characterId,member);
  return {room:'centro_teste',capacity,count:members.size,serverTime:clock(),sequence:body.sequence,chatEpoch,serverHour,hourOverride:serverHour>=0,
   teleport:member.teleport&&member.teleport.id>(body.afterTeleport??0)?member.teleport:null,
   messages:messages.filter(m=>m.id>(body.afterChat??0)&&(m.channel!=='admin'||member.adminLevel>0)).slice(-20).map(publicMessage),
   peers:[...members.values()].filter(m=>m.characterId!==identity.characterId&&(!m.invisible||member.adminLevel>0)).map(m=>({
    characterId:m.characterId,uid:m.uid,name:m.name,firstName:m.firstName,model:m.model,x:m.x,y:m.y,z:m.z,
    yaw:m.yaw,speed:m.speed,jumping:m.jumping,jumpPhase:m.jumpPhase,sequence:m.sequence,updatedAt:m.seen
   }))};
 }
 function publicMessage({characterId,clientId,sessionId,...message}){return message;}
 function online(uid){return [...members.values()].find(member=>member.uid===uid)||null;}
 function uidArg(value){if(!/^[1-9][0-9]{0,18}$/.test(value||''))reject(400,'COMMAND_USAGE','Informe o ID numérico do jogador.');return value;}
 function target(uid,actor,protect=true){
  const found=online(uidArg(uid));
  if(!found)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(protect&&(found.characterId===actor.characterId||found.adminLevel>=actor.adminLevel))
   reject(403,'ADMIN_HIERARCHY','Você só pode alterar jogadores de nível inferior.');
  return found;
 }
 function emit(identity,sessionId,clientId,messageId,text,channel='public'){
  const message={id:++messageSequence,messageId,characterId:identity.characterId,sessionId,clientId,uid:identity.uid,
   name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],isAdmin:true,text,channel,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();return message;
 }
 function command(identity,sessionId,body,member,name,args,externalAction){
  const definition=adminCommand(name);
  if(!definition)reject(400,'UNKNOWN_COMMAND','Comando administrativo desconhecido. Use /ahelp.');
  if(identity.adminLevel<definition.level)reject(403,'ADMIN_LEVEL_REQUIRED',`/${definition.name} exige nível ${definition.level}.`);
  name=definition.name;args=args.trim();
  const parts=args?args.split(/\s+/u):[];
  const ok=(message,extra={})=>({name,status:'ok',message,...extra});
  const usage=()=>reject(400,'COMMAND_USAGE','Uso: '+definition.usage);
  if(definition.missing)return {name,status:'not_implemented',message:`/${name} aguarda o sistema de ${definition.missing}.`};
  if(name==='ahelp'){
   if(parts.length>1||parts.length===1&&!/^[123]$/.test(parts[0]))usage();
   const level=parts.length?Number(parts[0]):identity.adminLevel;
   if(level>identity.adminLevel)reject(403,'ADMIN_LEVEL_REQUIRED','Você não pode consultar um nível acima do seu.');
   return ok(`Comandos disponíveis até o nível ${level}:`,{lines:adminHelp(level)});
  }
  if(name==='coords')return ok(`X ${member.x.toFixed(1)} · Y ${member.y.toFixed(1)} · Z ${member.z.toFixed(1)}`);
  if(name==='alist'){
   const admins=[...members.values()].filter(m=>m.adminLevel>0);
   return ok(`${admins.length} administrador(es) online:`,{lines:admins.map(m=>`ID ${m.uid} · ${m.firstName} · nível ${m.adminLevel}`)});
  }
  if(name==='mystats'||name==='astats'){
   if(name==='astats'&&parts.length!==1)usage();
   let subject=member;
   if(name==='astats'){
    const matches=[...members.values()].filter(m=>m.uid===parts[0]||m.name.toLowerCase()===parts[0].toLowerCase()||m.firstName.toLowerCase()===parts[0].toLowerCase());
    if(matches.length>1)reject(409,'AMBIGUOUS_PLAYER','Nome repetido. Use o ID do jogador.');
    subject=matches[0];
    if(!subject)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
   }
   if(subject.adminLevel<1)reject(400,'NOT_ADMIN','Este jogador não é administrador.');
   const count=adminUsage.get(subject.characterId)||0;
   return ok(`${subject.firstName}: nível ${subject.adminLevel}, ${count} comando(s) nesta sessão do servidor.`);
  }
  if(name==='a'||name==='aad'){
   if(!args)usage();
   const message=emit(identity,sessionId,body.clientId,body.messageId,args,name==='a'?'admin':'announcement');
   return ok(name==='a'?'Mensagem enviada à administração.':'Anúncio enviado a todos.',{messageId:message.id});
  }
  if(name==='goto'){
   if(parts.length!==1)usage();const found=target(parts[0],member,false);
   return {name,status:'teleport',message:`Indo até ${found.firstName} (ID ${found.uid}).`,x:found.x,y:found.y,z:found.z};
  }
  if(name==='gethere'||name==='spawn'){
   if(parts.length!==1)usage();const found=target(parts[0],member);
   found.teleport={id:++teleportSequence,x:name==='spawn'?0:member.x,y:name==='spawn'?.08:member.y,z:name==='spawn'?0:member.z};
   return ok(`${found.firstName} receberá o teleporte na próxima atualização.`);
  }
  if(name==='re'||name==='reoff'){
   if(name==='reoff'){if(parts.length)usage();return {name,status:'spectate_off',message:'Observação encerrada.'};}
   if(parts.length!==1)usage();const found=target(parts[0],member,false);
   return {name,status:'spectate',message:`Observando ${found.firstName} (ID ${found.uid}).`,uid:found.uid};
  }
  if(name==='mute'){
   if(parts.length<3||!/^\d+$/.test(parts[1]))usage();
   const minutes=Number(parts[1]);if(!Number.isSafeInteger(minutes)||minutes<1||minutes>1440)usage();
   const reason=parts.slice(2).join(' ');if(reason.length<3)usage();
   const found=target(parts[0],member);mutes.set(found.characterId,{until:clock()+minutes*60000,reason});
   return ok(`${found.firstName} silenciado por ${minutes} minuto(s). Motivo: ${reason}`);
  }
  if(name==='unmute'){
   if(parts.length!==1)usage();const found=target(parts[0],member);mutes.delete(found.characterId);
   return ok(`Silêncio de ${found.firstName} removido.`);
  }
  if(name==='inviz'||name==='removeinviz'||name==='returninviz'){
   if(name==='inviz'){if(parts.length)usage();member.invisible=!member.invisible;return ok(`Invisibilidade ${member.invisible?'ativada':'desativada'}.`);}
   if(parts.length!==1)usage();const found=target(parts[0],member);
   if(found.adminLevel<1)reject(400,'NOT_ADMIN','O alvo não é administrador.');
   found.invisible=name==='returninviz';return ok(`Invisibilidade de ${found.firstName} ${found.invisible?'ativada':'desativada'}.`);
  }
  if(name==='t'){
   if(parts.length!==1||!/^\d{1,2}$/.test(parts[0])||Number(parts[0])>23)usage();
   serverHour=Number(parts[0]);return ok(`Hora da sala ajustada para ${String(serverHour).padStart(2,'0')}:00.`);
  }
  if(name==='apanel'){if(parts.length)usage();return {name,status:'open_panel',message:'Abrindo lista administrativa.'};}
  if(name==='speed'){
   if(parts.length!==1||!['1','2','4','6','8','10','12'].includes(parts[0]))usage();
   return {name,status:'speed',message:`Velocidade x${parts[0]} ativada.`,value:Number(parts[0])};
  }
  if(['kick','ban','unban','makeadmin'].includes(name)&&externalAction)
   return externalAction({name,args,identity,sessionId});
  return {name,status:'not_implemented',message:`/${name} está registrado; a mecânica ainda não está disponível.`};
 }
 function chat(identity,sessionId,body,externalAction){
  prune();
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!['clientId','messageId','text'].includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.messageId||'')||typeof body.text!=='string')reject(400,'INVALID_MESSAGE','Mensagem inválida.');
  const text=body.text.normalize('NFC').trim();
  if(!text||Array.from(text).length>180||/[\p{Cc}\p{Cf}\p{Cs}]/u.test(text))reject(400,'INVALID_MESSAGE','Use de 1 a 180 caracteres em uma linha.');
  const member=members.get(identity.characterId);
  if(!member||member.sessionId!==sessionId||member.clientId!==body.clientId)reject(409,'NOT_IN_ROOM','Conecte-se à sala antes de conversar.');
  const key=identity.characterId+':'+sessionId+':'+body.messageId;
  const priorCommand=commandResults.get(key);
  if(priorCommand){if(priorCommand.text!==text)reject(409,'MESSAGE_CONFLICT','Mensagem repetida com outro texto.');return priorCommand.result;}
  const previous=messages.find(m=>m.characterId===identity.characterId&&m.sessionId===sessionId&&m.messageId===body.messageId);
  if(previous){if(previous.text!==text)reject(409,'MESSAGE_CONFLICT','Mensagem repetida com outro texto.');return {chatEpoch,message:publicMessage(previous)};}
  if(member.lastChatAt!==undefined&&clock()-member.lastChatAt<1200)reject(429,'CHAT_RATE_LIMITED','Aguarde um instante antes de enviar outra mensagem.');
  member.lastChatAt=clock();
  if(text.startsWith('/')){
   if(identity.accountRole!=='admin'||!Number.isInteger(identity.adminLevel)||identity.adminLevel<1)
    reject(403,'ADMIN_ONLY','Este comando é exclusivo da administração.');
   const match=text.match(/^\/([a-z]+)(?:\s+(.+))?$/iu);
   if(!match)reject(400,'UNKNOWN_COMMAND','Comando inválido. Use /ahelp.');
   const count=adminUsage.get(identity.characterId)||0;adminUsage.set(identity.characterId,count+1);
   const result=command(identity,sessionId,body,member,match[1].toLowerCase(),match[2]||'',externalAction);
   const response=Promise.resolve(result).then(value=>({chatEpoch,command:value}));
   commandResults.set(key,{text,result:response,sentAt:clock()});
   return response;
  }
  const mute=mutes.get(identity.characterId);
  if(mute&&mute.until>clock())reject(403,'PLAYER_MUTED',`Chat silenciado por mais ${Math.ceil((mute.until-clock())/60000)} minuto(s). Motivo: ${mute.reason}`);
  const isAdmin=identity.accountRole==='admin'&&Number.isInteger(identity.adminLevel)&&identity.adminLevel>0;
  const message={id:++messageSequence,messageId:body.messageId,characterId:identity.characterId,sessionId,uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],isAdmin,text,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();
  return {chatEpoch,message:publicMessage(message)};
 }
 function leave(characterId,sessionId,clientId){
  const member=members.get(characterId);
  if(member&&member.sessionId===sessionId&&(!clientId||member.clientId===clientId))members.delete(characterId);
  prune();return {ok:true};
 }
 function removeSession(sessionId){for(const [id,m] of members)if(m.sessionId===sessionId)members.delete(id);}
 return {sync,chat,leave,removeSession,findOnline:online};
}
