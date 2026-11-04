import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createRoom} from '../multiplayer.mjs';
import {ADMIN_COMMANDS,adminCommandAllowed,adminHelp,gameCommandHelp} from '../admin-command-catalog.mjs';

const identity=(level=4)=>({characterId:'rank-four',uid:'4',name:'Gestor Teste',firstName:'Gestor',
 model:'bybeyby',world:'centro_teste',health:100,accountRole:'admin',adminLevel:level,organization:{code:'pm'}});
const clientId=randomUUID();
const frame=sequence=>({clientId,sequence,x:0,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0});
const body=text=>({clientId,messageId:randomUUID(),text});

test('level 4 is exclusive and does not inherit other administrative commands or aliases',()=>{
 assert.equal(adminHelp(4).length,1);assert.match(adminHelp(4)[0],/\/adminpromov/);
 for(const entry of ADMIN_COMMANDS)assert.equal(adminCommandAllowed(4,entry.name),entry.name==='adminpromov',entry.name);
 for(const level of [0,2,3,5])assert.equal(adminCommandAllowed(level,'adminpromov'),false);
 assert.equal(adminCommandAllowed(1,'adminpromov'),true);
 assert.equal(adminCommandAllowed(4,'myastats'),false);
 assert.equal(adminCommandAllowed(3,'makeadmin'),true);
 for(const entry of ADMIN_COMMANDS)assert.equal(adminCommandAllowed(1,entry.name),true,`level 1: ${entry.name}`);
 assert.equal(adminCommandAllowed(1,'myastats'),true);
 const help=gameCommandHelp().find(entry=>entry.usage.startsWith('/adminpromov'));
 assert.equal(help.level,4);assert.match(help.permission,/nível 1.*nível 4/i);
});

test('the room rejects every unrelated admin command for level 4 before applying any mechanics',async()=>{
 let now=1000,sequence=0;const room=createRoom({clock:()=>now}),actor=identity();
 for(const name of [...ADMIN_COMMANDS.map(entry=>entry.name),'myastats'].filter(name=>name!=='adminpromov')){
  room.sync(actor,'session',frame(++sequence));
  assert.throws(()=>room.chat(actor,'session',body('/'+name)),error=>error.code==='ADMIN_ROLE_RESTRICTED',name);
  now+=1300;
 }
 let forwarded;
 room.sync(actor,'session',frame(++sequence));
 const promoted=await room.chat(actor,'session',body('/adminpromov João 2'),request=>{
  forwarded=request;return {name:request.name,status:'ok',message:'Teste'};
 });
 assert.equal(forwarded.name,'adminpromov');assert.equal(forwarded.args,'João 2');assert.equal(promoted.command.status,'ok');
 now+=1300;
 assert.equal((await room.chat(actor,'session',body('/ajuda'))).command.status,'open_help','ordinary player help is retained');
 assert.throws(()=>room.duty(actor,'session',{enabled:true}),error=>error.code==='ADMIN_ROLE_RESTRICTED');
});

test('cached replies and previous police/invisibility state cannot restore inherited privileges to level 4',async()=>{
 let now=1000;const room=createRoom({clock:()=>now,capacity:3}),actor=identity(3),specialist=identity(4);
 room.sync(actor,'session',frame(1));room.duty(actor,'session',{enabled:true});
 const old=body('/speed 4');assert.equal((await room.chat(actor,'session',old)).command.value,4);
 now+=1300;await room.chat(actor,'session',body('/inviz'));
 now+=1300;
 const fresh=room.sync(specialist,'session',frame(2));
 assert.equal(fresh.selfAccountRole,'admin');assert.equal(fresh.selfAdminLevel,4);
 assert.throws(()=>room.chat(specialist,'session',old),error=>error.code==='ADMIN_ROLE_RESTRICTED');
 assert.throws(()=>room.chat(specialist,'session',body('/wanted')),error=>error.code==='ADMIN_ROLE_RESTRICTED');
 const observer={...identity(0),characterId:'observer',uid:'2',accountRole:'user'};
 const observed=room.sync(observer,'observer-session',frame(1));
 assert.equal(observed.peers.find(peer=>peer.uid==='4').policeDuty,false);
 assert.ok(observed.peers.some(peer=>peer.uid==='4'),'a previous invisible state is cleared');
});
