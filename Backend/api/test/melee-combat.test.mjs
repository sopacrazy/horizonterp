import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createRoom} from '../multiplayer.mjs';
const person=n=>({characterId:'character-'+n,uid:String(n),name:'Jogador '+n,firstName:'Jogador',model:'bybeyby',world:'centro_teste',health:100});
const clientId='11111111-1111-4111-8111-111111111111';
const frame=(sequence=1,extra={})=>({clientId,sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,combatClient:true,...extra});
const action=()=>({clientId,attackId:randomUUID()});
function setup(options={}){
 let now=1000;const room=createRoom({capacity:5,clock:()=>now,...options}),a=person(1),b=person(2);
 room.sync(a,'a',frame());room.sync(b,'b',frame(1,{z:1.5}));
 return {room,a,b,advance(ms=2200){now+=ms;},refresh(sequence=2){room.sync(a,'a',frame(sequence));return room.sync(b,'b',frame(sequence,{z:1.5}));}};
}

test('server selects a nearby forward target and publishes separate attack/hit state to both players',async()=>{
 const {room,a,b}=setup();const request=action(),result=await room.attack(a,'a',request);
 assert.equal(result.targetUid,'2');assert.equal(result.damage,20);assert.equal(result.selfCombat.attackId,request.attackId);
 assert.equal(result.selfCombat.attackSequence,1);assert.equal(result.combatPeers[0].combat.health,80);
 assert.equal(result.combatPeers[0].combat.hitAttackId,request.attackId);
 const victim=room.sync(b,'b',frame(2,{z:1.5})),attacker=room.sync(a,'a',frame(2));
 assert.equal(victim.selfCombat.hitSequence,2);assert.equal(victim.selfCombat.health,80);
 assert.equal(victim.selfCombat.hitAttackId,request.attackId);
 assert.equal(victim.peers[0].combat.attackId,request.attackId);
 assert.equal(attacker.peers[0].combat.hitSequence,2);assert.equal(attacker.peers[0].combat.health,80);
 assert.equal(attacker.peers[0].combat.hitAttackId,request.attackId);
 assert.equal(attacker.peers[0].combatClient,true);assert.equal(victim.peers[0].combatClient,true);
 assert.equal(attacker.combatAvailable,true);assert.equal(victim.selfCombat.epoch,result.selfCombat.epoch);
});

test('legacy clients keep moving and are excluded from combat until they advertise the new capability',async()=>{
 const env=setup(),legacy=frame(2,{z:1.5});delete legacy.combatClient;
 const legacyReply=env.room.sync(env.b,'b',legacy);assert.equal(legacyReply.combatAvailable,true);
 assert.equal(env.room.sync(env.a,'a',frame(2)).peers.find(peer=>peer.uid==='2').combatClient,false);
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='COMBAT_CLIENT_REQUIRED');
 const miss=await env.room.attack(env.a,'a',action());assert.equal(miss.damage,0);assert.equal(miss.targetUid,'');
 const moving=env.room.sync(env.b,'b',{...legacy,sequence:3,x:.4,speed:1.65});
 assert.equal(moving.selfX,.4);assert.equal(moving.selfCombat.health,100);assert.equal(moving.selfCombat.hitSequence,0);
 assert.equal(moving.selfCombat.hitAttackId,'','a miss cannot confirm a predicted hit');
 env.advance();env.room.sync(env.a,'a',frame(3));
 env.room.sync(env.b,'b',frame(4,{z:1.5,combatClient:false}));
 assert.equal((await env.room.attack(env.a,'a',action())).damage,0,'explicit opt-out is also protected');
 env.advance();env.room.sync(env.a,'a',frame(4));
 env.room.sync(env.b,'b',frame(5,{z:1.5,combatClient:true}));
 const hit=await env.room.attack(env.a,'a',action());assert.equal(hit.targetUid,'2');assert.equal(hit.combatPeers[0].combat.health,80);
 env.advance(1600);env.room.sync(env.b,'b',frame(6,{z:1.5,yaw:180,combatClient:true}));env.room.sync(env.a,'a',frame(5));
 assert.equal((await env.room.attack(env.b,'b',action())).targetUid,'1','upgraded actor can attack after negotiating');
});

