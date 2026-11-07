// Small, authenticated two-player presence room. Positions are client simulated:
// this prototype does not grant authority over inventory, money or progression.
import {PLAYER_COMMANDS,playerCommand} from './player-command-catalog.mjs';
import {randomUUID} from 'node:crypto';
import {isPositionWithinWorld} from './world-bounds.mjs';
import {ADMIN_COMMANDS as COMMAND_CATALOG,adminCommand,adminCommandAllowed,adminHelp,gameCommandHelp} from './admin-command-catalog.mjs';
export class RoomError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new RoomError(status,code,message);};
export const ADMIN_COMMANDS=Object.freeze(COMMAND_CATALOG.map(command=>command.name));
export const POLICE_OPERATIONS=Object.freeze([
 'delegacia','cela','prender','soltar','ocorrencias','chamados','despachar190','gpsocorrencia','encerrar190','cancelarmulta','revistar','documentos',
 'placa','apreender','arsenal','giroflex','sirene'
]);
export const POLICE_PUBLIC=Object.freeze(['servico','pmrecrutar','pmpatente','pmdemitir','minhasmultas','pagarmulta','cancelar190']);
export const POLICE_COMMANDS=Object.freeze(['ide','wanted','su','clear','cuff','uncuff','follow','stay','put','eject','carpolicia','ticket','proc','m','f','d','gov',...POLICE_OPERATIONS]);
export const SERVICE_COMMANDS=Object.freeze(['190',...POLICE_PUBLIC]);
export const canPoliceDuty=identity=>identity.adminLevel!==4 && (identity.organization?.code==='pm'||
 identity.accountRole==='admin'&&Number.isInteger(identity.adminLevel)&&identity.adminLevel>0);
