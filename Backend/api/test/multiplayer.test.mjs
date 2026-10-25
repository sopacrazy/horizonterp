import test from 'node:test';
import assert from 'node:assert/strict';
import {createRoom} from '../multiplayer.mjs';
const id=n=>({characterId:'character-'+n,uid:String(n),name:'Jogador '+n,model:'bybeyby',world:'centro_teste'});
const frame=(sequence=1,extra={})=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});
test('peer timestamps belong to the source update rather than the observing poll',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 room.sync(id(2),'s2',frame(1,{x:1,speed:1.65}));
 now=1250;const first=room.sync(id(1),'s1',frame());
 assert.equal(first.serverTime,1250);assert.equal(first.peers[0].updatedAt,1000);
 now=1500;const again=room.sync(id(1),'s1',frame(2));
 assert.equal(again.peers[0].updatedAt,1000);
 now=1600;room.sync(id(2),'s2',frame(2,{x:2,speed:1.65}));
 now=1800;assert.equal(room.sync(id(1),'s1',frame(3)).peers[0].updatedAt,1600);
 assert.throws(()=>room.sync(id(1),'s1',frame(4,{updatedAt:9999})),e=>e.code==='INVALID_FRAME');
});
test('two players share movement and jumping; identities come from authentication',()=>{
 const room=createRoom();
 assert.equal(room.sync(id(1),'session1',frame()).count,1);
 const second=room.sync(id(2),'session2',frame(1,{x:2,speed:4.5,jumping:true,jumpPhase:.4}));
 assert.equal(second.count,2);assert.deepEqual(second.peers.map(p=>p.uid),['1']);
 const first=room.sync(id(1),'session1',frame(2));
 assert.equal(first.peers[0].x,2);assert.equal(first.peers[0].jumping,true);assert.equal(first.peers[0].jumpPhase,.4);
 assert.equal(first.peers[0].sessionId,undefined);assert.equal(first.peers[0].clientId,undefined);
 assert.throws(()=>room.sync(id(3),'session3',frame()),e=>e.code==='ROOM_FULL');
 assert.throws(()=>room.sync(id(1),'session1',frame(2)),e=>e.code==='STALE_FRAME');
 assert.throws(()=>room.sync(id(1),'session1',frame(3,{clientId:'22222222-2222-4222-8222-222222222222'})),e=>e.code==='SESSION_IN_USE');
 room.leave('character-1','wrong-session');assert.throws(()=>room.sync(id(3),'session3',frame()),e=>e.code==='ROOM_FULL');
 room.leave('character-1','session1');assert.equal(room.sync(id(3),'session3',frame()).count,2);
 room.removeSession('session2');assert.equal(room.sync(id(3),'session3',frame(2)).count,1);
});
test('expiry frees the room and malformed or protected state cannot be broadcast',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 for(const extra of [{x:NaN},{y:99},{x:23},{speed:9},{jumping:1},{jumpPhase:2},{uid:'spoof'},{cashCents:'999'},{clientId:null}])
  assert.throws(()=>room.sync(id(1),'s',frame(1,extra)),e=>e.code==='INVALID_FRAME');
 room.sync(id(1),'s1',frame());room.sync(id(2),'s2',frame());now+=10001;
 assert.equal(room.sync(id(3),'s3',frame()).count,1);
 // A new authenticated session can replace its own stale connection.
 assert.equal(room.sync(id(3),'s4',frame(1,{clientId:'22222222-2222-4222-8222-222222222222'})).count,1);
});
test('room chat is authenticated, Unicode bounded, ordered, deduplicated and rate limited',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 const message={clientId:frame().clientId,messageId:'22222222-2222-4222-8222-222222222222',text:' Olá, João! '};
 assert.throws(()=>room.chat(id(1),'s1',message),e=>e.code==='NOT_IN_ROOM');
 room.sync(id(1),'s1',frame());room.sync(id(2),'s2',frame());
 const sent=room.chat(id(1),'s1',message);
 assert.equal(sent.message.name,'Jogador 1');assert.equal(sent.message.text,'Olá, João!');
 assert.equal(sent.message.sessionId,undefined);assert.equal(sent.message.characterId,undefined);
 assert.equal(room.chat(id(1),'s1',message).message.id,sent.message.id);
 assert.throws(()=>room.chat(id(1),'s1',{...message,text:'Outro texto'}),e=>e.code==='MESSAGE_CONFLICT');
 assert.throws(()=>room.chat(id(1),'s1',{...message,messageId:'33333333-3333-4333-8333-333333333333'}),e=>e.code==='CHAT_RATE_LIMITED');
 for(const text of ['', 'a'.repeat(181),'linha\nnova','texto\u202eoculto'])
  assert.throws(()=>room.chat(id(1),'s1',{...message,text}),e=>e.code==='INVALID_MESSAGE');
 assert.throws(()=>room.chat(id(1),'s1',{...message,name:'Nome falso'}),e=>e.code==='INVALID_MESSAGE');
 let snapshot=room.sync(id(2),'s2',frame(2));assert.equal(snapshot.messages.length,1);
 assert.equal(snapshot.chatEpoch,sent.chatEpoch);
 assert.equal(room.sync(id(2),'s2',frame(3,{afterChat:sent.message.id})).messages.length,0);
 now+=1300;room.chat(id(2),'s2',{...message,messageId:'44444444-4444-4444-8444-444444444444',text:'Recebido!'});
 assert.equal(room.sync(id(1),'s1',frame(2,{afterChat:sent.message.id})).messages[0].name,'Jogador 2');
 // Successful sync must not reset the anti-spam timer.
 assert.throws(()=>room.chat(id(2),'s2',{...message,messageId:'55555555-5555-4555-8555-555555555555'}),e=>e.code==='CHAT_RATE_LIMITED');
 for(let i=0;i<65;i++){now+=1300;room.sync(id(1),'s1',frame(3+i));room.chat(id(1),'s1',{...message,messageId:`00000000-0000-4000-8000-${String(i).padStart(12,'0')}`});}
 snapshot=room.sync(id(1),'s1',frame(100));assert.equal(snapshot.messages.length,20);
 now+=900001;assert.equal(room.sync(id(1),'s1',frame(101)).messages.length,0);
});