test('combat capability accepts only booleans and does not change legacy frame validation',()=>{
 const env=setup();
 for(const combatClient of [1,0,'true',null,{}])assert.throws(()=>env.room.sync(env.a,'a',frame(2,{combatClient})),e=>e.code==='INVALID_FRAME');
 const legacy=frame(2);delete legacy.combatClient;assert.equal(env.room.sync(env.a,'a',legacy).combatAvailable,true);
});

test('attack retries including concurrent copies are idempotent and cannot duplicate damage',async()=>{
 let writes=0;const {room,a}=setup({persistDamage:async({health,damage})=>{writes++;await new Promise(done=>setTimeout(done,10));return health-damage;}});
 const request=action(),results=await Promise.all([room.attack(a,'a',request),room.attack(a,'a',request),room.attack(a,'a',request)]);
 assert.deepEqual(results[0],results[1]);assert.deepEqual(results[1],results[2]);assert.equal(writes,1);
 await assert.rejects(room.attack(a,'a',action()),e=>e.code==='ATTACK_COOLDOWN');
});

test('new combo waits for the authored 2.2-second animation; refresh cannot heal received damage',async()=>{
 const env=setup();await env.room.attack(env.a,'a',action());env.advance(2199);env.refresh();
 await assert.rejects(env.room.attack(env.a,'a',action()),e=>e.code==='ATTACK_COOLDOWN');
 env.advance(1);const hit=await env.room.attack(env.a,'a',action());assert.equal(hit.combatPeers[0].combat.health,60);
 const victim=env.room.sync({...env.b,health:100},'b',frame(3,{z:1.5}));assert.equal(victim.selfCombat.health,60);
});

test('five combos reach zero once, preserve the terminal death event and freeze movement',async()=>{
 const env=setup();let result;
 for(let i=0;i<5;i++){if(i){env.advance();env.refresh(i+1);}result=await env.room.attack(env.a,'a',action());}
 assert.equal(result.combatPeers[0].combat.health,0);assert.equal(result.combatPeers[0].combat.dead,true);
 const death=result.combatPeers[0].combat.deathSequence;assert.ok(death>0);
 const victim=env.room.sync(env.b,'b',frame(10,{x:30,z:30,speed:8,jumping:true,jumpPhase:.3}));
 assert.equal(victim.selfX,0);assert.equal(victim.selfZ,1.5);assert.equal(victim.selfCombat.deathSequence,death);
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='ATTACK_BLOCKED');
 env.advance();env.refresh(11);result=await env.room.attack(env.a,'a',action());
 assert.equal(result.damage,0);assert.equal(env.room.sync(env.b,'b',frame(12,{z:1.5})).selfCombat.deathSequence,death);
});

test('range, height, facing and nearest-target checks prevent distant, behind or duplicated hits',async()=>{
 for(const location of [{z:2.21},{z:1,y:1.51},{z:-1.5},{x:1.9,z:.1}]){
  const {room,a,b}=setup();room.sync(b,'b',frame(2,location));
  assert.equal((await room.attack(a,'a',action())).damage,0,JSON.stringify(location));
 }
 const {room,a}=setup(),c=person(3);room.sync(c,'c',frame(1,{z:.7}));
 const hit=await room.attack(a,'a',action());assert.equal(hit.targetUid,'3');
 const peers=room.sync(a,'a',frame(2)).peers;assert.equal(peers.find(p=>p.uid==='2').combat.health,100);
 assert.equal(peers.find(p=>p.uid==='3').combat.health,80);
});

test('authenticated room ownership is mandatory and health/target/time cannot be forged in the body',async()=>{
 const {room,a}=setup();
 for(const body of [null,{}, {...action(),health:0},{...action(),targetUid:'2'},{...action(),attackAt:0},{...action(),attackId:'1'}])
  await assert.rejects(room.attack(a,'a',body),e=>e.code==='INVALID_ATTACK');
 await assert.rejects(room.attack(a,'wrong-session',action()),e=>e.code==='NOT_IN_ROOM');
 await assert.rejects(room.attack(a,'a',{...action(),clientId:randomUUID()}),e=>e.code==='NOT_IN_ROOM');
 await assert.rejects(room.attack(person(4),'d',action()),e=>e.code==='NOT_IN_ROOM');
});

