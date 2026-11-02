import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createRoom} from '../multiplayer.mjs';

const clientId='11111111-1111-4111-8111-111111111111';
const person=n=>({characterId:'character-'+n,uid:String(n),name:'Jogador '+n,firstName:'Jogador',model:'bybeyby',world:'centro_teste',health:100});
const frame=(sequence=1,extra={})=>({clientId,sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,combatClient:true,afterRespawn:0,weaponEquipped:true,weaponAiming:true,...extra});
const shot=extra=>({clientId,shotId:randomUUID(),originX:0,originY:1.3,originZ:.3,directionX:0,directionY:0,directionZ:1,targetUid:'2',...extra});
function fixture(options={}){
 let now=1000,sequence=1;const room=createRoom({capacity:5,clock:()=>now,...options}),a=person(1),b=person(2);
 const refresh=(extra={})=>{sequence++;room.sync(a,'a',frame(sequence));return room.sync(b,'b',frame(sequence,{z:10,...extra}));};
 refresh();return {room,a,b,refresh,advance(ms){now+=ms;},request(state){return {clientId,requestId:randomUUID(),epoch:state.epoch,deathSequence:state.deathSequence};}};
}
async function kill(env){let last;for(let i=0;i<4;i++){if(i){env.advance(150);env.refresh();}last=await env.room.shoot(env.a,'a',shot());}return last.combatPeers[0].combat;}

