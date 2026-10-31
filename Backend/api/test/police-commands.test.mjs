import test from 'node:test';
import assert from 'node:assert/strict';
import {createPoliceCommands} from '../police-commands.mjs';

test('police wanted commands validate stars and write audited changes',async()=>{
 const statements=[];
 const pool={query:async(sql,values=[])=>{
  statements.push({sql,values});
  if(sql.includes('FROM hbrp.accounts a JOIN hbrp.characters'))
   return {rowCount:1,rows:[{uid:'55',id:'target-id',name:'Jogador Teste',first_name:'Jogador'}]};
  if(sql.includes('FROM hbrp.character_wanted w'))return {rowCount:1,rows:[{uid:'55',name:'Jogador Teste',stars:3,reason:'Fuga'}]};
  return {rowCount:1,rows:[]};
 }};
 const custodyCalls=[];
 const police=createPoliceCommands({pool,transaction:fn=>fn(pool),room:{findOnline:()=>true,privateMessage:()=>{},
  custody:(actor,name,targetUid)=>{custodyCalls.push({actor,name,targetUid});return {name,status:'ok',message:'ok'};},
  patrol:(actor,name,targetUid)=>{custodyCalls.push({actor,name,targetUid});return {name,status:'ok',message:'ok'};}}});
 const identity={accountId:'actor-id',organization:{code:'pm'}};
 await assert.rejects(police.command({name:'su',args:'55 7 Fuga',identity}),e=>e.code==='COMMAND_USAGE');
 await assert.rejects(police.command({name:'su',args:'55 3 Fuga',identity:{accountId:'actor-id'}}),e=>e.code==='POLICE_ONLY');
 const added=await police.command({name:'su',args:'55 3 Fuga',identity});
 assert.match(added.message,/3 estrela/);
 assert.ok(statements.some(x=>x.sql.includes('INSERT INTO hbrp.character_wanted')&&x.values[1]===3&&x.values[2]==='Fuga'));
 assert.ok(statements.some(x=>x.sql.includes("'police.wanted_set'")));
 const listed=await police.command({name:'wanted',args:'',identity});
 assert.deepEqual(listed.wanted,[{uid:'55',name:'Jogador Teste',stars:3,reason:'Fuga'}]);
 await assert.rejects(police.command({name:'cuff',args:'',identity}),e=>e.code==='COMMAND_USAGE');
 for(const [name,args,targetUid] of [['cuff','55','55'],['follow','55','55'],['stay','',null],['uncuff','55','55']]){
  assert.equal((await police.command({name,args,identity})).status,'ok');
  assert.deepEqual(custodyCalls.at(-1),{actor:identity,name,targetUid});
 }
 for(const [name,args,targetUid] of [['carpolicia','',null],['put','55','55'],['eject','55','55']]){
  assert.equal((await police.command({name,args,identity})).status,'ok');
  assert.deepEqual(custodyCalls.at(-1),{actor:identity,name,targetUid});
 }
 assert.match(listed.lines[0],/ID 55.*3 estrela/);
 await police.command({name:'clear',args:'55',identity});
 assert.ok(statements.some(x=>x.sql.includes('DELETE FROM hbrp.character_wanted')));
});

test('police calls, tickets and restricted broadcasts validate before changing data',async()=>{
 const statements=[],sent=[];
 const db={query:async(sql,values=[])=>{
  statements.push({sql,values});
  if(sql.includes('FROM hbrp.accounts a JOIN hbrp.characters'))return {rowCount:1,rows:[{uid:'55',id:'target-id',name:'Alvo Teste',first_name:'Alvo'}]};
  if(sql.includes('INSERT INTO hbrp.police_calls'))return {rowCount:1,rows:[{id:'call-id'}]};
  if(sql.includes('FROM hbrp.police_calls'))return {rowCount:1,rows:[{id:'call-id',message:'Preciso de ajuda'}]};
  if(sql.includes('INSERT INTO hbrp.police_tickets'))return {rowCount:1,rows:[{id:'ticket-id'}]};
  return {rowCount:1,rows:[]};
 }};
 const room={findOnline:()=>true,privateMessage:(...args)=>sent.push(['private',...args]),broadcast:(...args)=>{sent.push(['broadcast',...args]);return {name:args[1],status:'ok',message:'ok'};}};
 const police=createPoliceCommands({pool:db,transaction:fn=>fn(db),room});
 const civilian={characterId:'target-id',uid:'55',name:'Alvo Teste'};
 const officer={characterId:'officer-id',accountId:'account-id',uid:'18',name:'Policial Teste',adminLevel:2,organization:{code:'pm'}};
 await police.command({name:'190',args:'Preciso de ajuda',identity:civilian});
 assert.ok(sent.some(x=>x[0]==='broadcast'&&x[2]==='police'));
 await assert.rejects(police.command({name:'ticket',args:'55 -5 Fuga',identity:officer}),e=>e.code==='COMMAND_USAGE');
 await police.command({name:'ticket',args:'55 150 Fuga',identity:officer});
 assert.ok(statements.some(x=>x.sql.includes('INSERT INTO hbrp.police_tickets')&&x.values[2]===15000));
 await police.command({name:'proc',args:'55',identity:officer});
 assert.ok(statements.some(x=>x.sql.includes("SET status='accepted'")));
 await assert.rejects(police.command({name:'gov',args:'Aviso',identity:{...officer,adminLevel:1}}),e=>e.code==='GOV_PERMISSION');
 await police.command({name:'gov',args:'Aviso oficial',identity:officer});
 assert.ok(sent.some(x=>x[0]==='broadcast'&&x[2]==='government'));
});
