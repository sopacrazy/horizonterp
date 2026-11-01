import test from 'node:test';
import assert from 'node:assert/strict';
import {createRoom} from '../multiplayer.mjs';
const identity=n=>({characterId:String(n),uid:String(n),accountRole:'admin',adminLevel:3,name:'Teste '+n,firstName:'Teste',model:'bybeyby',world:'centro_teste'});
const frame=(sequence,extra={})=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});

test('microphone state is negotiated, stays active during silence and clears on release/stale connection',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 assert.equal(room.sync(identity(1),'a',frame(1)).voiceStateAvailable,true);
 room.sync(identity(1),'a',frame(2,{voiceActive:true}));
 assert.equal(room.sync(identity(2),'b',frame(1)).peers[0].voiceActive,true);
 now+=500;room.sync(identity(1),'a',frame(3,{voiceActive:true}));
 assert.equal(room.sync(identity(2),'b',frame(2)).peers[0].voiceActive,true,'silence does not turn off an enabled microphone');
 assert.equal(room.sync(identity(2),'b',frame(3,{x:36})).peers[0].voiceActive,false,'proximity scope respected');
 now+=1501;
 assert.equal(room.sync(identity(2),'b',frame(4)).peers[0].voiceActive,false,'lost sender does not blink forever');
 room.sync(identity(1),'a',frame(4,{voiceActive:false}));
 assert.equal(room.sync(identity(2),'b',frame(5)).peers[0].voiceActive,false);
});

test('radio indicator follows police permission, channel and channel disconnect',()=>{
 const room=createRoom({capacity:3});
 for(let i=1;i<=3;i++){room.sync(identity(i),'s'+i,frame(1));room.duty(identity(i),'s'+i,{enabled:true});room.radio(identity(i),'s'+i,{channel:i===3?2:1});}
 room.sync(identity(1),'s1',frame(2,{radioActive:true}));
 assert.equal(room.sync(identity(2),'s2',frame(2)).peers.find(p=>p.uid==='1').radioActive,true);
 assert.equal(room.sync(identity(3),'s3',frame(2)).peers.find(p=>p.uid==='1').radioActive,false);
 room.radio(identity(1),'s1',{channel:0});
 assert.equal(room.sync(identity(2),'s2',frame(3)).peers.find(p=>p.uid==='1').radioActive,false);
 assert.throws(()=>room.sync(identity(1),'s1',frame(3,{radioActive:true})),e=>e.code==='RADIO_FORBIDDEN');
});

test('old clients remain compatible and malformed/ambiguous microphone states are rejected',()=>{
 const room=createRoom();
 room.sync(identity(1),'a',frame(1));
 assert.equal(room.sync(identity(2),'b',frame(1)).peers[0].voiceActive,false);
 for(const extra of [{voiceActive:'true'},{radioActive:1},{voiceActive:true,radioActive:true}])
  assert.throws(()=>room.sync(identity(1),'a',frame(2,extra)),e=>e.code==='INVALID_FRAME');
});
