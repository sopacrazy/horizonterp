// Small, authenticated two-player presence room. Positions are client simulated:
// this prototype does not grant authority over inventory, money or progression.
import {PLAYER_COMMANDS,playerCommand,playerHelp} from './player-command-catalog.mjs';
import {randomUUID} from 'node:crypto';
import {isPositionWithinWorld} from './world-bounds.mjs';
import {ADMIN_COMMANDS as COMMAND_CATALOG,adminCommand,adminHelp,gameCommandHelp} from './admin-command-catalog.mjs';
export class RoomError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new RoomError(status,code,message);};
export const ADMIN_COMMANDS=Object.freeze(COMMAND_CATALOG.map(command=>command.name));
export const POLICE_COMMANDS=Object.freeze(['ide','wanted','su','clear','cuff','uncuff','follow','stay','put','eject','carpolicia','ticket','proc','m','f','d','gov']);
export const SERVICE_COMMANDS=Object.freeze(['190']);
export function createRoom({clock=Date.now,ttl=10000,capacity=2}={}){
 const members=new Map();
 let followerBot=null;
 const policeDuty=new Map();
 const policeChannels=new Map();
 const restraints=new Map();
 const policeCars=new Map();
 const messages=[];const chatEpoch=randomUUID();let messageSequence=0,teleportSequence=0;
 const mutes=new Map(),adminUsage=new Map(),commandResults=new Map();let serverHour=-1;
 function prune(){for(const [key,member] of members)if(!member.isBot&&clock()-member.seen>=ttl){members.delete(key);policeDuty.delete(key);policeChannels.delete(key);releaseCustody(key);}
  if(followerBot&&!members.has(followerBot.ownerCharacterId))members.delete(followerBot.characterId);
  while(messages.length&&clock()-messages[0].sentAt>900000)messages.shift();
  for(const [key,mute] of mutes)if(mute.until<=clock())mutes.delete(key);
  for(const [key,result] of commandResults)if(clock()-result.sentAt>900000)commandResults.delete(key);}
 function sync(identity,sessionId,body){
  prune();
  const keys=['clientId','sequence','x','y','z','yaw','speed','jumping','jumpPhase','afterChat','afterTeleport','driving','voice','voiceSequence','radioVoice','radioSequence','radioChannel'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!keys.includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.clientId||'')||!Number.isSafeInteger(body.sequence)||body.sequence<0||body.sequence>2147483647)
   reject(400,'INVALID_FRAME','Atualização de movimento inválida.');
  if(body.afterChat!==undefined&&(!Number.isSafeInteger(body.afterChat)||body.afterChat<0))reject(400,'INVALID_FRAME','Cursor de chat inválido.');
  if(body.driving!==undefined&&typeof body.driving!=='boolean')reject(400,'INVALID_FRAME','Estado do veículo inválido.');
  if(body.afterTeleport!==undefined&&(!Number.isSafeInteger(body.afterTeleport)||body.afterTeleport<0))reject(400,'INVALID_FRAME','Cursor de teleporte inválido.');
  if(body.voice!==undefined&&body.voice!==''&&(!Number.isSafeInteger(body.voiceSequence)||body.voiceSequence<1||
    typeof body.voice!=='string'||body.voice.length>2200||!/^([A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(body.voice)))
   reject(400,'INVALID_FRAME','Áudio inválido.');
  if(body.radioVoice!==undefined&&body.radioVoice!==''&&(!Number.isSafeInteger(body.radioSequence)||body.radioSequence<1||
    typeof body.radioVoice!=='string'||body.radioVoice.length>2200||!/^([A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(body.radioVoice)))
   reject(400,'INVALID_FRAME','Áudio de rádio inválido.');
  for(const key of ['x','y','z','yaw','speed','jumpPhase'])if(typeof body[key]!=='number'||!Number.isFinite(body[key]))reject(400,'INVALID_FRAME','Movimento inválido.');
  if(body.yaw<0||body.yaw>=360||
    body.speed<0||body.speed>8||typeof body.jumping!=='boolean'||body.jumpPhase<0||body.jumpPhase>1)
   reject(400,'INVALID_FRAME','Movimento fora da área de teste.');
  if(identity.world!=='centro_teste')reject(409,'WORLD_UNAVAILABLE','Esta área não participa do teste online.');
  if(!isPositionWithinWorld(identity.world,body))reject(400,'INVALID_FRAME','Movimento fora da área de teste.');
  const previous=members.get(identity.characterId);
  const restraint=restraints.get(identity.characterId);
  const isPolice=identity.accountRole==='admin'&&identity.adminLevel>0&&policeDuty.get(identity.characterId)===sessionId;
  if(!isPolice){policeDuty.delete(identity.characterId);policeChannels.delete(identity.characterId);}
  if(body.radioVoice&&(!isPolice||policeChannels.get(identity.characterId)?.sessionId!==sessionId||
    body.radioChannel!==policeChannels.get(identity.characterId)?.channel))
   reject(403,'RADIO_FORBIDDEN','Conecte-se ao rádio policial antes de transmitir.');
  if(previous&&previous.sessionId===sessionId&&previous.clientId!==body.clientId)
   reject(409,'SESSION_IN_USE','Esta conta já está conectada em outro jogo.');
  if(previous&&previous.sessionId===sessionId&&previous.clientId===body.clientId&&body.sequence<=previous.sequence)
   reject(409,'STALE_FRAME','Atualização de movimento antiga.');
  if(!previous&&members.size>=capacity)reject(409,'ROOM_FULL','A sala de teste já tem dois jogadores.');
  const member={...body,renderSequence:Math.max((previous?.renderSequence??0)+1,body.sequence),lastChatAt:previous?.sessionId===sessionId?previous.lastChatAt:undefined,sessionId,seen:clock(),characterId:identity.characterId,
   voice:body.voice||'',voiceSequence:body.voice?body.voiceSequence:previous?.voiceSequence||0,
   radioVoice:body.radioVoice||'',radioSequence:body.radioVoice?body.radioSequence:previous?.radioSequence||0,
   radioChannel:isPolice?policeChannels.get(identity.characterId)?.channel||0:0,
   uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],model:identity.model,world:identity.world,
   policeDuty:isPolice,orgCode:identity.organization?.code||'',
   adminLevel:identity.accountRole==='admin'?identity.adminLevel:0,
   invisible:identity.accountRole==='admin'&&identity.adminLevel>=3&&(previous?.invisible||false),teleport:previous?.teleport||null};
  // Keep a small recovery window for clients polling at different phases or over mobile jitter.
  // Legacy voice/radioVoice fields remain unchanged for installed clients.
  const sameSource=previous?.sessionId===sessionId&&previous?.clientId===body.clientId;
  member.voiceFrames=voiceWindow(sameSource?previous.voiceFrames:[],body.voice,body.voiceSequence);
  member.radioFrames=voiceWindow(sameSource&&isPolice&&previous.radioChannel===member.radioChannel?previous.radioFrames:[],body.radioVoice,body.radioSequence);
  members.set(identity.characterId,member);
  if(restraint){
   const officer=members.get(restraint.officerCharacterId);
   if(!officer){restraints.delete(identity.characterId);}
   else if(previous){member.x=previous.x;member.y=previous.y;member.z=previous.z;member.yaw=previous.yaw;member.speed=0;member.jumping=false;member.jumpPhase=0;member.driving=false;}
  }
  for(const [detainedId,record] of restraints)if(record.following&&record.officerCharacterId===identity.characterId){
   const detained=members.get(detainedId);if(!detained)continue;
   const radians=member.yaw*Math.PI/180;
   detained.x=member.x-Math.sin(radians)*1.7;detained.y=member.y;
   detained.z=member.z-Math.cos(radians)*1.7;detained.yaw=member.yaw;
   detained.speed=Math.min(member.speed,1.65);
   detained.renderSequence=(detained.renderSequence??detained.sequence)+1;
   detained.seen=clock();
  }
  if(followerBot&&identity.characterId===followerBot.ownerCharacterId)advanceFollower(member);
  const seat=[...policeCars.values()].find(car=>car.occupantCharacterId===identity.characterId);
  if(seat){member.x=seat.x;member.y=seat.y;member.z=seat.z;member.yaw=seat.yaw;member.speed=0;member.jumping=false;member.jumpPhase=0;}
  return {room:'centro_teste',capacity,count:members.size,serverTime:clock(),sequence:body.sequence,chatEpoch,serverHour,hourOverride:serverHour>=0,
   selfCuffed:restraints.has(identity.characterId),selfEscortedBy:restraints.get(identity.characterId)?.following?members.get(restraints.get(identity.characterId).officerCharacterId)?.uid||'':'',
   selfX:member.x,selfY:member.y,selfZ:member.z,selfYaw:member.yaw,
   selfInPoliceCar:!!seat,
   policeCars:[...policeCars.values()].map(car=>({id:car.id,ownerUid:car.ownerUid,x:car.x,y:car.y,z:car.z,yaw:car.yaw,
    occupantUid:members.get(car.occupantCharacterId)?.uid||'',occupantName:members.get(car.occupantCharacterId)?.firstName||''})),
   teleport:member.teleport&&member.teleport.id>(body.afterTeleport??0)?member.teleport:null,
   messages:messages.filter(m=>m.id>(body.afterChat??0)&&(m.channel!=='admin'||member.adminLevel>0)&&
    (!m.targetCharacterId||m.targetCharacterId===member.characterId)&&
    (!m.recipientCharacterIds||m.recipientCharacterIds.includes(member.characterId))).slice(-20).map(publicMessage),
   peers:[...members.values()].filter(m=>m.characterId!==identity.characterId&&(!m.invisible||member.adminLevel>0)).map(m=>({
    characterId:m.characterId,uid:m.uid,name:m.name,firstName:m.firstName,model:m.model,x:m.x,y:m.y,z:m.z,
    yaw:m.yaw,speed:m.speed,jumping:m.jumping,jumpPhase:m.jumpPhase,driving:m.driving===true,sequence:m.renderSequence??m.sequence,updatedAt:m.seen,
   voice:m.voice&&m.seen>=clock()-500&&Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=35?m.voice:'',voiceSequence:m.voiceSequence,
   voiceFrames:Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=35?publicVoice(m.voiceFrames):[],
   radioVoice:member.policeDuty&&member.radioChannel>0&&m.radioChannel===member.radioChannel&&m.seen>=clock()-500?m.radioVoice:'',radioSequence:m.radioSequence,
   radioFrames:member.policeDuty&&m.policeDuty&&member.radioChannel>0&&m.radioChannel===member.radioChannel?publicVoice(m.radioFrames):[],
    policeDuty:m.policeDuty===true,cuffed:restraints.has(m.characterId),escortedBy:restraints.get(m.characterId)?.following?members.get(restraints.get(m.characterId).officerCharacterId)?.uid||'': '',
    inPoliceCar:[...policeCars.values()].some(car=>car.occupantCharacterId===m.characterId)
   }))};
 }
 function voiceWindow(previous,data,sequence){
  const frames=(previous||[]).filter(frame=>clock()-frame.at<=500);
  if(data&&(!frames.length||sequence>frames.at(-1).sequence))frames.push({sequence,data,at:clock()});
  return frames.slice(-4);
 }
 function publicVoice(frames){return (frames||[]).filter(frame=>clock()-frame.at<=500).map(({sequence,data})=>({sequence,data}));}
 function publicMessage({characterId,clientId,sessionId,targetCharacterId,recipientCharacterIds,...message}){return message;}
 function privateMessage(targetCharacterId,text){
  const targetMember=members.get(targetCharacterId);
  if(!targetMember)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  const message={id:++messageSequence,messageId:randomUUID(),characterId:null,sessionId:'system',
   uid:'0',name:'Polícia Militar',firstName:'Polícia',isAdmin:false,channel:'system',
   text,targetCharacterId,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();return message;
 }
 function broadcast(identity,channel,text){
  prune();
  const sender=members.get(identity.characterId);
  if(!sender)reject(409,'NOT_IN_ROOM','Entre na cidade antes de enviar a mensagem.');
  if(channel==='megaphone'&&![...policeCars.values()].some(car=>car.ownerUid===identity.uid&&Math.hypot(car.x-sender.x,car.z-sender.z)<=8))
   reject(409,'POLICE_CAR_MISSING','Aproxime-se da sua viatura para usar o megafone.');
  const recipients=[...members.values()].filter(m=>{
   if(channel==='admin')return m.adminLevel>0;
   if(channel==='police')return m.policeDuty&&m.orgCode==='pm';
   if(channel==='faction')return m.orgCode===sender.orgCode;
   if(channel==='departments')return !!m.orgCode;
   if(channel==='megaphone')return Math.hypot(m.x-sender.x,m.y-sender.y,m.z-sender.z)<=70;
   return true;
  }).map(m=>m.characterId);
  if(!recipients.includes(identity.characterId))recipients.push(identity.characterId);
  const message={id:++messageSequence,messageId:randomUUID(),characterId:identity.characterId,
   uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],
   isAdmin:identity.accountRole==='admin',channel,text,recipientCharacterIds:recipients,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();
  return {name:channel,status:'ok',message:`Mensagem enviada (${recipients.length} participante(s)).`};
 }
 function setFollowerBot(bot){
  if(bot&&(!/^[0-9a-f-]{36}$/i.test(bot.characterId)||!/^[0-9a-f-]{36}$/i.test(bot.ownerCharacterId)||!bot.uid))
   throw new Error('Invalid follower bot');
  if(followerBot)members.delete(followerBot.characterId);
  followerBot=bot;
 }
 function advanceFollower(owner){
  const now=clock(),previous=members.get(followerBot.characterId);
  // Keep the test account present in the room without moving after the first spawn.
  if(previous){previous.seen=now;previous.sequence++;return;}
  const angle=owner.yaw*Math.PI/180;
  const targetX=owner.x-Math.sin(angle)*8,targetZ=owner.z-Math.cos(angle)*8;
  const x=previous?.x??targetX,z=previous?.z??targetZ;
  const dx=targetX-x,dz=targetZ-z,distance=Math.hypot(dx,dz);
  const elapsed=Math.min(0.25,Math.max(0,(now-(previous?.seen??now))/1000));
  const jumpToOwner=!previous||distance>35;
  const step=jumpToOwner?distance:Math.min(distance,2.8*elapsed);
  const moving=!jumpToOwner&&step>0.002;
  const next={characterId:followerBot.characterId,uid:followerBot.uid,name:followerBot.name,
   firstName:followerBot.firstName,model:followerBot.model||'bybeyby',world:owner.world,
   x:distance>0?x+dx/distance*step:x,y:owner.y,z:distance>0?z+dz/distance*step:z,
   yaw:moving?((Math.atan2(dx,dz)*180/Math.PI+360)%360):previous?.yaw??owner.yaw,
   speed:moving?2.8:0,jumping:false,jumpPhase:0,driving:false,
   sequence:(previous?.sequence??0)+1,seen:now,sessionId:'bot',clientId:'bot',adminLevel:0,
   policeDuty:false,radioChannel:0,voice:'',voiceSequence:0,radioVoice:'',radioSequence:0,isBot:true};
  members.set(followerBot.characterId,next);
 }
 function online(uid){return [...members.values()].find(member=>member.uid===uid)||null;}
 function releaseCustody(characterId){
  restraints.delete(characterId);
  for(const [id,record] of restraints)if(record.officerCharacterId===characterId)restraints.delete(id);
  for(const [id,car] of policeCars){
   if(id===characterId)policeCars.delete(id);
   else if(car.occupantCharacterId===characterId)car.occupantCharacterId=null;
  }
 }
 function custody(identity,name,targetUid){
  prune();
  const officer=members.get(identity.characterId);
  if(!officer?.policeDuty||identity.organization?.code!=='pm')reject(403,'POLICE_ONLY','Entre em serviço na Polícia Militar.');
  if(name==='stay'){
   const detained=[...restraints].find(([,record])=>record.officerCharacterId===identity.characterId&&record.following);
   if(!detained)reject(409,'NO_ESCORT','Você não está conduzindo ninguém.');
   detained[1].following=false;
   return {name,status:'ok',message:`Condução de ID ${members.get(detained[0])?.uid||'?'} interrompida. As algemas permanecem.`};
  }
  const target=online(targetUid);
  if(!target)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(target.characterId===identity.characterId)reject(400,'INVALID_TARGET','Você não pode usar algemas em si mesmo.');
  if(target.driving||officer.driving)reject(409,'VEHICLE_ACTIVE','Saia do veículo antes de usar as algemas.');
  if(Math.hypot(target.x-officer.x,target.y-officer.y,target.z-officer.z)>4)
   reject(409,'TARGET_TOO_FAR','Aproxime-se a até 4 metros do jogador.');
  const record=restraints.get(target.characterId);
  if([...policeCars.values()].some(car=>car.occupantCharacterId===target.characterId))
   reject(409,'DETAINEE_IN_CAR','Retire o detido da viatura com /eject antes de usar este comando.');
  if(name==='cuff'){
   if(record)reject(409,'ALREADY_CUFFED','Este jogador já está algemado.');
   restraints.set(target.characterId,{officerCharacterId:identity.characterId,following:false});
   return {name,status:'ok',message:`ID ${target.uid} algemado.`};
  }
  if(!record)reject(409,'NOT_CUFFED','Este jogador não está algemado.');
  if(record.officerCharacterId!==identity.characterId)reject(403,'NOT_YOUR_DETAINEE','Este jogador está sob custódia de outro policial.');
  if(name==='uncuff'){
   restraints.delete(target.characterId);
   return {name,status:'ok',message:`Algemas retiradas do ID ${target.uid}.`};
  }
  if([...restraints].some(([id,item])=>id!==target.characterId&&item.officerCharacterId===identity.characterId&&item.following))
   reject(409,'ESCORT_BUSY','Interrompa a condução atual com /stay.');
  record.following=true;
  return {name,status:'ok',message:`ID ${target.uid} acompanhando o policial. Use /stay para parar.`};
 }
 function patrol(identity,name,targetUid){
  prune();
  const officer=members.get(identity.characterId);
  if(!officer?.policeDuty||identity.organization?.code!=='pm')reject(403,'POLICE_ONLY','Entre em serviço na Polícia Militar.');
  const current=policeCars.get(identity.characterId);
  if(name==='carpolicia'){
   if(officer.driving)reject(409,'VEHICLE_ACTIVE','Saia do veículo antes de chamar a viatura.');
   if(current?.occupantCharacterId)reject(409,'CAR_OCCUPIED','Retire o detido antes de reposicionar a viatura.');
   const radians=officer.yaw*Math.PI/180;
   policeCars.set(identity.characterId,{id:'pm-'+officer.uid,ownerUid:officer.uid,
    x:officer.x+Math.sin(radians)*4,y:officer.y,z:officer.z+Math.cos(radians)*4,
    yaw:officer.yaw,occupantCharacterId:null});
   return {name,status:'ok',message:'Viatura policial posicionada à sua frente.'};
  }
  if(!current)reject(409,'POLICE_CAR_MISSING','Use /carpolicia para posicionar sua viatura.');
  const target=online(targetUid);
  if(!target)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(Math.hypot(target.x-officer.x,target.y-officer.y,target.z-officer.z)>4||
     Math.hypot(target.x-current.x,target.y-current.y,target.z-current.z)>6)
   reject(409,'TARGET_TOO_FAR','Aproxime-se do detido e da viatura.');
  const record=restraints.get(target.characterId);
  if(!record||record.officerCharacterId!==identity.characterId)
   reject(403,'NOT_YOUR_DETAINEE','Algeme o detido com /cuff antes de usar a viatura.');
  if(name==='put'){
   if(current.occupantCharacterId)reject(409,'CAR_OCCUPIED','A viatura já tem um detido.');
   current.occupantCharacterId=target.characterId;record.following=false;
   target.x=current.x;target.y=current.y;target.z=current.z;target.yaw=current.yaw;target.speed=0;
   target.renderSequence=(target.renderSequence??target.sequence)+1;target.seen=clock();
   return {name,status:'ok',message:`ID ${target.uid} colocado no banco traseiro da viatura.`};
  }
  if(current.occupantCharacterId!==target.characterId)reject(409,'NOT_IN_CAR','Este jogador não está na sua viatura.');
  current.occupantCharacterId=null;
  const radians=current.yaw*Math.PI/180;
  target.x=current.x+Math.cos(radians)*2.1;target.y=current.y;target.z=current.z-Math.sin(radians)*2.1;
  target.speed=0;target.renderSequence=(target.renderSequence??target.sequence)+1;target.seen=clock();
  return {name,status:'ok',message:`ID ${target.uid} retirado da viatura. As algemas permanecem.`};
 }
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
  if(POLICE_COMMANDS.includes(name)||SERVICE_COMMANDS.includes(name))
   return externalAction({name,args,identity,sessionId});
  const playerDefinition=playerCommand(name);
  if(playerDefinition){
   args=args.trim();
   const ok=(message,extra={})=>({name,status:'ok',message,...extra});
   if(playerDefinition.missing)return {name,status:'not_implemented',message:`/${name} em preparação: ${playerDefinition.missing}.`};
   if(['s','w'].includes(name)){
    const mute=mutes.get(identity.characterId);
    if(mute&&mute.until>clock())reject(403,'PLAYER_MUTED','Seu chat está silenciado pela administração.');
    let text=args,recipients;
    if(name==='w'){
     const match=args.match(/^([1-9][0-9]{0,18})\s+(.+)$/u);
     if(!match)reject(400,'COMMAND_USAGE','Uso: /w [ID] [mensagem]');
     const found=[...members.values()].find(m=>m.uid===match[1]);
     if(!found)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
     text=match[2];recipients=[member.characterId,found.characterId];
    }else recipients=[...members.values()].filter(m=>Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=50).map(m=>m.characterId);
    if(!text)reject(400,'COMMAND_USAGE','Uso: '+playerDefinition.usage);
    const message={id:++messageSequence,messageId:body.messageId,characterId:identity.characterId,sessionId,clientId:body.clientId,
     uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],isAdmin:identity.accountRole==='admin',
     channel:name==='s'?'shout':'whisper',text,recipientCharacterIds:recipients,sentAt:clock()};
    messages.push(message);while(messages.length>60)messages.shift();
    return ok(name==='s'?'Grito enviado em até 50 metros.':'Sussurro enviado em privado.');
   }
   if(name==='report'||name==='myreports'){
    if(name==='myreports'&&args)reject(400,'COMMAND_USAGE','Uso: /myreports');
    return externalAction({name,args,identity,sessionId});
   }
   if(args)reject(400,'COMMAND_USAGE','Uso: '+playerDefinition.usage);
   if(name==='ajuda')return {name,status:'open_help',message:'Guia do jogador aberto.',help:playerHelp()};
   if(name==='headmove')return {name,status:'headmove',message:'Movimento da cabeça alternado.'};
   return {name,status:'player_panel',message:'Abrindo '+playerDefinition.description.toLowerCase()};
  }
  const definition=adminCommand(name);
  if(!definition)reject(400,'UNKNOWN_COMMAND','Comando desconhecido. Use /ajuda.');
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
  if(name==='helpadmin'){
   if(parts.length)usage();
   return {name,status:'open_help',message:'Guia de comandos aberto.',help:gameCommandHelp()};
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
   const match=text.match(/^\/([a-z0-9]+)(?:\s+(.+))?$/iu);
   if(!match)reject(400,'UNKNOWN_COMMAND','Comando inválido. Use /ajuda.');
   const policeCommand=POLICE_COMMANDS.includes(match[1].toLowerCase());
   if(policeCommand){
    if(!member.policeDuty||identity.organization?.code!=='pm')
     reject(403,'POLICE_ONLY','Entre em serviço na Polícia Militar para usar este comando.');
   }else if(!playerCommand(match[1].toLowerCase())&&!SERVICE_COMMANDS.includes(match[1].toLowerCase())&&
     (identity.accountRole!=='admin'||!Number.isInteger(identity.adminLevel)||identity.adminLevel<1))
    reject(403,'ADMIN_ONLY','Este comando é exclusivo da administração.');
   if(adminCommand(match[1].toLowerCase())){const count=adminUsage.get(identity.characterId)||0;adminUsage.set(identity.characterId,count+1);}
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
  if(member&&member.sessionId===sessionId&&(!clientId||member.clientId===clientId)){members.delete(characterId);policeDuty.delete(characterId);policeChannels.delete(characterId);releaseCustody(characterId);}
  prune();return {ok:true};
 }
 function removeSession(sessionId){for(const [id,m] of members)if(m.sessionId===sessionId){members.delete(id);policeDuty.delete(id);policeChannels.delete(id);releaseCustody(id);}}
 function duty(identity,sessionId,body){
  prune();
  if(identity.accountRole!=='admin'||!Number.isInteger(identity.adminLevel)||identity.adminLevel<1)
   reject(403,'ADMIN_REQUIRED','Apenas administradores podem entrar em serviço policial.');
  const member=members.get(identity.characterId);
  if(!member||member.sessionId!==sessionId)reject(409,'NOT_IN_ROOM','Entre na cidade antes de alterar o serviço policial.');
  if(body!==undefined){
   if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==1||typeof body.enabled!=='boolean')
    reject(400,'INVALID_DUTY','Estado de serviço inválido.');
   if(body.enabled)policeDuty.set(identity.characterId,sessionId);
   else {policeDuty.delete(identity.characterId);policeChannels.delete(identity.characterId);releaseCustody(identity.characterId);}
  }
  member.policeDuty=policeDuty.get(identity.characterId)===sessionId;
  if(!member.policeDuty){member.radioFrames=[];member.radioVoice='';}
  member.radioChannel=policeChannels.get(identity.characterId)?.channel||0;
  return {policeDuty:member.policeDuty,radioChannel:member.radioChannel};
 }
 function radio(identity,sessionId,body){
  duty(identity,sessionId);
  const member=members.get(identity.characterId);
  if(!member.policeDuty)reject(403,'RADIO_FORBIDDEN','Entre em serviço policial para acessar o rádio.');
  if(body!==undefined){
   if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==1||
      !Number.isInteger(body.channel)||body.channel<0||body.channel>3)
    reject(400,'INVALID_CHANNEL','Selecione um canal de 1 a 3, ou 0 para desconectar.');
   if(body.channel)policeChannels.set(identity.characterId,{sessionId,channel:body.channel});
   else policeChannels.delete(identity.characterId);
   member.radioVoice='';
   member.radioFrames=[];
  }
  member.radioChannel=policeChannels.get(identity.characterId)?.channel||0;
  return {radioChannel:member.radioChannel,policeDuty:true};
 }
 return {sync,chat,leave,removeSession,findOnline:online,duty,radio,setFollowerBot,privateMessage,broadcast,custody,patrol};
}
