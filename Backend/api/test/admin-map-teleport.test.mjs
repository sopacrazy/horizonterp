import test from 'node:test';
import assert from 'node:assert/strict';
import {createPlayerAdmin} from '../player-admin.mjs';

test('map teleport checks the current server role, exact finite coordinates and records an audit',async()=>{
 let role='user',level=0;const audit=[];
 const db={query:async(sql,args)=>{
  if(sql.startsWith('SELECT id,uid'))return {rows:[{id:'actor',account_role:role,admin_level:level}]};
  if(sql.startsWith('SELECT id FROM hbrp.characters'))return {rows:[{id:'character'}]};
  if(sql.startsWith('INSERT INTO hbrp.audit_events'))audit.push(args);
  return {rows:[]};
 }};
 const admin=createPlayerAdmin({pool:db,session:async()=>({account_id:'actor'}),transaction:fn=>fn(db)});
 await assert.rejects(admin.mapTeleport('token',{x:100,z:1000}),e=>e.code==='ADMIN_REQUIRED');
 role='admin';level=1;
 for(const body of [{x:NaN,z:1000},{x:100,z:Infinity},{x:'100',z:1000},{x:4201,z:0},{x:0,z:0,y:12},null])
  await assert.rejects(admin.mapTeleport('token',body),e=>e.code==='INVALID_MAP_POINT');
 assert.equal(audit.length,0);
 const result=await admin.mapTeleport('token',{x:2489.3,z:1668.5});
 assert.deepEqual(result,{authorized:true,x:2489.3,z:1668.5});assert.equal(audit.length,1);
 assert.deepEqual(JSON.parse(audit[0][2]),{x:2489.3,z:1668.5});
 role='user';level=0;
 await assert.rejects(admin.mapTeleport('token',{x:100,z:1000}),e=>e.code==='ADMIN_REQUIRED');
});