test('stale movement cannot attack or receive hits until fresh authenticated presence arrives',async()=>{
 const env=setup();env.advance(1501);
 await assert.rejects(env.room.attack(env.a,'a',action()),e=>e.code==='STALE_COMBAT_POSE');
 env.room.sync(env.a,'a',frame(2));assert.equal((await env.room.attack(env.a,'a',action())).damage,0);
});

test('cuffed, jumping and vehicle actors cannot attack; seated detainees cannot receive melee through the car',async()=>{
 const env=setup(),officer={...env.a,accountRole:'admin',adminLevel:1,organization:{code:'pm'}};
 env.room.duty(officer,'a',{enabled:true});env.room.custody(officer,'cuff','2');
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='ATTACK_BLOCKED');
 env.room.custody(officer,'uncuff','2');env.room.sync(env.b,'b',frame(2,{z:1.5,jumping:true,jumpPhase:.2}));
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='ATTACK_BLOCKED');
 env.room.sync(env.b,'b',frame(3,{z:1.5}));env.room.custody(officer,'cuff','2');env.room.patrol(officer,'carpolicia');env.room.patrol(officer,'put','2');
 assert.equal((await env.room.attack(officer,'a',action())).damage,0);
 env.advance();env.room.sync(officer,'a',frame(4,{z:4}));env.room.vehicle(officer,'a',{action:'enter',carId:'pm-1'});
 await assert.rejects(env.room.attack(officer,'a',action()),e=>e.code==='ATTACK_BLOCKED');
});

test('failed persistence publishes no event, applies no cooldown, and a retry writes once',async()=>{
 let fail=true;const {room,a}=setup({persistDamage:async({health,damage})=>{if(fail)throw Error('offline');return health-damage;}}),request=action();
 await assert.rejects(room.attack(a,'a',request),/offline/);
 let snapshot=room.sync(a,'a',frame(2));assert.equal(snapshot.selfCombat.attackSequence,0);assert.equal(snapshot.peers[0].combat.health,100);
 fail=false;const result=await room.attack(a,'a',request);assert.equal(result.damage,20);assert.equal(result.combatPeers[0].combat.health,80);
});

test('Hit Reaction staggers attacks and freezes movement for the full 1.6-second authored reaction',async()=>{
 const env=setup();await env.room.attack(env.a,'a',action());
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='ATTACK_BLOCKED');
 const reacting=env.room.sync(env.b,'b',frame(2,{x:5,z:5,speed:4,jumping:true,jumpPhase:.5}));
 assert.equal(reacting.selfX,0);assert.equal(reacting.selfZ,1.5);
 env.advance(1599);env.refresh(3);
 await assert.rejects(env.room.attack(env.b,'b',action()),e=>e.code==='ATTACK_BLOCKED');
 env.advance(1);env.room.sync(env.b,'b',frame(4,{z:1.5,yaw:180}));
 const counter=await env.room.attack(env.b,'b',action());assert.equal(counter.targetUid,'1');assert.equal(counter.combatPeers[0].combat.health,80);
});

test('a delayed retry retains its original revision so clients can ignore it after a newer hit',async()=>{
 const env=setup(),request=action(),old=await env.room.attack(env.a,'a',request);
 env.advance();env.refresh();await env.room.attack(env.a,'a',action());
 const retry=await env.room.attack(env.a,'a',request),fresh=env.room.sync(env.b,'b',frame(3,{z:1.5}));
 assert.equal(retry.combatPeers[0].combat.health,80);assert.equal(fresh.selfCombat.health,60);
 assert.ok(fresh.selfCombat.revision>old.combatPeers[0].combat.revision);
});

test('official stationary test bot receives the same authoritative damage and reactions',async()=>{
 const env=setup(),owner={...env.a,characterId:'11111111-2222-4333-8444-555555555555'};
 env.room.setFollowerBot({ownerCharacterId:owner.characterId,characterId:'66666666-7777-4888-8999-aaaaaaaaaaaa',uid:'55',name:'Bot Teste',firstName:'Bot',health:70});
 env.room.sync(owner,'owner',frame(1,{x:10,z:10}));
 env.room.sync(owner,'owner',frame(2,{x:10,z:1,yaw:0}));
 const reply=await env.room.attack(owner,'owner',action());assert.equal(reply.targetUid,'55');assert.equal(reply.combatPeers[0].combat.health,50);
});

