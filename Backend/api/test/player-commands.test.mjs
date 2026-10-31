import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createRoom} from '../multiplayer.mjs';
import {createPlayerCommands} from '../player-commands.mjs';
const identity=n=>({characterId:'character-'+n,accountId:'account-'+n,uid:String(n),name:'Player Teste',firstName:'Player',model:'bybeyby',world:'centro_teste',accountRole:'player',adminLevel:0});
const frame=(sequence,x=0)=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x,y:0,z:0,yaw:0,speed:0,jumping:false,jumpPhase:0});
test('player commands isolate whispers and proximity shouts, with mute enforcement',async()=>{
 let now=1000;const room=createRoom({clock:()=>now,capacity:4});
 const a=identity(1),b=identity(2),c=identity(3),admin={...identity(4),accountRole:'admin',adminLevel:3};
 [a,b,c,admin].forEach((who,i)=>room.sync(who,'s'+who.uid,frame(1,[0,49,51,2][i])));
 const send=(who,text)=>room.chat(who,'s'+who.uid,{clientId:frame(1).clientId,messageId:randomUUID(),text});
 await send(a,'/s Olá vizinhos');
 assert.ok(room.sync(b,'s2',frame(2,49)).messages.some(m=>m.channel==='shout'));
 assert.ok(!room.sync(c,'s3',frame(2,51)).messages.some(m=>m.channel==='shout'));
 now+=1300;await send(a,'/w 2 segredo');
 assert.ok(room.sync(b,'s2',frame(3,49)).messages.some(m=>m.text==='segredo'));
 assert.ok(!room.sync(c,'s3',frame(3,51)).messages.some(m=>m.text==='segredo'));
 assert.ok(!room.sync(admin,'s4',frame(2,2)).messages.some(m=>m.text==='segredo'));
 now+=1300;await send(admin,'/mute 1 1 teste');now+=1300;
 assert.throws(()=>send(a,'/s não deve sair'),e=>e.code==='PLAYER_MUTED');
 now+=1300;assert.throws(()=>send(a,'/w 2 não deve sair'),e=>e.code==='PLAYER_MUTED');
});
test('ordinary player gets player help and explicit pending actions without admin privilege',async()=>{
 let now=1000;const room=createRoom({clock:()=>now,ttl:120000});const who=identity(1);room.sync(who,'s1',frame(1));
 const send=text=>room.chat(who,'s1',{clientId:frame(1).clientId,messageId:randomUUID(),text});
 const help=await send('/ajuda');assert.equal(help.command.status,'open_help');
 assert.ok(help.command.help.every(e=>e.level===0&&e.group==='Jogador'));
 for(const name of ['me','do','try','mask','house','biz','daily','healme','j','v']){
  now+=1300;assert.equal((await send('/'+name)).command.status,'not_implemented');
 }
 for(const name of ['gps','phone','quests','stats','anim']){now+=1300;assert.equal((await send('/'+name)).command.status,'player_panel');}
 now+=1300;assert.throws(()=>send('/makeadmin 1 3'),e=>e.code==='ADMIN_ONLY');
});
test('report submission persists once, alerts admins and myreports scopes query to authenticated character',async()=>{
 const statements=[],alerts=[];const who=identity(1);
 const db={query:async(sql,values=[])=>{
  statements.push({sql,values});
  if(sql.includes('INSERT INTO'))return {rows:[{id:'report-id'}],rowCount:1};
  if(sql.startsWith('SELECT id,message'))return {rows:[{id:'report-id',message:'Ajuda por favor',status:'open',created_at:new Date()}],rowCount:1};
  return {rows:[],rowCount:0};
 }};
 const commands=createPlayerCommands({pool:db,transaction:fn=>fn(db),room:{broadcast:(...args)=>alerts.push(args)}});
 const result=await commands.command({name:'report',args:'Ajuda por favor',identity:who});assert.match(result.message,/report-id/);
 assert.equal(alerts[0][1],'admin');
 const history=await commands.command({name:'myreports',args:'',identity:who});assert.equal(history.lines.length,1);
 assert.ok(statements.some(s=>s.sql.includes('WHERE character_id=$1')&&s.values[0]===who.characterId));
 db.query=async()=>({rowCount:1,rows:[]});
 await assert.rejects(commands.command({name:'report',args:'Outra ajuda',identity:who}),e=>e.code==='REPORT_RATE_LIMITED');
});
