import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from '../server.mjs';

test('HTTP police-car route authenticates entry and accepts only server-granted driving without a mission loan',{timeout:120000},async()=>{
 const db=new PGlite();let server;
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  for(const name of (await fs.readdir(new URL('../../database/',import.meta.url))).filter(n=>/^\d{3}_.*\.sql$/.test(n)).sort())
   await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  await db.exec('RESET ROLE');
  const account=(await db.query("INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES('police-driver@example.invalid','admin',1) RETURNING id")).rows[0].id;
  const character=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,'Lucas Teste','Lucas','Teste','male') RETURNING id",[account])).rows[0].id;
  await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[character]);
  await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z) SELECT $1,id,10,0,10 FROM hbrp.worlds',[character]);
  await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character]);
  const token='p'.repeat(43);
  await db.query("INSERT INTO hbrp.sessions(account_id,token_hash,expires_at) VALUES($1,sha256(convert_to($2,'UTF8')),now()+interval '1 day')",[account,token]);
  await db.exec('SET ROLE hbrp_app');
  const pool={query:(sql,args)=>db.query(sql,args),connect:async()=>({query:(sql,args)=>db.query(sql,args),release(){}})};
  const errors=[];
  server=http.createServer(createApiHandler(pool,{authMode:'legacy',log(){},errorLog:line=>errors.push(line)}));
  await new Promise(done=>server.listen(0,'127.0.0.1',done));
  const base='http://127.0.0.1:'+server.address().port;
  async function post(path,body,authorized=true){
   const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json',...(authorized?{Authorization:'Bearer '+token}:{})},body:JSON.stringify(body)});
   return {status:response.status,...await response.json()};
  }
  let sequence=0;const clientId=randomUUID();
  const move=extra=>post('/v1/multiplayer/sync',{clientId,sequence:++sequence,x:10,y:0,z:10,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});
  assert.equal((await post('/v1/police/car',{action:'enter',carId:'pm-1'},false)).status,401);
  assert.equal((await move({driving:true})).code,'MISSION_VEHICLE_UNAVAILABLE');
  assert.equal((await move({})).status,200);
  assert.equal((await post('/v1/police/duty',{enabled:true})).status,200);
  await move({});
  const spawned=await post('/v1/multiplayer/chat',{clientId,messageId:randomUUID(),text:'/carpolicia'});
  assert.equal(spawned.status,200);assert.equal(spawned.command.status,'ok');
  const car=(await move({})).policeCars[0];assert.ok(car?.id);
  assert.equal((await post('/v1/police/car',{action:'enter',carId:car.id})).status,200);
  const drive=await move({driving:true,policeCarId:car.id,x:12,z:16,speed:2});
  assert.equal(drive.status,200,'police driving does not require a mission loan');
  assert.equal(drive.selfPoliceCarId,car.id);assert.equal(drive.policeCars[0].x,12);
  assert.equal((await move({driving:true,policeCarId:'pm-999'})).code,'POLICE_CAR_FORBIDDEN','an arbitrary ID cannot bypass the mission-loan check');
  await move({driving:true,policeCarId:car.id,x:12,z:16,speed:0});
  const exit=await post('/v1/police/car',{action:'exit',carId:car.id});assert.equal(exit.status,200);assert.equal(exit.driving,false);
  assert.equal(errors.length,0);
 }finally{
  if(server)await new Promise(done=>server.close(done));
  await db.close();
 }
});