test('rifle validates observed actor and publishes immediate persisted health in steps of25',async()=>{
 let writes=0;const env=fixture({persistDamage:async({health,damage})=>{writes++;return health-damage;}});
 const result=await env.room.shoot(env.a,'a',shot());assert.equal(result.damage,25);assert.equal(result.combatPeers[0].combat.health,75);assert.equal(writes,1);
 const self=env.refresh();assert.equal(self.selfCombat.health,75);assert.equal(self.rifleDamageAvailable,true);assert.equal(self.respawnAvailable,true);
});
test('concurrent retries cannot duplicate rifle damage and rate limits apply only to new shot ids',async()=>{
 let writes=0;const env=fixture({persistDamage:async({health,damage})=>{writes++;await new Promise(resolve=>setTimeout(resolve,10));return health-damage;}}),request=shot();
 const values=await Promise.all([env.room.shoot(env.a,'a',request),env.room.shoot(env.a,'a',request)]);
 assert.deepEqual(values[0],values[1]);assert.equal(writes,1);
 await assert.rejects(env.room.shoot(env.a,'a',shot()),error=>error.code==='SHOT_COOLDOWN');
});
test('forged health, damage, distant origin, direction and target never produce damage',async()=>{
 const env=fixture();
 for(const extra of [{damage:100},{health:0},{originX:50},{directionZ:2},{targetUid:'999'},{targetUid:'1'},{directionX:1,directionZ:0}])
  await assert.rejects(env.room.shoot(env.a,'a',shot(extra)));
 assert.equal(env.room.sync(env.b,'b',frame(23,{z:10})).selfCombat.health,100);
 await assert.rejects(env.room.shoot(env.a,'wrong',shot()),error=>error.code==='NOT_IN_ROOM');
});
test('unarmed actors and opted-out legacy targets are protected',async()=>{
 const env=fixture();env.room.sync(env.a,'a',frame(20,{weaponEquipped:false,weaponAiming:false}));
 await assert.rejects(env.room.shoot(env.a,'a',shot()),error=>error.code==='SHOT_BLOCKED');
 env.room.sync(env.a,'a',frame(21));env.room.sync(env.b,'b',frame(22,{z:10,combatClient:false}));
 await assert.rejects(env.room.shoot(env.a,'a',shot()),error=>error.code==='INVALID_SHOT_TARGET');
});
test('installed combat clients without the respawn cursor cannot receive or cause rifle damage',async()=>{
 const env=fixture(),legacy=frame(20,{z:10});delete legacy.afterRespawn;
 env.room.sync(env.b,'b',legacy);
 await assert.rejects(env.room.shoot(env.a,'a',shot()),error=>error.code==='INVALID_SHOT_TARGET');
 env.room.sync(env.b,'b',frame(21,{z:10}));const oldActor=frame(22);delete oldActor.afterRespawn;
 env.room.sync(env.a,'a',oldActor);
 await assert.rejects(env.room.shoot(env.a,'a',shot()),error=>error.code==='SHOT_BLOCKED');
 assert.equal(env.room.sync(env.b,'b',frame(23,{z:10})).selfCombat.health,100);
});
test('ephemeral test bot revives after5seconds at its original spawn without a client or database write',async()=>{
 let now=1000,writes=0,sequence=0;const a={...person(1),characterId:randomUUID()},botId=randomUUID();
 const room=createRoom({clock:()=>now,persistRespawn:async()=>{writes++;}});
 room.setFollowerBot({ownerCharacterId:a.characterId,characterId:botId,uid:'BOT1',name:'Bot Teste',firstName:'Bot',ephemeral:true,health:100,spawnInFront:true});
 const refresh=()=>room.sync(a,'a',frame(++sequence));let response=refresh(),initial=response.peers[0];
 for(let i=0;i<4;i++){now+=150;refresh();await room.shoot(a,'a',shot({targetUid:'BOT1'}));}
 response=refresh();assert.equal(response.peers[0].combat.dead,true);now+=4999;response=refresh();assert.equal(response.peers[0].combat.dead,true);
 now+=1;response=refresh();assert.equal(response.peers[0].combat.health,100);assert.equal(response.peers[0].combat.dead,false);
 assert.equal(response.peers[0].x,initial.x);assert.equal(response.peers[0].z,initial.z);assert.ok(response.peers[0].combat.respawnSequence>0);assert.equal(writes,0);
});
test('a client miss cannot choose a hidden target; failed persistence never publishes damage',async()=>{
 const env=fixture({persistDamage:async()=>{throw new Error('write failed');}});
 const miss=await env.room.shoot(env.a,'a',shot({targetUid:''}));assert.equal(miss.damage,0);
 env.advance(150);env.refresh();await assert.rejects(env.room.shoot(env.a,'a',shot()),/write failed/);
 assert.equal(env.refresh().selfCombat.health,100);
});
test('death waits exactly5seconds and concurrent respawn persists one health/spawn transition',async()=>{
 let writes=0;const spawn={x:2489.3,y:12.37,z:1668.5,yaw:40};
 const env=fixture({persistRespawn:async data=>{writes++;assert.equal(data.characterId,env.b.characterId);assert.equal(data.x,spawn.x);await new Promise(resolve=>setTimeout(resolve,10));}});
 const dead=await kill(env),request=env.request(dead);assert.equal(dead.health,0);assert.equal(dead.dead,true);assert.ok(dead.respawnAt>dead.deathAt);
 env.advance(4999);await assert.rejects(env.room.respawn(env.b,'b',request),error=>error.code==='RESPAWN_WAIT');assert.equal(writes,0);
 env.advance(1);const [a,b]=await Promise.all([env.room.respawn(env.b,'b',request),env.room.respawn(env.b,'b',{...request,requestId:randomUUID()})]);
 assert.deepEqual(a,b);assert.equal(writes,1);assert.equal(a.selfCombat.health,100);assert.equal(a.selfCombat.dead,false);assert.ok(a.selfCombat.respawnSequence>dead.deathSequence);
 const stale=env.refresh({x:99,z:99});assert.equal(stale.selfX,spawn.x);assert.equal(stale.selfZ,spawn.z,'in-flight death frames cannot overwrite spawn');
 const acknowledged=env.refresh({x:spawn.x+1,y:spawn.y,z:spawn.z,afterRespawn:a.selfCombat.respawnSequence});assert.equal(acknowledged.selfX,spawn.x+1);
});
test('only the dead authenticated generation can respawn; failed transaction leaves death intact',async()=>{
 let fail=true;const env=fixture({persistRespawn:async()=>{if(fail)throw new Error('rollback');}}),dead=await kill(env),request=env.request(dead);
 env.advance(5000);
 await assert.rejects(env.room.respawn(env.b,'wrong',request),error=>error.code==='NOT_IN_ROOM');
 await assert.rejects(env.room.respawn(env.b,'b',{...request,deathSequence:dead.deathSequence+1}),error=>error.code==='RESPAWN_STALE');
 await assert.rejects(env.room.respawn(env.b,'b',{...request,epoch:randomUUID()}),error=>error.code==='INVALID_RESPAWN');
 await assert.rejects(env.room.respawn(env.b,'b',request),/rollback/);assert.equal(env.refresh().selfCombat.health,0);
 fail=false;assert.equal((await env.room.respawn(env.b,'b',request)).selfCombat.health,100);
});