test('escort pauses during Hit Reaction and ends on death without moving or boarding the corpse',async()=>{
 let now=1000;const room=createRoom({capacity:3,clock:()=>now});
 const officer={...person(1),accountRole:'admin',adminLevel:1,organization:{code:'pm'}},detained={...person(2),health:40},attacker=person(3);
 room.sync(officer,'officer',frame(1,{yaw:180}));room.sync(detained,'detained',frame(1,{z:1.5}));room.sync(attacker,'attacker',frame(1,{z:3,yaw:180}));
 room.duty(officer,'officer',{enabled:true});room.custody(officer,'cuff','2');room.custody(officer,'follow','2');
 let peers=room.sync(officer,'officer',frame(2,{yaw:180})).peers;
 const before=peers.find(peer=>peer.uid==='2');assert.equal(before.z,1.7);assert.equal(before.escortedBy,'1');
 await room.attack(attacker,'attacker',action());
 peers=room.sync(officer,'officer',frame(3,{x:1,yaw:180,speed:1.65})).peers;
 let target=peers.find(peer=>peer.uid==='2');assert.equal(target.x,before.x);assert.equal(target.z,before.z);assert.equal(target.speed,0);
 assert.equal(target.escortedBy,'1','a nonfatal hit pauses rather than erases the escort');
 now+=1600;room.sync(attacker,'attacker',frame(2,{z:3,yaw:180}));
 target=room.sync(officer,'officer',frame(4,{x:1,z:1,yaw:180,speed:1.65})).peers.find(peer=>peer.uid==='2');
 assert.ok(Math.abs(target.x-1)<1e-8);assert.equal(target.z,2.7);
 now+=600;room.sync(attacker,'attacker',frame(3,{x:1,z:4,yaw:180}));await room.attack(attacker,'attacker',action());
 target=room.sync(officer,'officer',frame(5,{x:5,z:1,yaw:180,speed:1.65})).peers.find(peer=>peer.uid==='2');
 assert.equal(target.combat.dead,true);assert.ok(Math.abs(target.x-1)<1e-8);assert.equal(target.z,2.7);assert.equal(target.escortedBy,'');assert.equal(target.cuffed,true);
 assert.throws(()=>room.custody(officer,'follow','2'),e=>e.code==='TARGET_DEAD');
 assert.throws(()=>room.custody(officer,'cuff','2'),e=>e.code==='TARGET_DEAD');
 room.sync(officer,'officer',frame(6,{x:1,z:1}));room.patrol(officer,'carpolicia');
 assert.throws(()=>room.patrol(officer,'put','2'),e=>e.code==='TARGET_DEAD');
 assert.equal(room.custody(officer,'uncuff','2').status,'ok','cleanup can remove existing cuffs');
});

test('a dead officer cannot cuff, escort, board suspects or spawn cars; custody cleanup remains available',async()=>{
 const room=createRoom({capacity:4}),officer={...person(1),health:20,accountRole:'admin',adminLevel:1,organization:{code:'pm'}},detained=person(2),attacker=person(3),other=person(4);
 room.sync(officer,'officer',frame());room.sync(detained,'detained',frame(1,{z:1.5}));
 room.sync(attacker,'attacker',frame(1,{z:-1.5}));room.sync(other,'other',frame(1,{x:1}));
 room.duty(officer,'officer',{enabled:true});room.custody(officer,'cuff','2');room.custody(officer,'cuff','4');
 room.patrol(officer,'carpolicia');room.patrol(officer,'put','2');
 assert.equal((await room.attack(attacker,'attacker',action())).targetUid,'1');
 for(const name of ['cuff','follow'])assert.throws(()=>room.custody(officer,name,'4'),e=>e.code==='PLAYER_DEAD');
 for(const name of ['carpolicia','put'])assert.throws(()=>room.patrol(officer,name,'2'),e=>e.code==='PLAYER_DEAD');
 assert.equal(room.custody(officer,'uncuff','4').status,'ok');
 assert.equal(room.patrol(officer,'eject','2').status,'ok','the existing rear reservation can still be cleaned up');
});
