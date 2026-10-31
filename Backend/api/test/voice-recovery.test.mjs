import test from 'node:test';
import assert from 'node:assert/strict';
import {createRoom} from '../multiplayer.mjs';
const identity=n=>({characterId:String(n),uid:String(n),accountRole:'admin',adminLevel:3,name:'Teste '+n,firstName:'Teste',model:'bybeyby',world:'centro_teste'});
const frame=(sequence,extra={})=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});
const audio=n=>Buffer.alloc(1600,n).toString('base64');

test('voice recovery retains four ordered blocks across movement-only updates and expires by audio time',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 for(let i=1;i<=6;i++){now+=100;room.sync(identity(1),'sender',frame(i,{voice:audio(i),voiceSequence:i}));}
 room.sync(identity(1),'sender',frame(7));
 const peer=room.sync(identity(2),'receiver',frame(1)).peers[0];
 assert.equal(peer.voice,'','legacy field retains its established semantics');
 assert.deepEqual(peer.voiceFrames.map(p=>p.sequence),[3,4,5,6]);
 assert.equal(peer.voiceFrames[0].data,audio(3));
 assert.deepEqual(room.sync(identity(2),'receiver',frame(2,{x:36})).peers[0].voiceFrames,[]);
 now+=501;room.sync(identity(1),'sender',frame(8));
 assert.deepEqual(room.sync(identity(2),'receiver',frame(3)).peers[0].voiceFrames,[]);
 room.sync(identity(1),'new-session',frame(1,{voice:audio(9),voiceSequence:1}));
 assert.deepEqual(room.sync(identity(2),'receiver',frame(4)).peers[0].voiceFrames.map(p=>p.sequence),[1]);
});

test('radio recovery respects service, channel changes and disconnection',()=>{
 let now=1000;const room=createRoom({clock:()=>now,capacity:3});
 for(let i=1;i<=3;i++){room.sync(identity(i),'s'+i,frame(1));room.duty(identity(i),'s'+i,{enabled:true});room.radio(identity(i),'s'+i,{channel:i===3?2:1});}
 room.sync(identity(1),'s1',frame(2,{radioVoice:audio(1),radioSequence:1,radioChannel:1}));
 now+=100;room.sync(identity(1),'s1',frame(3));
 assert.equal(room.sync(identity(2),'s2',frame(2)).peers.find(p=>p.uid==='1').radioFrames.length,1);
 assert.equal(room.sync(identity(3),'s3',frame(2)).peers.find(p=>p.uid==='1').radioFrames.length,0);
 room.radio(identity(1),'s1',{channel:2});
 assert.equal(room.sync(identity(3),'s3',frame(3)).peers.find(p=>p.uid==='1').radioFrames.length,0,'old channel audio is erased immediately');
 room.sync(identity(1),'s1',frame(4,{radioVoice:audio(2),radioSequence:2,radioChannel:2}));
 room.duty(identity(1),'s1',{enabled:false});
 assert.equal(room.sync(identity(3),'s3',frame(4)).peers.find(p=>p.uid==='1').radioFrames.length,0);
});

test('independent mobile polling with 100-300 ms gaps recovers every voice block locally',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 const recovered=new Set(),legacy=new Set();let receiverSequence=0;
 room.sync(identity(2),'receiver',frame(++receiverSequence));
 for(let n=1;n<=60;n++){
  now=1000+n*100;room.sync(identity(1),'sender',frame(n,{voice:audio(n),voiceSequence:n}));
  // Receiver occasionally skips two polling intervals, independently of the speaker.
  if(n%3===0){const p=room.sync(identity(2),'receiver',frame(++receiverSequence)).peers[0];
   if(p.voice)legacy.add(p.voiceSequence);for(const packet of p.voiceFrames)recovered.add(packet.sequence);}
 }
 assert.equal(recovered.size,60);assert.equal(legacy.size,20);
});
