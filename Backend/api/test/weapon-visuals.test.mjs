import test from 'node:test';
import assert from 'node:assert/strict';
import {createRoom} from '../multiplayer.mjs';
const identity=n=>({characterId:'character-'+n,uid:String(n),name:'Player '+n,model:'bybeyby',world:'centro_teste'});
const frame=(sequence=1,extra={})=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});

test('weapon visual state is negotiated and does not change authoritative health',()=>{
 const room=createRoom({capacity:3});
 const first=room.sync(identity(1),'one',frame());
 assert.equal(first.weaponStateAvailable,true);
 room.sync(identity(1),'one',frame(2,{weaponEquipped:true,weaponAiming:true,weaponShotSequence:5,weaponAimPitch:-15}));
 const seen=room.sync(identity(2),'two',frame()).peers[0];
 assert.equal(seen.weaponEquipped,true);assert.equal(seen.weaponAiming,true);
 assert.equal(seen.weaponShotSequence,5);assert.equal(seen.weaponAimPitch,-15);
 assert.equal(seen.combat.health,100);
 room.sync(identity(1),'one',frame(3,{weaponEquipped:false,weaponAiming:false,weaponShotSequence:5,weaponAimPitch:0}));
 assert.equal(room.sync(identity(2),'two',frame(2)).peers[0].weaponEquipped,false);
});

test('old movement clients are accepted and cannot inherit a stale rifle',()=>{
 const room=createRoom({capacity:3});
 room.sync(identity(1),'one',frame(1,{weaponEquipped:true}));
 room.sync(identity(1),'one',frame(2));
 const seen=room.sync(identity(2),'two',frame()).peers[0];
 assert.equal(seen.weaponEquipped,false);assert.equal(seen.weaponAiming,false);
 assert.equal(seen.weaponShotSequence,0);
});

test('malformed cosmetic weapon fields are rejected before publishing a peer',()=>{
 for(const invalid of [{weaponEquipped:1},{weaponAiming:true},{weaponShotSequence:-1},
  {weaponShotSequence:1.1},{weaponShotSequence:2147483648},{weaponAimPitch:81},{weaponAimPitch:'0'},{weaponAimPitch:Infinity}]){
  const room=createRoom();
  assert.throws(()=>room.sync(identity(1),'one',frame(1,invalid)),e=>e.code==='INVALID_FRAME');
 }
});

test('driving suppresses the displayed rifle even if a client advertises it',()=>{
 const room=createRoom({capacity:3});
 room.sync(identity(1),'one',frame(1,{driving:true,weaponEquipped:true,weaponAiming:true}));
 const seen=room.sync(identity(2),'two',frame()).peers[0];
 assert.equal(seen.weaponEquipped,false);assert.equal(seen.weaponAiming,false);
});