test('admin placeholders require server identity and never enter public chat',()=>{
 let now=1000;const room=createRoom({clock:()=>now});const admin={...id(1),accountRole:'admin',adminLevel:2};
 room.sync(admin,'s1',frame());room.sync(id(2),'s2',frame());
 const body=text=>({clientId:frame().clientId,messageId:'22222222-2222-4222-8222-222222222222',text});
 assert.throws(()=>room.chat(id(2),'s2',body('/goto 1')),e=>e.code==='ADMIN_ONLY');
 for(const cmd of ['a mensagem','ans','alist','inf 2','myastats','goto 2','mute 2 5 motivo','unmute 2','Muterep 2 5 motivo','Unmuterep 2','jail Nome 5 motivo','unjail Nome','re 2','reoff','kick 2 motivo']){
  now+=1300;room.sync(admin,'s1',frame(now));
  const result=room.chat(admin,'s1',body('/'+cmd));assert.equal(result.command.status,'not_implemented');assert.equal(result.message,undefined);
 }
 assert.equal(room.sync(id(2),'s2',frame(2)).messages.length,0);
 now+=1300;assert.throws(()=>room.chat({...admin,adminLevel:0},'s1',body('/a teste')),e=>e.code==='ADMIN_ONLY');
 now+=1300;assert.throws(()=>room.chat(admin,'s1',body('/desconhecido')),e=>e.code==='UNKNOWN_COMMAND');
});

test('admin chat tags come from authenticated identity, never from message fields',()=>{
 let now=1000;const room=createRoom({clock:()=>now});
 const admin={...id(1),accountRole:'admin',adminLevel:2};
 room.sync(admin,'s1',frame());room.sync(id(2),'s2',frame());
 const body={clientId:frame().clientId,messageId:'22222222-2222-4222-8222-222222222222',text:'Mensagem da administração'};
 const sent=room.chat(admin,'s1',body);assert.equal(sent.message.isAdmin,true);
 assert.equal(room.sync(id(2),'s2',frame(2)).messages[0].isAdmin,true);
 assert.throws(()=>room.chat(id(2),'s2',{...body,isAdmin:true}),e=>e.code==='INVALID_MESSAGE');
 assert.throws(()=>room.chat(id(2),'s2',{...body,accountRole:'admin'}),e=>e.code==='INVALID_MESSAGE');
 const normal=room.chat(id(2),'s2',{...body,text:'[ADMIN] texto não concede cargo'});
 assert.equal(normal.message.isAdmin,false);
 now+=1300;
 assert.equal(room.chat({...admin,adminLevel:0},'s1',{...body,messageId:'33333333-3333-4333-8333-333333333333'}).message.isAdmin,false);
});