export function createRoom({clock=Date.now,ttl=10000,capacity=2,persistDamage=async({health,damage})=>Math.max(0,health-damage),persistRespawn=async()=>{},spawnForWorld=()=>({x:2489.3,y:12.37,z:1668.5,yaw:40})}={}){
 const members=new Map();
 let followerBot=null;
 const policeDuty=new Map();
 const policeChannels=new Map();
 const restraints=new Map();
 const policeCars=new Map();
 const combatStates=new Map(),attackResults=new Map(),shotResults=new Map(),respawnResults=new Map();let combatSequence=0,combatQueue=Promise.resolve();
 const combatEpoch=randomUUID();
 const messages=[];const chatEpoch=randomUUID();let messageSequence=0,teleportSequence=0;
 const mutes=new Map(),adminUsage=new Map(),commandResults=new Map();let serverHour=-1;
 function prune(){for(const [key,member] of members)if(!member.isBot&&clock()-member.seen>=ttl){releaseDriver(key);members.delete(key);policeDuty.delete(key);policeChannels.delete(key);releaseCustody(key);}
  for(const member of members.values())if(member.isBot&&member.ephemeral){
   const state=combatStates.get(member.characterId);
   if(state?.dead&&clock()>=state.deathAt+5000){
    const spawn=member.botSpawn||spawnForWorld(member.world);
    if(isPositionWithinWorld(member.world,spawn))restoreCombatAtSpawn(member,state,spawn);
   }
  }
  if(followerBot&&!members.has(followerBot.ownerCharacterId)){releaseCustody(followerBot.characterId);members.delete(followerBot.characterId);}
  const testBot=followerBot&&members.get(followerBot.characterId);
  if(testBot?.jailed&&testBot.botJailUntil<=clock())Object.assign(testBot,{x:1546,y:12.62,z:1676,yaw:90,jailed:false,botJailUntil:0,renderSequence:(testBot.renderSequence??testBot.sequence)+1});
  while(messages.length&&clock()-messages[0].sentAt>900000)messages.shift();
  for(const [key,mute] of mutes)if(mute.until<=clock())mutes.delete(key);
  for(const [key,result] of commandResults)if(clock()-result.sentAt>900000)commandResults.delete(key);
  for(const [key,result] of attackResults)if(clock()-result.at>900000)attackResults.delete(key);
  for(const map of [shotResults,respawnResults])for(const [key,result] of map)if(clock()-result.at>900000)map.delete(key);
  for(const [key,value] of combatStates)if(!members.has(key)&&clock()-value.seen>900000)combatStates.delete(key);}
 function combatState(identity){
  let value=combatStates.get(identity.characterId);
  if(!value){
   const health=typeof identity.health==='number'&&Number.isFinite(identity.health)?Math.max(0,Math.min(100,identity.health)):100;
   value={epoch:combatEpoch,revision:0,attackSequence:0,attackId:'',attackAt:0,hitSequence:0,hitAttackId:'',hitAt:0,deathSequence:0,deathAt:0,respawnSequence:0,respawnAt:0,respawnX:0,respawnY:0,respawnZ:0,respawnYaw:0,health,dead:health<=0,seen:clock()};
   if(value.dead){value.deathAt=clock();value.deathSequence=++combatSequence;value.revision=value.deathSequence;}
   combatStates.set(identity.characterId,value);
  }
  value.seen=clock();return value;
 }
 function publicCombat(identity){const {seen,...value}=combatState(identity);return {...value,respawnAt:value.dead?value.deathAt+5000:0};}
 function serializeCombat(operation){const next=combatQueue.then(operation);combatQueue=next.catch(()=>{});return next;}
 const validUuid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
 function authenticatedActor(identity,sessionId,clientId){
  const actor=members.get(identity.characterId);
  if(!actor||actor.sessionId!==sessionId||actor.clientId!==clientId)reject(409,'NOT_IN_ROOM','Entre na cidade antes de agir.');
  if(!actor.combatClient)reject(409,'COMBAT_CLIENT_REQUIRED','Atualize o cliente antes de usar o combate.');
  return actor;
 }
 function shoot(identity,sessionId,body){return serializeCombat(()=>applyShot(identity,sessionId,body));}
 async function applyShot(identity,sessionId,body){
  prune();
  if(Number(identity.jailUntil)>clock())reject(403,'PLAYER_JAILED','Você está cumprindo pena.');
  const keys=['clientId','shotId','originX','originY','originZ','directionX','directionY','directionZ','targetUid'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(key=>!keys.includes(key))||
   !validUuid(body.clientId)||!validUuid(body.shotId)||typeof body.targetUid!=='string'||body.targetUid.length>24||
   ['originX','originY','originZ','directionX','directionY','directionZ'].some(key=>!Number.isFinite(body[key])))
   reject(400,'INVALID_SHOT','Disparo inválido.');
  const actor=authenticatedActor(identity,sessionId,body.clientId),cacheKey=sessionId+':'+body.clientId+':'+body.shotId;
  if(shotResults.has(cacheKey))return shotResults.get(cacheKey).reply;
  const state=combatState(identity);
  if(actor.policeDuty&&(!actor.policeWeapon||actor.policeWeapon!==actor.weaponType))
   reject(403,'POLICE_WEAPON_REQUIRED','Retire seu equipamento no arsenal da PM.');
  if(state.dead||restraints.has(identity.characterId)||actor.driving||actor.weaponEquipped!==true||!actor.respawnClient||
    [...policeCars.values()].some(car=>car.driverCharacterId===identity.characterId||car.occupantCharacterId===identity.characterId))
   reject(409,'SHOT_BLOCKED','Você não pode atirar neste estado.');
  if(clock()-actor.seen>1500)reject(409,'STALE_COMBAT_POSE','Aguarde a conexão de movimento antes de atirar.');
  if(state.lastShotAt!==undefined&&clock()-state.lastShotAt<120)reject(429,'SHOT_COOLDOWN','Aguarde o próximo disparo.');
  const ox=body.originX,oy=body.originY,oz=body.originZ,dx=body.directionX,dy=body.directionY,dz=body.directionZ;
  const length=Math.hypot(dx,dy,dz);
  if(length<.99||length>1.01||Math.hypot(ox-actor.x,oz-actor.z)>2.2||oy<actor.y-.3||oy>actor.y+2.5)
   reject(400,'INVALID_SHOT_RAY','Origem ou direção do disparo inválida.');
  // Static map geometry is not hosted by this API. The client sends only its
  // first unobstructed collider, while the server independently checks capsule,
  // range and nearby origin. Never accept a damage amount or arbitrary health.
  const target=body.targetUid?[...members.values()].find(candidate=>candidate.uid===body.targetUid):null;
  if(body.targetUid&&(!target||target.characterId===identity.characterId||target.world!==actor.world||
   clock()-target.seen>1500||(!target.combatClient&&!target.isBot)||(!target.respawnClient&&!target.isBot)||combatState(target).dead||target.driving||target.jailed))
   reject(409,'INVALID_SHOT_TARGET','Alvo indisponível.');
  if(target){
   const tx=target.x-ox,ty=target.y+.95-oy,tz=target.z-oz,along=tx*dx+ty*dy+tz*dz;
   const radius=Math.hypot(tx-dx*along,Math.max(0,Math.abs(ty-dy*along)-.65),tz-dz*along);
   if(along<0||along>160||radius>.55)reject(400,'SHOT_MISALIGNED','O alvo não está na trajetória do disparo.');
  }
  const targetState=target?combatState(target):null;
  let nextHealth=targetState?.health;
  if(target){nextHealth=target.ephemeral?Math.max(0,targetState.health-25):await persistDamage({characterId:target.characterId,health:targetState.health,damage:25,attackerCharacterId:identity.characterId,attackId:body.shotId});
   if(!Number.isFinite(nextHealth)||nextHealth<0||nextHealth>100)throw new Error('Invalid persisted rifle health');}
  const at=clock();state.lastShotAt=at;
  if(target){
   targetState.health=nextHealth;targetState.dead=nextHealth<=0;targetState.hitSequence=++combatSequence;
   targetState.hitAttackId=body.shotId;targetState.hitAt=at;targetState.hitReactions=1;targetState.revision=targetState.hitSequence;
   target.speed=0;target.jumping=false;target.jumpPhase=0;target.renderSequence=(target.renderSequence??target.sequence)+1;
   if(targetState.dead){targetState.deathSequence=++combatSequence;targetState.deathAt=at;targetState.revision=targetState.deathSequence;releaseDriver(target.characterId);releaseCustody(target.characterId);}
  }
  const reply={combatAvailable:true,rifleDamageAvailable:true,respawnAvailable:true,serverTime:at,selfCombat:publicCombat(identity),targetUid:target?.uid||'',damage:target?25:0,
   combatPeers:target?[{uid:target.uid,combat:publicCombat(target)}]:[]};
  shotResults.set(cacheKey,{at,reply});return reply;
 }
 function respawn(identity,sessionId,body){return serializeCombat(()=>applyRespawn(identity,sessionId,body));}
 async function applyRespawn(identity,sessionId,body){
  if(Number(identity.jailUntil)>clock())reject(403,'PLAYER_JAILED','Você está cumprindo pena.');
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==4||!validUuid(body.clientId)||
   !validUuid(body.requestId)||body.epoch!==combatEpoch||!Number.isSafeInteger(body.deathSequence)||body.deathSequence<=0)
   reject(400,'INVALID_RESPAWN','Pedido de renascimento inválido.');
  const actor=authenticatedActor(identity,sessionId,body.clientId),key=identity.characterId+':'+body.epoch+':'+body.deathSequence;
  if(respawnResults.has(key))return respawnResults.get(key).reply;
  const state=combatState(identity);
  if(!state.dead||state.deathSequence!==body.deathSequence)reject(409,'RESPAWN_STALE','O personagem não está aguardando esse renascimento.');
  if(clock()<state.deathAt+5000)reject(409,'RESPAWN_WAIT','Aguarde cinco segundos para renascer.');
  const spawn=spawnForWorld(identity.world);
  if(!spawn||!isPositionWithinWorld(identity.world,spawn)||!Number.isFinite(spawn.yaw)||spawn.yaw<0||spawn.yaw>=360)throw new Error('Invalid server respawn position');
  if(!actor.ephemeral)await persistRespawn({characterId:identity.characterId,world:identity.world,...spawn});
  restoreCombatAtSpawn(actor,state,spawn);
  const reply={combatAvailable:true,rifleDamageAvailable:true,respawnAvailable:true,serverTime:clock(),selfCombat:publicCombat(identity)};
  respawnResults.set(key,{at:clock(),reply});return reply;
 }
 function restoreCombatAtSpawn(actor,state,spawn){
  state.dead=false;state.health=100;state.hitAt=state.attackAt=0;state.hitSequence=state.attackSequence=0;
  state.respawnSequence=++combatSequence;state.revision=state.respawnSequence;
  state.respawnX=spawn.x;state.respawnY=spawn.y;state.respawnZ=spawn.z;state.respawnYaw=spawn.yaw;
  actor.x=spawn.x;actor.y=spawn.y;actor.z=spawn.z;actor.yaw=spawn.yaw;actor.speed=0;actor.jumping=false;actor.jumpPhase=0;
  actor.weaponEquipped=false;actor.weaponAiming=false;actor.renderSequence=(actor.renderSequence??actor.sequence)+1;
  releaseDriver(actor.characterId);releaseCustody(actor.characterId);
 }
 function attack(identity,sessionId,body){
  // One room queue serializes concurrent hits and retried HTTP requests. Only the
  // persisted health is published, so a failed database write never creates a hit.
  const next=combatQueue.then(()=>applyAttack(identity,sessionId,body));
  combatQueue=next.catch(()=>{});return next;
 }
 async function applyAttack(identity,sessionId,body){
  prune();
  if(Number(identity.jailUntil)>clock())reject(403,'PLAYER_JAILED','Você está cumprindo pena.');
  const uuid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==2||!uuid(body.clientId)||!uuid(body.attackId))
   reject(400,'INVALID_ATTACK','Ataque inválido.');
  const actor=members.get(identity.characterId);
  if(!actor||actor.sessionId!==sessionId||actor.clientId!==body.clientId)
   reject(409,'NOT_IN_ROOM','Entre na cidade antes de atacar.');
  if(actor.combatClient!==true)reject(409,'COMBAT_CLIENT_REQUIRED','Atualize o cliente e sincronize o combate antes de atacar.');
  const cacheKey=sessionId+':'+body.clientId+':'+body.attackId;
  if(attackResults.has(cacheKey))return attackResults.get(cacheKey).reply;
  const state=combatState(identity);
  if(state.dead||restraints.has(identity.characterId)||actor.driving||actor.jumping||
    [...policeCars.values()].some(car=>car.driverCharacterId===identity.characterId||car.occupantCharacterId===identity.characterId))
   reject(409,'ATTACK_BLOCKED','Você não pode atacar neste estado.');
  if(clock()-actor.seen>1500)reject(409,'STALE_COMBAT_POSE','Aguarde a conexão de movimento antes de atacar.');
  if(state.attackSequence&&clock()-state.attackAt<2200)reject(429,'ATTACK_COOLDOWN','Aguarde o próximo combo.');
  const radians=actor.yaw*Math.PI/180,forwardX=Math.sin(radians),forwardZ=Math.cos(radians);
  const target=[...members.values()].filter(candidate=>{
   if(candidate.characterId===identity.characterId||candidate.world!==actor.world||clock()-candidate.seen>1500||
      candidate.combatClient!==true&&!candidate.isBot||
      candidate.driving||candidate.jailed||candidate.invisible&&actor.adminLevel<=0||combatState(candidate).dead||
      [...policeCars.values()].some(car=>car.driverCharacterId===candidate.characterId||car.occupantCharacterId===candidate.characterId))return false;
   const dx=candidate.x-actor.x,dz=candidate.z-actor.z,distance=Math.hypot(dx,dz);
   return distance<=2.2&&Math.abs(candidate.y-actor.y)<=1.5&&
    (distance<.15||(dx*forwardX+dz*forwardZ)/distance>=.5);
  }).sort((a,b)=>Math.hypot(a.x-actor.x,a.z-actor.z)-Math.hypot(b.x-actor.x,b.z-actor.z))[0];
  const targetState=target?combatState(target):null;
  let nextHealth=targetState?.health;
  if(target){
   nextHealth=target.ephemeral?Math.max(0,targetState.health-20):
    await persistDamage({characterId:target.characterId,health:targetState.health,damage:20,attackerCharacterId:identity.characterId,attackId:body.attackId});
   if(typeof nextHealth!=='number'||!Number.isFinite(nextHealth)||nextHealth<0||nextHealth>100)
    throw new Error('Invalid persisted combat health');
  }
  const at=clock();
  state.attackSequence=++combatSequence;state.revision=state.attackSequence;state.attackId=body.attackId;state.attackAt=at;
  const currentActor=members.get(identity.characterId);if(currentActor)currentActor.renderSequence=(currentActor.renderSequence??currentActor.sequence)+1;
  if(target){
   targetState.health=nextHealth;targetState.dead=nextHealth<=0;
   targetState.hitSequence=++combatSequence;targetState.hitAttackId=body.attackId;targetState.hitAt=at;targetState.hitReactions=3;targetState.revision=targetState.hitSequence;
   if(targetState.dead){
    targetState.deathSequence=++combatSequence;targetState.deathAt=at;targetState.revision=targetState.deathSequence;releaseDriver(target.characterId);
    for(const [detainedId,record] of restraints)if(detainedId===target.characterId||record.officerCharacterId===target.characterId){
     record.following=false;
     const detained=members.get(detainedId);if(detained){detained.speed=0;detained.renderSequence=(detained.renderSequence??detained.sequence)+1;}
    }
   }
   const currentTarget=members.get(target.characterId);
   if(currentTarget){currentTarget.renderSequence=(currentTarget.renderSequence??currentTarget.sequence)+1;
    currentTarget.speed=0;currentTarget.jumping=false;currentTarget.jumpPhase=0;}
  }
  const reply={combatAvailable:true,serverTime:at,selfCombat:publicCombat(identity),targetUid:target?.uid||'',damage:target?20:0,
   combatPeers:target?[{uid:target.uid,combat:publicCombat(target)}]:[]};
  attackResults.set(cacheKey,{at,reply});return reply;
 }
 function sync(identity,sessionId,body){
  prune();
  const keys=['clientId','sequence','x','y','z','yaw','speed','jumping','jumpPhase','afterChat','afterTeleport','afterRespawn','driving','voice','voiceSequence','radioVoice','radioSequence','radioChannel','voiceActive','radioActive','policeCarId','combatClient','weaponEquipped','weaponAiming','weaponShotSequence','weaponAimPitch','weaponType','animationClip','animationSequence','prisonReady'];
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!keys.includes(k))||
    !/^[a-f0-9-]{36}$/i.test(body.clientId||'')||!Number.isSafeInteger(body.sequence)||body.sequence<0||body.sequence>2147483647)
   reject(400,'INVALID_FRAME','Atualização de movimento inválida.');
  if(body.afterChat!==undefined&&(!Number.isSafeInteger(body.afterChat)||body.afterChat<0))reject(400,'INVALID_FRAME','Cursor de chat inválido.');
  if(body.driving!==undefined&&typeof body.driving!=='boolean')reject(400,'INVALID_FRAME','Estado do veículo inválido.');
  if(body.combatClient!==undefined&&typeof body.combatClient!=='boolean')reject(400,'INVALID_FRAME','Capacidade de combate inválida.');
  for(const key of ['weaponEquipped','weaponAiming'])if(body[key]!==undefined&&typeof body[key]!=='boolean')reject(400,'INVALID_FRAME','Estado visual de arma inválido.');
  if(body.prisonReady!==undefined&&typeof body.prisonReady!=='boolean')reject(400,'INVALID_FRAME','Estado da custódia inválido.');
  if(body.weaponType!==undefined&&!['rifle','pistol'].includes(body.weaponType))reject(400,'INVALID_FRAME','Tipo de arma inválido.');
  if(body.animationClip!==undefined&&!['','fight_d.FightD_IDLE','fight_c.FightC_M','fight_c.FightC_2','fight_b.FightB_IDLE','ped.IDLE_taxi','ped.GunCrouchFwd','ped.gas_cwr'].includes(body.animationClip)||
    body.animationSequence!==undefined&&(!Number.isSafeInteger(body.animationSequence)||body.animationSequence<0||body.animationSequence>2147483647))
   reject(400,'INVALID_FRAME','Animação inválida.');
  if(body.weaponAiming&&!body.weaponEquipped||body.weaponShotSequence!==undefined&&(!Number.isSafeInteger(body.weaponShotSequence)||body.weaponShotSequence<0||body.weaponShotSequence>2147483647)||
    body.weaponAimPitch!==undefined&&(typeof body.weaponAimPitch!=='number'||!Number.isFinite(body.weaponAimPitch)||Math.abs(body.weaponAimPitch)>80))reject(400,'INVALID_FRAME','Estado visual de arma inválido.');
  if(body.policeCarId!==undefined&&(typeof body.policeCarId!=='string'||body.policeCarId.length>64||
    (body.policeCarId!==''&&!/^pm-[1-9][0-9]{0,18}$/.test(body.policeCarId))))reject(400,'INVALID_FRAME','Viatura inválida.');
  for(const key of ['voiceActive','radioActive'])if(body[key]!==undefined&&typeof body[key]!=='boolean')reject(400,'INVALID_FRAME','Estado do microfone inválido.');
  if(body.voiceActive&&body.radioActive)reject(400,'INVALID_FRAME','Transmissão de voz ambígua.');
  if(body.afterTeleport!==undefined&&(!Number.isSafeInteger(body.afterTeleport)||body.afterTeleport<0))reject(400,'INVALID_FRAME','Cursor de teleporte inválido.');
  if(body.afterRespawn!==undefined&&(!Number.isSafeInteger(body.afterRespawn)||body.afterRespawn<0))reject(400,'INVALID_FRAME','Cursor de renascimento inválido.');
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
  const selfCombat=combatState(identity);
  const restraint=restraints.get(identity.characterId);
  const jailed=Number(identity.jailUntil)>clock();
  const isPolice=!jailed&&canPoliceDuty(identity)&&policeDuty.get(identity.characterId)===sessionId;
  if(!isPolice&&policeDuty.has(identity.characterId)){
   if(jailed||!canPoliceDuty(identity))revokePoliceDuty(identity.characterId);
   else {policeDuty.delete(identity.characterId);policeChannels.delete(identity.characterId);releaseDriver(identity.characterId);}
  }
  let driven=[...policeCars.values()].find(car=>car.driverCharacterId===identity.characterId);
  if(driven&&(driven.driverSessionId!==sessionId||identity.organization?.code!=='pm'||restraint||selfCombat.dead))
   {releaseDriver(identity.characterId);driven=null;}
  if(!jailed&&body.policeCarId&&(!driven||driven.id!==body.policeCarId||body.driving!==true))
   reject(403,'POLICE_CAR_FORBIDDEN','Entre na sua viatura antes de conduzir.');
  if(!jailed&&body.radioVoice&&(!isPolice||policeChannels.get(identity.characterId)?.sessionId!==sessionId||
    body.radioChannel!==policeChannels.get(identity.characterId)?.channel))
   reject(403,'RADIO_FORBIDDEN','Conecte-se ao rádio policial antes de transmitir.');
  if(!jailed&&body.radioActive&&(!isPolice||policeChannels.get(identity.characterId)?.sessionId!==sessionId||!policeChannels.get(identity.characterId)?.channel))
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
   voiceActive:body.voiceActive===true,radioActive:body.radioActive===true&&isPolice,
   combatClient:body.combatClient===true,
   respawnClient:Number.isSafeInteger(body.afterRespawn),
   uid:identity.uid,name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],gender:identity.gender==='female'?'female':'male',model:identity.model,world:identity.world,
   jailed,prisonReady:body.prisonReady===true,policeDuty:isPolice,orgCode:identity.organization?.code||'',policeCarId:driven?.id||'',
   policeWeapon:isPolice&&previous?.sessionId===sessionId?previous.policeWeapon:null,
   adminLevel:identity.accountRole==='admin'?identity.adminLevel:0,
   invisible:identity.accountRole==='admin'&&(identity.adminLevel===1||identity.adminLevel>=3)&&identity.adminLevel!==4&&(previous?.invisible||false),teleport:identity.adminLevel===4?null:previous?.teleport||null};
  // Keep a small recovery window for clients polling at different phases or over mobile jitter.
  // Legacy voice/radioVoice fields remain unchanged for installed clients.
  const sameSource=previous?.sessionId===sessionId&&previous?.clientId===body.clientId;
  member.voiceFrames=voiceWindow(sameSource?previous.voiceFrames:[],body.voice,body.voiceSequence);
  member.radioFrames=voiceWindow(sameSource&&isPolice&&previous.radioChannel===member.radioChannel?previous.radioFrames:[],body.radioVoice,body.radioSequence);
  members.set(identity.characterId,member);
  if(jailed){
   Object.assign(member,{x:identity.x,y:identity.y,z:identity.z,yaw:identity.yaw,speed:0,
    jumping:false,jumpPhase:0,driving:false,policeCarId:'',teleport:null,weaponEquipped:false,weaponAiming:false,
    voiceActive:false,voiceFrames:[],voice:''});
  }
  if(!jailed&&selfCombat.respawnSequence>(body.afterRespawn??0)){
   member.x=selfCombat.respawnX;member.y=selfCombat.respawnY;member.z=selfCombat.respawnZ;member.yaw=selfCombat.respawnYaw;
   member.speed=0;member.jumping=false;member.jumpPhase=0;member.driving=false;
  }
  if(!jailed&&selfCombat.dead){
   if(previous){member.x=previous.x;member.y=previous.y;member.z=previous.z;member.yaw=previous.yaw;}
   member.speed=0;member.jumping=false;member.jumpPhase=0;member.driving=false;
  }
  if(restraint){
   const officer=members.get(restraint.officerCharacterId);
   if(!officer){restraints.delete(identity.characterId);}
   else if(previous){member.x=previous.x;member.y=previous.y;member.z=previous.z;member.yaw=previous.yaw;member.speed=0;member.jumping=false;member.jumpPhase=0;member.driving=false;}
  }
  if(driven){
   // Enter/exit are server-owned. Frames without the new field during the entry
   // animation keep the parked car still instead of moving it with the actor.
   if(body.policeCarId){driven.x=member.x;driven.y=member.y;driven.z=member.z;driven.yaw=member.yaw;}
   else {member.x=driven.x;member.y=driven.y;member.z=driven.z;member.yaw=driven.yaw;member.speed=0;}
   member.driving=true;member.jumping=false;member.jumpPhase=0;
   const detained=members.get(driven.occupantCharacterId);
   if(detained){detained.x=driven.x;detained.y=driven.y;detained.z=driven.z;detained.yaw=driven.yaw;
    detained.speed=0;detained.renderSequence=(detained.renderSequence??detained.sequence)+1;detained.seen=clock();}
  }
  for(const [detainedId,record] of restraints)if(record.following&&record.officerCharacterId===identity.characterId){
   const detained=members.get(detainedId);if(!detained)continue;
   const detainedCombat=combatState(detained);
   if(selfCombat.dead||detainedCombat.dead){record.following=false;detained.speed=0;continue;}
   if(selfCombat.hitSequence&&clock()-selfCombat.hitAt<1600||detainedCombat.hitSequence&&clock()-detainedCombat.hitAt<1600){detained.speed=0;continue;}
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
  return {room:'centro_teste',voiceStateAvailable:true,policeVehicleAvailable:true,combatAvailable:true,weaponStateAvailable:true,pistolStateAvailable:true,animationSyncAvailable:true,prisonAvailable:true,rifleDamageAvailable:true,respawnAvailable:true,selfCombat:publicCombat(identity),selfAccountRole:identity.accountRole||'user',selfAdminLevel:identity.adminLevel||0,capacity,count:members.size,serverTime:clock(),sequence:body.sequence,chatEpoch,serverHour,hourOverride:serverHour>=0,
   selfPoliceDuty:isPolice,selfPoliceDutyAvailable:true,selfOrganization:identity.organization??null,
   selfJailUntil:jailed?identity.jailUntil:0,selfCuffed:restraints.has(identity.characterId),selfEscortedBy:restraints.get(identity.characterId)?.following?members.get(restraints.get(identity.characterId).officerCharacterId)?.uid||'':'',
   selfX:member.x,selfY:member.y,selfZ:member.z,selfYaw:member.yaw,
   selfInPoliceCar:!!seat,
   selfPoliceCarId:driven?.id||'',
   policeCars:[...policeCars.values()].map(car=>({id:car.id,ownerUid:car.ownerUid,x:car.x,y:car.y,z:car.z,yaw:car.yaw,
    driverUid:members.get(car.driverCharacterId)?.uid||'',driverName:members.get(car.driverCharacterId)?.firstName||'',
    occupantUid:members.get(car.occupantCharacterId)?.uid||'',occupantName:members.get(car.occupantCharacterId)?.firstName||'',
    lightbar:car.lightbar===true,siren:car.siren===true})),
   teleport:member.teleport&&member.teleport.id>(body.afterTeleport??0)?member.teleport:null,
   messages:messages.filter(m=>m.id>(body.afterChat??0)&&(m.channel!=='admin'||member.adminLevel>0)&&
    (!m.targetCharacterId||m.targetCharacterId===member.characterId)&&
    (!m.recipientCharacterIds||m.recipientCharacterIds.includes(member.characterId))).slice(-20).map(publicMessage),
   peers:[...members.values()].filter(m=>m.characterId!==identity.characterId&&(!m.invisible||member.adminLevel>0)).map(m=>({
    characterId:m.characterId,uid:m.uid,name:m.name,firstName:m.firstName,gender:m.gender||'male',model:m.model,adminLevel:m.adminLevel||0,x:m.x,y:m.y,z:m.z,combat:publicCombat(m),combatClient:m.combatClient===true||m.isBot===true,
    yaw:m.yaw,speed:m.speed,jumping:m.jumping,jumpPhase:m.jumpPhase,driving:m.driving===true,policeCarId:m.policeCarId||'',sequence:m.renderSequence??m.sequence,updatedAt:m.seen,
    weaponEquipped:m.weaponEquipped===true&&!m.driving&&!restraints.has(m.characterId)&&!publicCombat(m).dead,
    weaponAiming:m.weaponAiming===true&&m.weaponEquipped===true&&!m.driving&&!restraints.has(m.characterId)&&!publicCombat(m).dead,
    weaponShotSequence:m.weaponShotSequence||0,weaponAimPitch:m.weaponAimPitch||0,
    animationClip:m.animationClip||'',animationSequence:m.animationSequence||0,
    weaponType:m.weaponType==='pistol'?'pistol':'rifle',
   voice:m.voice&&m.seen>=clock()-500&&Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=35?m.voice:'',voiceSequence:m.voiceSequence,
   voiceFrames:Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=35?publicVoice(m.voiceFrames):[],
   voiceActive:m.voiceActive===true&&m.seen>=clock()-1500&&Math.hypot(m.x-member.x,m.y-member.y,m.z-member.z)<=35,
   radioActive:m.radioActive===true&&m.policeDuty&&member.policeDuty&&member.radioChannel>0&&m.radioChannel===member.radioChannel&&m.seen>=clock()-1500,
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
  if(followerBot){releaseCustody(followerBot.characterId);members.delete(followerBot.characterId);combatStates.delete(followerBot.characterId);}
  followerBot=bot;
 }
 function advanceFollower(owner){
  const now=clock(),previous=members.get(followerBot.characterId);
  // Keep the test account present in the room without moving after the first spawn.
  if(previous){previous.seen=now;previous.sequence++;return;}
  const angle=owner.yaw*Math.PI/180;
  const targetX=owner.x+Math.sin(angle)*(followerBot.spawnInFront?2: -8);
  const targetZ=owner.z+Math.cos(angle)*(followerBot.spawnInFront?2: -8);
  const x=previous?.x??targetX,z=previous?.z??targetZ;
  const dx=targetX-x,dz=targetZ-z,distance=Math.hypot(dx,dz);
  const elapsed=Math.min(0.25,Math.max(0,(now-(previous?.seen??now))/1000));
  const jumpToOwner=!previous||distance>35;
  const step=jumpToOwner?distance:Math.min(distance,2.8*elapsed);
  const moving=!jumpToOwner&&step>0.002;
  const next={characterId:followerBot.characterId,ownerCharacterId:followerBot.ownerCharacterId,uid:followerBot.uid,name:followerBot.name,
   health:followerBot.health,
   firstName:followerBot.firstName,gender:followerBot.gender==='female'?'female':'male',model:followerBot.model||'bybeyby',world:owner.world,
   x:distance>0?x+dx/distance*step:x,y:owner.y,z:distance>0?z+dz/distance*step:z,
   yaw:moving?((Math.atan2(dx,dz)*180/Math.PI+360)%360):previous?.yaw??owner.yaw,
   speed:moving?2.8:0,jumping:false,jumpPhase:0,driving:false,
   sequence:(previous?.sequence??0)+1,seen:now,sessionId:'bot',clientId:'bot',adminLevel:0,
   policeDuty:false,radioChannel:0,voice:'',voiceSequence:0,radioVoice:'',radioSequence:0,isBot:true,ephemeral:followerBot.ephemeral===true};
  next.botSpawn={x:next.x,y:next.y,z:next.z,yaw:next.yaw};
  members.set(followerBot.characterId,next);
 }
 function online(uid){return [...members.values()].find(member=>member.uid===uid)||null;}
 function releaseDriver(characterId){
  for(const car of policeCars.values())if(car.driverCharacterId===characterId){car.driverCharacterId=null;car.driverSessionId=null;}
  const member=members.get(characterId);
  if(member?.policeCarId){member.policeCarId='';member.driving=false;member.speed=0;}
 }
 function releaseCustody(characterId){
  releaseDriver(characterId);
  restraints.delete(characterId);
  for(const [id,record] of restraints)if(record.officerCharacterId===characterId)restraints.delete(id);
  for(const [id,car] of policeCars){
   if(id===characterId)policeCars.delete(id);
   else if(car.occupantCharacterId===characterId)car.occupantCharacterId=null;
  }
 }
 function revokePoliceDuty(characterId){
  policeDuty.delete(characterId);policeChannels.delete(characterId);
  releaseCustody(characterId);
  const member=members.get(characterId);
  if(member)Object.assign(member,{policeDuty:false,policeWeapon:null,radioChannel:0,radioVoice:'',radioFrames:[],radioActive:false});
 }
 function applyPoliceSentence(characterId){
  revokePoliceDuty(characterId);
  const member=members.get(characterId);
  if(member)Object.assign(member,{jailed:true,weaponEquipped:false,weaponAiming:false,teleport:null});
 }
 function requirePoliceCustody(identity,targetId){
  const record=restraints.get(targetId);
  if(record?.officerCharacterId!==identity.characterId||
   [...policeCars.values()].some(car=>car.occupantCharacterId===targetId))
   reject(403,'NOT_YOUR_DETAINEE','Algeme o jogador e retire-o da viatura antes de prosseguir.');
 }
 function followingDetainee(identity){
  const entry=[...restraints].find(([id,record])=>record.officerCharacterId===identity.characterId&&record.following&&members.has(id));
  return entry?members.get(entry[0]):null;
 }
 function jailTestBot(identity,minutes,point){
  const bot=followingDetainee(identity);
  if(!bot?.isBot||bot.ownerCharacterId!==identity.characterId)
   reject(409,'NOT_YOUR_DETAINEE','Conduza seu bot algemado até a cela.');
  requirePoliceCustody(identity,bot.characterId);
  restraints.delete(bot.characterId);
  Object.assign(bot,{x:point.x,y:point.y,z:point.z,yaw:point.yaw,speed:0,jailed:true,
   botJailUntil:clock()+Number(minutes)*60000,renderSequence:(bot.renderSequence??bot.sequence)+1});
  return bot;
 }
 function releaseTestBot(identity){
  const bot=online('BOT1');
  if(!bot?.isBot||bot.ownerCharacterId!==identity.characterId||!bot.jailed)
   reject(404,'SENTENCE_NOT_FOUND','Seu bot não está preso.');
  Object.assign(bot,{x:1546,y:12.62,z:1676,yaw:90,jailed:false,botJailUntil:0,
   renderSequence:(bot.renderSequence??bot.sequence)+1});
  return bot;
 }
 function teleportPoliceEscort(identity,point){
  const detained=followingDetainee(identity);
  if(!detained)return null;
  const officer=members.get(identity.characterId);
  if(detained.world!==officer?.world||Math.hypot(detained.x-officer.x,detained.y-officer.y,detained.z-officer.z)>4||detained.driving)
   reject(409,'TARGET_TOO_FAR','Aproxime o preso algemado da porta antes de entrar.');
  if(point.y< -94&&!detained.prisonReady&&!detained.isBot)
   reject(409,'PRISON_CLIENT_REQUIRED','O preso precisa atualizar o jogo para entrar na custódia.');
  Object.assign(detained,{x:point.x,y:point.y,z:point.z+1.7,yaw:point.yaw,speed:0,driving:false,
   teleport:{id:++teleportSequence,x:point.x,y:point.y,z:point.z+1.7}});
  detained.renderSequence=(detained.renderSequence??detained.sequence)+1;
  detained.seen=clock();
  return detained;
 }
 function togglePoliceEquipment(identity,name){
  const car=policeCars.get(identity.characterId);
  if(!car||car.driverCharacterId!==identity.characterId)
   reject(409,'POLICE_CAR_REQUIRED','Entre como motorista da sua viatura.');
  const field=name==='sirene'?'siren':'lightbar';
  car[field]=!car[field];
  return car[field];
 }
 function grantPoliceWeapon(identity,weapon){
  const member=members.get(identity.characterId);
  if(!member?.policeDuty)reject(403,'POLICE_ONLY','Entre em serviço na PM.');
  member.policeWeapon=weapon;
 }
 function custody(identity,name,targetUid){
  prune();
  const officer=members.get(identity.characterId);
  if(!officer?.policeDuty||identity.organization?.code!=='pm')reject(403,'POLICE_ONLY','Entre em serviço na Polícia Militar.');
  if(combatState(identity).dead&&!['stay','uncuff'].includes(name))reject(409,'PLAYER_DEAD','Você não pode executar esta ação enquanto está morto.');
  if(name==='stay'){
   const detained=[...restraints].find(([,record])=>record.officerCharacterId===identity.characterId&&record.following);
   if(!detained)reject(409,'NO_ESCORT','Você não está conduzindo ninguém.');
   detained[1].following=false;
   return {name,status:'ok',message:`Condução de ID ${members.get(detained[0])?.uid||'?'} interrompida. As algemas permanecem.`};
  }
  const target=online(targetUid);
  if(target?.isBot&&target.ownerCharacterId!==identity.characterId)reject(403,'NOT_YOUR_DETAINEE','Você só pode conduzir seu próprio bot de teste.');
  if(!target)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(name!=='uncuff'&&combatState(target).dead)reject(409,'TARGET_DEAD','Este jogador está morto e não pode ser conduzido.');
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
  if(combatState(identity).dead&&name!=='eject')reject(409,'PLAYER_DEAD','Você não pode executar esta ação enquanto está morto.');
  const current=policeCars.get(identity.characterId);
  if(name==='carpolicia'){
   if(officer.driving||current?.driverCharacterId)reject(409,'VEHICLE_ACTIVE','Saia do veículo antes de chamar a viatura.');
   if(current?.occupantCharacterId)reject(409,'CAR_OCCUPIED','Retire o detido antes de reposicionar a viatura.');
   const radians=officer.yaw*Math.PI/180;
   policeCars.set(identity.characterId,{id:'pm-'+officer.uid,ownerUid:officer.uid,
    x:officer.x+Math.sin(radians)*4,y:officer.y,z:officer.z+Math.cos(radians)*4,
    yaw:officer.yaw,occupantCharacterId:null,driverCharacterId:null,driverSessionId:null});
   return {name,status:'ok',message:'Viatura policial posicionada à sua frente.'};
  }
  if(!current)reject(409,'POLICE_CAR_MISSING','Use /carpolicia para posicionar sua viatura.');
  if(current.driverCharacterId&&(members.get(current.driverCharacterId)?.speed||0)>1)
   reject(409,'VEHICLE_MOVING','Pare a viatura antes de colocar ou retirar o detido.');
  const target=online(targetUid);
  if(target?.isBot&&target.ownerCharacterId!==identity.characterId)reject(403,'NOT_YOUR_DETAINEE','Você só pode conduzir seu próprio bot de teste.');
  if(!target)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(name==='put'&&combatState(target).dead)reject(409,'TARGET_DEAD','Este jogador está morto e não pode entrar na viatura.');
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
 function vehicle(identity,sessionId,body){
  if(Number(identity.jailUntil)>clock())reject(403,'PLAYER_JAILED','Você está cumprindo pena.');
  prune();
  if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==2||
    !['enter','exit'].includes(body.action)||typeof body.carId!=='string'||!/^pm-[1-9][0-9]{0,18}$/.test(body.carId))
   reject(400,'INVALID_VEHICLE_ACTION','Informe a viatura e a ação enter ou exit.');
  const dutyState=duty(identity,sessionId);
  if(!dutyState.policeDuty||identity.organization?.code!=='pm')reject(403,'POLICE_ONLY','Entre em serviço na Polícia Militar.');
  const officer=members.get(identity.characterId),car=policeCars.get(identity.characterId);
  if(!car||car.id!==body.carId)reject(403,'POLICE_CAR_FORBIDDEN','Você só pode conduzir sua própria viatura.');
  if(body.action==='enter'){
   if(combatState(identity).dead||restraints.has(identity.characterId)||[...policeCars.values()].some(item=>item.occupantCharacterId===identity.characterId))
    reject(409,'VEHICLE_ACTIVE','Você não pode assumir o volante nesta condição.');
   if(car.driverCharacterId===identity.characterId&&car.driverSessionId===sessionId)
    return {carId:car.id,driving:true,x:car.x,y:car.y,z:car.z,yaw:car.yaw};
   if(officer.driving||[...policeCars.values()].some(item=>item.driverCharacterId===identity.characterId)||car.driverCharacterId)
    reject(409,'VEHICLE_ACTIVE','Saia do veículo atual antes de assumir a viatura.');
   if(Math.hypot(officer.x-car.x,officer.y-car.y,officer.z-car.z)>4)
    reject(409,'TARGET_TOO_FAR','Aproxime-se a até 4 metros da sua viatura.');
   car.driverCharacterId=identity.characterId;car.driverSessionId=sessionId;
   officer.policeCarId=car.id;officer.driving=true;officer.speed=0;
   officer.renderSequence=(officer.renderSequence??officer.sequence)+1;
   return {carId:car.id,driving:true,x:car.x,y:car.y,z:car.z,yaw:car.yaw};
  }
  if(car.driverCharacterId!==identity.characterId||car.driverSessionId!==sessionId)
   reject(409,'NOT_DRIVER','Você não está dirigindo esta viatura.');
  if(officer.speed>1)reject(409,'VEHICLE_MOVING','Pare a viatura antes de sair.');
  const radians=car.yaw*Math.PI/180;
  let exit={x:car.x-Math.cos(radians)*2.1,y:car.y,z:car.z+Math.sin(radians)*2.1,yaw:car.yaw};
  if(!isPositionWithinWorld(identity.world,exit))exit={x:car.x+Math.cos(radians)*2.1,y:car.y,z:car.z-Math.sin(radians)*2.1,yaw:car.yaw};
  if(!isPositionWithinWorld(identity.world,exit))reject(409,'EXIT_BLOCKED','Não há ponto de saída dentro da área disponível.');
  releaseDriver(identity.characterId);
  Object.assign(officer,exit,{speed:0,driving:false,policeCarId:''});
  officer.renderSequence=(officer.renderSequence??officer.sequence)+1;
  return {carId:car.id,driving:false,...exit};
 }
 function uidArg(value){if(!/^[1-9][0-9]{0,18}$/.test(value||''))reject(400,'COMMAND_USAGE','Informe o ID numérico do jogador.');return value;}
 function target(uid,actor,protect=true){
  const found=online(uidArg(uid));
  if(!found)reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
  if(protect&&(found.characterId===actor.characterId||(actor.adminLevel!==1&&found.adminLevel>=actor.adminLevel)))
   reject(403,'ADMIN_HIERARCHY','Você só pode alterar jogadores de nível inferior.');
  return found;
 }
 function emit(identity,sessionId,clientId,messageId,text,channel='public'){
  const message={id:++messageSequence,messageId,characterId:identity.characterId,sessionId,clientId,uid:identity.uid,
   name:identity.name,firstName:identity.firstName||identity.name.split(/\s+/u)[0],isAdmin:true,text,channel,sentAt:clock()};
  messages.push(message);while(messages.length>60)messages.shift();return message;
 }
 function requireAdminCommandAccess(identity,name){
  const definition=adminCommand(name);if(!definition)return;
  if(identity.accountRole!=='admin'||!Number.isInteger(identity.adminLevel)||identity.adminLevel<1)
   reject(403,'ADMIN_ONLY','Este comando é exclusivo da administração.');
  if(identity.adminLevel===4&&definition.name!=='adminpromov')reject(403,'ADMIN_ROLE_RESTRICTED','O cargo administrativo nível 4 permite somente /adminpromov [ID/nick] [1|2].');
  if(definition.name==='adminpromov'&&![1,4].includes(identity.adminLevel))reject(403,'ADMIN_LEVEL_REQUIRED','/adminpromov exige administrador nível 1 ou cargo nível 4.');
  if(!adminCommandAllowed(identity.adminLevel,definition.name))reject(403,'ADMIN_LEVEL_REQUIRED',`/${definition.name} exige nível ${definition.level}.`);
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
   if(name==='ajuda')return {name,status:'open_help',message:'Guia do jogador aberto.',
    help:gameCommandHelp().filter(entry=>entry.group==='Jogador'||entry.group==='Atendimento'||
     entry.group==='Polícia'&&canPoliceDuty(identity))};
   if(name==='headmove')return {name,status:'headmove',message:'Movimento da cabeça alternado.'};
   return {name,status:'player_panel',message:'Abrindo '+playerDefinition.description.toLowerCase()};
  }
  const definition=adminCommand(name);
  if(!definition)reject(400,'UNKNOWN_COMMAND','Comando desconhecido. Use /ajuda.');
  requireAdminCommandAccess(identity,name);
  name=definition.name;args=args.trim();
  const parts=args?args.split(/\s+/u):[];
  const ok=(message,extra={})=>({name,status:'ok',message,...extra});
  const usage=()=>reject(400,'COMMAND_USAGE','Uso: '+definition.usage);
  if(definition.missing)return {name,status:'not_implemented',message:`/${name} aguarda o sistema de ${definition.missing}.`};
  if(name==='ahelp'){
   if(parts.length>1||parts.length===1&&!/^[1-4]$/.test(parts[0]))usage();
   const level=parts.length?Number(parts[0]):identity.adminLevel;
   if(identity.adminLevel!==1&&level>identity.adminLevel)reject(403,'ADMIN_LEVEL_REQUIRED','Você não pode consultar um nível acima do seu.');
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
   const angle=found.yaw*Math.PI/180;
   return {name,status:'teleport',message:`Indo até ${found.firstName} (ID ${found.uid}).`,
    x:found.x+Math.sin(angle)*1.7,y:found.y,z:found.z+Math.cos(angle)*1.7};
  }
  if(name==='bot1'){
   if(parts.length)usage();
   setFollowerBot({ownerCharacterId:identity.characterId,characterId:randomUUID(),uid:'BOT1',
    name:'Bot Teste',firstName:'Bot',gender:'male',model:'bybeyby',health:100,spawnInFront:true,ephemeral:true});
   advanceFollower(member);
   return ok('Bot de teste criado à sua frente. Ele ficará parado e desaparecerá quando você sair.');
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
  if(['kick','ban','unban','makeadmin','adminpromov','trocarsexo','jail','unjail'].includes(name)&&externalAction)
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
  // Recheck current DB-backed permissions before replaying a cached command.
  // A previous level-3 response must not restore tools after switching to level 4.
  const requested=text.match(/^\/([a-z0-9]+)(?:\s|$)/iu)?.[1].toLowerCase();
  if(requested){
   requireAdminCommandAccess(identity,requested);
   if(identity.adminLevel===4&&POLICE_COMMANDS.includes(requested))
    reject(403,'ADMIN_ROLE_RESTRICTED','O cargo administrativo nível 4 permite somente /adminpromov.');
  }
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
  if(identity.accountRole==='admin'&&identity.adminLevel===4)reject(403,'ADMIN_ROLE_RESTRICTED','O cargo administrativo nível 4 permite somente /adminpromov.');
  if(!canPoliceDuty(identity))reject(403,'ADMIN_REQUIRED','Você precisa pertencer à Polícia Militar.');
  const member=members.get(identity.characterId);
  if(!member||member.sessionId!==sessionId)reject(409,'NOT_IN_ROOM','Entre na cidade antes de alterar o serviço policial.');
  if(body!==undefined){
   if(!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).length!==1||typeof body.enabled!=='boolean')
    reject(400,'INVALID_DUTY','Estado de serviço inválido.');
   if(body.enabled){
    if(Number(identity.jailUntil)>clock())reject(403,'PLAYER_JAILED','Você está cumprindo pena.');
    policeDuty.set(identity.characterId,sessionId);
    member.orgCode=identity.organization?.code||'';
   }else revokePoliceDuty(identity.characterId);
  }
  member.policeDuty=policeDuty.get(identity.characterId)===sessionId;
  if(!member.policeDuty){member.radioFrames=[];member.radioVoice='';member.radioActive=false;}
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
   member.radioFrames=[];member.radioActive=false;
  }
  member.radioChannel=policeChannels.get(identity.characterId)?.channel||0;
  return {radioChannel:member.radioChannel,policeDuty:true};
 }
 function setCharacterGender(characterId,gender){
  const member=members.get(characterId);
  if(member&&['female','male'].includes(gender))member.gender=gender;
 }
 return {sync,chat,attack,shoot,respawn,leave,removeSession,findOnline:online,duty,radio,vehicle,setFollowerBot,privateMessage,broadcast,custody,patrol,setCharacterGender,
  revokePoliceDuty,applyPoliceSentence,requirePoliceCustody,followingDetainee,teleportPoliceEscort,jailTestBot,releaseTestBot,togglePoliceEquipment,grantPoliceWeapon,
  findPoliceCar:id=>[...policeCars.values()].find(car=>car.id===id)};
}
