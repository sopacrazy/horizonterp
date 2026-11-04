import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import {PGlite} from '@electric-sql/pglite';
import {createCriminalEconomy,createCriminalEconomyRouteHandler} from '../criminal-economy.mjs';

class Pool{
 constructor(db){this.db=db;this.tail=Promise.resolve();this.history=[];}
 async connect(){const pending=this.tail;let release;this.tail=new Promise(done=>release=done);await pending;
  return {query:(sql,args)=>{this.history.push(sql.replace(/\s+/g,' ').trim());return this.db.query(sql,args);},release};}
 async query(sql,args){const client=await this.connect();try{return await client.query(sql,args);}finally{client.release();}}
}
const transaction=pool=>async fn=>{const client=await pool.connect();try{await client.query('BEGIN');const result=await fn(client);
 await client.query('COMMIT');return result;}catch(error){await client.query('ROLLBACK');throw error;}finally{client.release();}};

async function character(db,email,name){
 const account=(await db.query('INSERT INTO hbrp.accounts(email) VALUES($1) RETURNING id,uid::text',[email])).rows[0];
 const person=(await db.query(`INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender)
  VALUES($1,$2,$3,$4,'male') RETURNING id`,[account.id,name,...name.split(' ')])).rows[0];
 await db.query('INSERT INTO hbrp.inventories(character_id,capacity) VALUES($1,30)',[person.id]);
 await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[person.id]);
 return {accountId:account.id,characterId:person.id,uid:account.uid,name,health:100};
}

test('criminal economy pilot is additive, atomic, audited and retry-safe',{timeout:240000},async t=>{
 const db=new PGlite();
 try{
  await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
  const migrations=(await fs.readdir(new URL('../../database/',import.meta.url))).filter(name=>/^\d{3}_.*\.sql$/.test(name)).sort();
  for(const name of migrations)await db.exec(await fs.readFile(new URL('../../database/'+name,import.meta.url),'utf8'));
  const leader=await character(db,'pilot-leader@example.invalid','Lider Piloto');
  const member=await character(db,'pilot-member@example.invalid','Membro Piloto');
  const outsider=await character(db,'pilot-other@example.invalid','Outro Piloto');
  const admin=await character(db,'pilot-admin@example.invalid','Admin Piloto');
  const admin3=await character(db,'pilot-admin3@example.invalid','Gestor Piloto');
  const admin4=await character(db,'pilot-admin4@example.invalid','Restrito Piloto');
  await db.query("UPDATE hbrp.accounts SET account_role='admin',admin_level=1 WHERE id=$1",[admin.accountId]);
  await db.query("UPDATE hbrp.accounts SET account_role='admin',admin_level=3 WHERE id=$1",[admin3.accountId]);
  await db.query("UPDATE hbrp.accounts SET account_role='admin',admin_level=4 WHERE id=$1",[admin4.accountId]);
  const organization=(await db.query("SELECT id FROM hbrp.organizations WHERE code='faccao_roxa'")).rows[0];
  await db.query(`INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code)
   VALUES($1,$2,'lider'),($1,$3,'membro')`,[organization.id,leader.characterId,member.characterId]);
  await db.exec('SET ROLE hbrp_app');
  const pool=new Pool(db);let now=new Date('2026-11-03T12:00:00.000Z');
  const positions=new Map();
  const move=async(person,locationCode)=>{
   const point=(await pool.query('SELECT x,y,z,world FROM hbrp.criminal_economy_locations WHERE code=$1',[locationCode])).rows[0];
   positions.set(person.characterId,point);
  };
  const economy=createCriminalEconomy({pool,transaction:transaction(pool),clock:()=>new Date(now),
   getPresence:identity=>positions.get(identity.characterId)});

  await t.test('migration seeds only fictional definitions and both pilot organizations',async()=>{
   assert.equal((await pool.query('SELECT max(version)::int AS version FROM hbrp.schema_migrations')).rows[0].version,18);
   assert.deepEqual((await pool.query(`SELECT code FROM hbrp.organizations
    WHERE code IN ('faccao_roxa','mafia_branca') ORDER BY code`)).rows.map(row=>row.code),['faccao_roxa','mafia_branca']);
   const definitions=(await pool.query(`SELECT code,metadata->>'fictional' AS fictional FROM hbrp.item_definitions
    WHERE metadata @> '{"criminalEconomyPilot":true}'::jsonb ORDER BY code`)).rows;
   assert.equal(definitions.length,9);assert.ok(definitions.every(row=>row.fictional==='true'));
  });

  await t.test('activity reward is server-defined, cooldown protected and idempotent',async()=>{
   await move(leader,'urban_recycling_ls');
   const started=await economy.startActivity({identity:leader,activityCode:'urban_recycling',idempotencyKey:randomUUID()});
   assert.equal((await economy.state({identity:leader})).activeActivity.activityRunId,started.activityRunId);
   await assert.rejects(economy.completeActivity({identity:leader,activityCode:'urban_recycling',
    activityRunId:started.activityRunId,idempotencyKey:randomUUID()}),error=>error.code==='ACTIVITY_NOT_READY');
   now=new Date(now.getTime()+20000);
   const request=randomUUID();
   const first=await economy.completeActivity({identity:leader,activityCode:'urban_recycling',activityRunId:started.activityRunId,idempotencyKey:request});
   assert.deepEqual(first.reward,{itemCode:'pilot_scrap',quantity:3});assert.equal(first.inventoryQuantity,3);
   const replay=await economy.completeActivity({identity:leader,activityCode:'urban_recycling',activityRunId:started.activityRunId,idempotencyKey:request});
   assert.equal(replay.idempotentReplay,true);assert.equal(replay.inventoryQuantity,3);
   await assert.rejects(economy.startActivity({identity:leader,activityCode:'urban_recycling',idempotencyKey:randomUUID()}),
    error=>error.code==='ACTIVITY_COOLDOWN');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.criminal_activity_runs')).rows[0].n,1);
   now=new Date(now.getTime()+21000);
   const second=await economy.startActivity({identity:leader,activityCode:'urban_recycling',idempotencyKey:randomUUID()});
   now=new Date(now.getTime()+20000);
   assert.equal((await economy.completeActivity({identity:leader,activityCode:'urban_recycling',activityRunId:second.activityRunId,idempotencyKey:randomUUID()})).inventoryQuantity,6);
  });

  await t.test('warehouse transfers preserve quantities, enforce rank and write immutable movements',async()=>{
   await move(leader,'faccao_roxa_warehouse');await move(member,'faccao_roxa_warehouse');
   const request=randomUUID(),before=pool.history.length;
   const [first,second]=await Promise.all([
    economy.deposit({identity:leader,itemCode:'pilot_scrap',quantity:6,idempotencyKey:request}),
    economy.deposit({identity:leader,itemCode:'pilot_scrap',quantity:6,idempotencyKey:request})
   ]);
   const deposited=first.idempotentReplay?second:first,replayed=first.idempotentReplay?first:second;
   assert.equal(deposited.inventoryQuantity,0);assert.equal(deposited.warehouseQuantity,6);
   assert.equal(replayed.idempotentReplay,true);assert.equal(replayed.warehouseQuantity,6);
   const issued=pool.history.slice(before);
   assert.ok(issued.filter(sql=>sql.startsWith('SELECT pg_advisory_xact_lock')).length>=2);
   assert.ok(issued.findIndex(sql=>sql.startsWith('SELECT pg_advisory_xact_lock'))<
    issued.findIndex(sql=>sql.includes('FROM hbrp.organization_members')));
   const withdrawn=await economy.withdraw({identity:leader,itemCode:'pilot_scrap',quantity:1,idempotencyKey:randomUUID()});
   assert.equal(withdrawn.inventoryQuantity,1);assert.equal(withdrawn.warehouseQuantity,5);
   await assert.rejects(economy.withdraw({identity:member,itemCode:'pilot_scrap',quantity:1,idempotencyKey:randomUUID()}),
    error=>error.code==='ECONOMY_PERMISSION_DENIED');
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.organization_warehouse_movements')).rows[0].n,2);
   assert.equal((await pool.query("SELECT count(*)::int AS n FROM hbrp.audit_events WHERE action LIKE 'criminal_economy.%'")).rows[0].n,6);
  });

  await t.test('one abstract recipe consumes warehouse inputs atomically',async()=>{
   const inv=(await pool.query('SELECT id FROM hbrp.inventories WHERE character_id=$1',[leader.characterId])).rows[0];
   await pool.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
    VALUES($1,'pilot_scrap',10,12),($1,'pilot_crystal',11,8)`,[inv.id]);
   await economy.deposit({identity:leader,itemCode:'pilot_scrap',quantity:12,idempotencyKey:randomUUID()});
   await economy.deposit({identity:leader,itemCode:'pilot_crystal',quantity:8,idempotencyKey:randomUUID()});
   await move(leader,'faccao_roxa_craft');
   const request=randomUUID(),crafted=await economy.craft({identity:leader,recipeCode:'pilot_logistics_package',batches:2,idempotencyKey:request});
   assert.deepEqual(crafted.output,{itemCode:'pilot_logistics_crate',quantity:2});
   assert.equal((await economy.craft({identity:leader,recipeCode:'pilot_logistics_package',batches:2,idempotencyKey:request})).idempotentReplay,true);
   assert.equal((await economy.state({identity:leader})).warehouse.some(i=>i.itemCode==='pilot_logistics_crate'),false);
   await assert.rejects(economy.finishProduction({identity:leader,productionId:crafted.productionId,idempotencyKey:randomUUID()}),e=>e.code==='PRODUCTION_NOT_READY');
   now=new Date(crafted.readyAt);
   const completed=await economy.finishProduction({identity:leader,productionId:crafted.productionId,idempotencyKey:randomUUID()});
   assert.equal(completed.output.quantity,2);
   const balances=(await pool.query(`SELECT item_code,quantity::int FROM hbrp.organization_warehouse_items
    WHERE warehouse_id=(SELECT id FROM hbrp.organization_warehouses WHERE organization_id=$1) ORDER BY item_code`,[organization.id])).rows;
   assert.deepEqual(balances,[{item_code:'pilot_logistics_crate',quantity:2},{item_code:'pilot_scrap',quantity:5}]);
   assert.equal((await pool.query('SELECT count(*)::int AS n FROM hbrp.criminal_production_runs')).rows[0].n,1);
  });

  await t.test('craft limit counts every consumed and produced unit',async()=>{
   // The leader limit is 1000; each batch moves 6+4 input units and 1 output unit.
   // 91 batches therefore move 1001 units even though only 91 output units are produced.
   await assert.rejects(economy.craft({identity:leader,recipeCode:'pilot_logistics_package',batches:91,
    idempotencyKey:randomUUID()}),error=>error.code==='ECONOMY_OPERATION_LIMIT');
  });

  await t.test('presence, living character, world and organization points are required',async()=>{
   const input={identity:member,activityCode:'urban_recycling',idempotencyKey:randomUUID()};
   positions.delete(member.characterId);
   await assert.rejects(economy.startActivity(input),error=>error.code==='PRESENCE_REQUIRED');
   await move(member,'urban_recycling_ls');
   const position=positions.get(member.characterId);
   positions.set(member.characterId,{...position,world:'another_world'});
   await assert.rejects(economy.startActivity(input),error=>error.code==='LOCATION_REQUIRED');
   positions.set(member.characterId,{...position,x:position.x+100});
   await assert.rejects(economy.startActivity(input),error=>error.code==='LOCATION_REQUIRED');
   positions.set(member.characterId,{...position,x:NaN});
   await assert.rejects(economy.startActivity(input),error=>error.code==='PRESENCE_REQUIRED');
   positions.set(member.characterId,{...position,driving:true});
   await assert.rejects(economy.startActivity(input),error=>error.code==='ON_FOOT_REQUIRED');
   positions.set(member.characterId,position);
   await assert.rejects(economy.startActivity({...input,identity:{...member,health:0}}),error=>error.code==='CHARACTER_DEAD');
   await pool.query('UPDATE hbrp.character_stats SET health=0 WHERE character_id=$1',[member.characterId]);
   await assert.rejects(economy.startActivity(input),error=>error.code==='CHARACTER_DEAD');
   await pool.query('UPDATE hbrp.character_stats SET health=100 WHERE character_id=$1',[member.characterId]);
   await assert.rejects(economy.startActivity({...input,identity:{...member,characterId:leader.characterId}}),error=>error.code==='CHARACTER_UNAVAILABLE');
   const missingPresence=createCriminalEconomy({pool,transaction:transaction(pool)});
   await assert.rejects(missingPresence.startActivity(input),error=>error.code==='PRESENCE_UNAVAILABLE');
   await move(leader,'mafia_branca_warehouse');
   await assert.rejects(economy.deposit({identity:leader,itemCode:'pilot_scrap',quantity:1,idempotencyKey:randomUUID()}),
    error=>error.code==='LOCATION_REQUIRED');
   await move(leader,'faccao_roxa_warehouse');
   positions.set(leader.characterId,{...positions.get(leader.characterId),x:2491.3});
   await assert.rejects(economy.craft({identity:leader,recipeCode:'pilot_logistics_package',idempotencyKey:randomUUID()}),
    error=>error.code==='LOCATION_REQUIRED');
  });

  await t.test('activity sessions survive service recreation, bind actor and point, and grant exactly once',async()=>{
   await move(member,'test_mining_ls');const startRequest=randomUUID();
   const started=await economy.startActivity({identity:member,activityCode:'test_mining',idempotencyKey:startRequest});
   assert.equal(new Date(started.readyAt)-new Date(started.startedAt),30000);
   const restored=createCriminalEconomy({pool,transaction:transaction(pool),clock:()=>new Date(now),
    getPresence:identity=>positions.get(identity.characterId)});
   const retry=await restored.startActivity({identity:member,activityCode:'test_mining',idempotencyKey:startRequest});
   assert.equal(retry.idempotentReplay,true);assert.equal(retry.activityRunId,started.activityRunId);
   await assert.rejects(restored.startActivity({identity:member,activityCode:'test_mining',idempotencyKey:randomUUID()}),
    error=>error.code==='ACTIVITY_ALREADY_ACTIVE');
   await move(outsider,'test_mining_ls');
   await assert.rejects(restored.startActivity({identity:outsider,activityCode:'test_mining',idempotencyKey:startRequest}),
    error=>error.code==='IDEMPOTENCY_CONFLICT');
   const complete={identity:member,activityCode:'test_mining',activityRunId:started.activityRunId,idempotencyKey:randomUUID()};
   await assert.rejects(restored.completeActivity({...complete,identity:outsider}),error=>error.code==='ACTIVITY_RUN_NOT_FOUND');
   now=new Date(now.getTime()+29999);
   await assert.rejects(restored.completeActivity(complete),error=>error.code==='ACTIVITY_NOT_READY');
   now=new Date(now.getTime()+1);await move(member,'urban_recycling_ls');
   await assert.rejects(restored.completeActivity(complete),error=>error.code==='LOCATION_REQUIRED');
   await move(member,'test_mining_ls');
   const completed=await Promise.all([restored.completeActivity(complete),restored.completeActivity(complete)]);
   assert.equal(completed.filter(row=>row.idempotentReplay).length,1);
   assert.equal(completed[0].inventoryQuantity,2);
   await assert.rejects(restored.completeActivity({...complete,idempotencyKey:randomUUID()}),error=>error.code==='ACTIVITY_ALREADY_COMPLETED');
   await assert.rejects(restored.completeActivity({...complete,activityRunId:randomUUID()}),error=>error.code==='IDEMPOTENCY_CONFLICT');
   assert.equal((await restored.state({identity:member})).activeActivity,null);
  });

  await t.test('organization administration uses fresh roles, protects hierarchy and audits removals',async()=>{
   const assignment={uid:outsider.uid,organizationCode:'mafia_branca',rankCode:'lider'};
   await assert.rejects(economy.setOrganization({identity:{...member,accountRole:'admin',adminLevel:1},...assignment}),
    error=>error.code==='ECONOMY_ADMIN_REQUIRED');
   await assert.rejects(economy.adminOrganizations({identity:admin4}),error=>error.code==='ECONOMY_ADMIN_REQUIRED');
   await assert.rejects(economy.setOrganization({identity:admin4,...assignment}),error=>error.code==='ECONOMY_ADMIN_REQUIRED');
   await assert.rejects(economy.setOrganization({identity:admin3,...assignment,uid:admin.uid}),error=>error.code==='ADMIN_HIERARCHY');
   await assert.rejects(economy.setOrganization({identity:admin3,...assignment,uid:admin3.uid}),error=>error.code==='ADMIN_HIERARCHY');
   await assert.rejects(economy.setOrganization({identity:admin3,...assignment,uid:admin4.uid}),error=>error.code==='ADMIN_HIERARCHY');
   await assert.rejects(economy.setOrganization({identity:admin,...assignment,rankCode:'injected'}),error=>error.code==='INVALID_ORGANIZATION_RANK');
   await assert.rejects(economy.setOrganization({identity:admin,...assignment,organizationCode:'none'}),error=>error.code==='INVALID_ORGANIZATION_RANK');
   const changed=await economy.setOrganization({identity:admin3,...assignment});
   assert.equal(changed.organization.code,'mafia_branca');
   const all=await economy.adminOrganizations({identity:admin});
   assert.equal(all.organizations.length,6);
   assert.ok(all.organizations.every(row=>row.ranks.includes('lider')));
   assert.ok(all.organizations.find(row=>row.code==='mafia_branca').members.some(row=>row.uid===outsider.uid&&row.rank==='lider'));
   await economy.setOrganization({identity:admin,uid:outsider.uid,organizationCode:'none',rankCode:'none'});
   assert.equal((await economy.state({identity:outsider})).organization,null);
   await pool.query("UPDATE hbrp.accounts SET account_role='user',admin_level=0 WHERE id=$1",[admin3.accountId]);
   await assert.rejects(economy.setOrganization({identity:{...admin3,accountRole:'admin',adminLevel:3},...assignment}),
    error=>error.code==='ECONOMY_ADMIN_REQUIRED');
   await economy.setOrganization({identity:admin,...assignment});
   assert.equal((await pool.query("SELECT count(*)::int AS n FROM hbrp.audit_events WHERE action='criminal_economy.organization_set'")).rows[0].n,3);
  });

  await t.test('state and latest-50 history isolate characters and organizations',async()=>{
   const state=await economy.state({identity:outsider});
   assert.ok(state.locations.every(row=>['activity','shop'].includes(row.kind)||row.organizationCode==='mafia_branca'));
   assert.equal(state.locations.length,10);
   assert.equal((await economy.history({identity:outsider})).operations.length,0);
   const activity=await economy.startActivity({identity:outsider,activityCode:'test_mining',idempotencyKey:randomUUID()});
   now=new Date(now.getTime()+30000);
   await economy.completeActivity({identity:outsider,activityCode:'test_mining',activityRunId:activity.activityRunId,idempotencyKey:randomUUID()});
   await move(outsider,'mafia_branca_warehouse');
   await economy.deposit({identity:outsider,itemCode:'pilot_crystal',quantity:2,idempotencyKey:randomUUID()});
   const own=await economy.history({identity:outsider}),other=await economy.history({identity:leader});
   assert.equal(own.activities.length,1);assert.equal(own.activities[0].activityRunId,activity.activityRunId);
   assert.equal(own.operations.length,1);assert.equal(own.operations[0].organization,'mafia_branca');
   assert.ok(other.activities.every(row=>row.activityCode==='urban_recycling'));
   assert.ok(other.operations.every(row=>row.organization==='faccao_roxa'));
   assert.equal((await economy.history({identity:member})).activities.length,1);
   assert.ok((await economy.history({identity:member})).operations.every(row=>row.organization==='faccao_roxa'));
   // A bounded history must remain bounded after prolonged production use.
   for(let index=0;index<51;index++)await pool.query(`INSERT INTO hbrp.criminal_activity_runs
    (request_id,character_id,activity_code,reward_item_code,reward_quantity,result)
    VALUES($1,$2,'test_mining','pilot_crystal',1,'{}')`,[randomUUID(),outsider.characterId]);
   assert.equal((await economy.history({identity:outsider})).activities.length,50);
  });

  await t.test('composite foreign keys reject cross-warehouse ledger rows and ledgers stay append-only',async()=>{
   const operation=(await pool.query('SELECT id,warehouse_id FROM hbrp.organization_warehouse_operations LIMIT 1')).rows[0];
   const other=(await pool.query('SELECT id FROM hbrp.organization_warehouses WHERE id<>$1 LIMIT 1',[operation.warehouse_id])).rows[0];
   await assert.rejects(pool.query(`INSERT INTO hbrp.organization_warehouse_movements
    (operation_id,warehouse_id,item_code,quantity_delta,balance_after)
    VALUES($1,$2,'pilot_scrap',1,1)`,[operation.id,other.id]));
   const grants=(await pool.query(`SELECT privilege_type FROM information_schema.role_table_grants
    WHERE grantee='hbrp_app' AND table_schema='hbrp' AND table_name='organization_warehouse_movements'
    ORDER BY privilege_type`)).rows.map(row=>row.privilege_type);
   assert.deepEqual(grants,['INSERT','SELECT']);
  });

  await t.test('route adapter exposes only the mounted pilot routes',async()=>{
   const handle=createCriminalEconomyRouteHandler(economy);
   const state=await handle({method:'GET',path:'/v1/criminal-economy',identity:leader});
   assert.equal(state.organization.code,'faccao_roxa');assert.equal(state.recipes.length,2);
   await assert.rejects(handle({method:'POST',path:'/v1/criminal-economy/warehouse/deposit',identity:leader,
    body:{itemCode:'pilot_scrap',quantity:1,requestId:randomUUID(),unexpected:true}}),error=>error.code==='INVALID_BODY');
   await assert.rejects(handle({method:'POST',path:'/v1/criminal-economy/production/craft',identity:leader,
    body:{recipeCode:'pilot_logistics_package',requestId:randomUUID()}}),error=>error.code==='INVALID_BODY');
   await assert.rejects(handle({method:'POST',path:'/v1/criminal-economy/warehouse/deposit',identity:leader,
    body:{itemCode:'pilot_scrap',quantity:'1',requestId:randomUUID()}}),error=>error.code==='INVALID_QUANTITY');
   assert.equal(await handle({method:'GET',path:'/v1/not-mounted',identity:leader}),null);
  });
  await t.test('cancel and expiry release activities without awarding items',async()=>{
   now=new Date(now.getTime()+60000);await move(member,'urban_recycling_ls');
   const run=await economy.startActivity({identity:member,activityCode:'urban_recycling',idempotencyKey:randomUUID()});
   const input={identity:member,activityRunId:run.activityRunId,idempotencyKey:randomUUID()};
   await assert.rejects(economy.cancelActivity({...input,identity:outsider}),e=>e.code==='ACTIVITY_RUN_NOT_FOUND');
   await economy.cancelActivity(input);assert.equal((await economy.cancelActivity(input)).idempotentReplay,true);
   now=new Date(now.getTime()+21000);
   await assert.rejects(economy.completeActivity({...input,activityCode:'urban_recycling',idempotencyKey:randomUUID()}),e=>e.code==='ACTIVITY_CLOSED');
   const expired=await economy.startActivity({identity:member,activityCode:'urban_recycling',idempotencyKey:randomUUID()});
   now=new Date(new Date(expired.expiresAt).getTime());
   assert.equal((await economy.state({identity:member})).activeActivity,null);
   await assert.rejects(economy.completeActivity({identity:member,activityCode:'urban_recycling',activityRunId:expired.activityRunId,idempotencyKey:randomUUID()}),e=>e.code==='ACTIVITY_CLOSED');
   await economy.startActivity({identity:member,activityCode:'urban_recycling',idempotencyKey:randomUUID()});
  });
  await t.test('store conserves goods and cash, rejects stale prices and replays purchases once',async()=>{
   await pool.query(`INSERT INTO hbrp.organization_warehouse_items(warehouse_id,item_code,quantity)
    SELECT id,'pilot_logistics_crate',5 FROM hbrp.organization_warehouses WHERE organization_id=$1
    ON CONFLICT(warehouse_id,item_code) DO UPDATE SET quantity=5`,[organization.id]);
   const wallet=(await pool.query("INSERT INTO hbrp.wallets(character_id,currency) VALUES($1,'cash') RETURNING id",[member.characterId])).rows[0];
   await pool.query("SELECT hbrp.change_balance($1,10000,$2,'test_fixture')",[wallet.id,randomUUID()]);
   await move(leader,'faccao_roxa_shop');await move(member,'faccao_roxa_shop');
   const supply={identity:leader,operation:'supply',shopCode:'faccao_roxa_shop',itemCode:'pilot_logistics_crate',quantity:3,priceCents:2500,idempotencyKey:randomUUID()};
   await assert.rejects(economy.shopOperation({...supply,identity:member}),e=>e.code==='ECONOMY_PERMISSION_DENIED');
   await economy.shopOperation(supply);assert.equal((await economy.shopOperation(supply)).idempotentReplay,true);
   await assert.rejects(economy.shopOperation({...supply,identity:outsider,idempotencyKey:randomUUID()}),e=>e.code==='SHOP_PERMISSION_DENIED');
   await assert.rejects(economy.shopOperation({...supply,quantity:-1,idempotencyKey:randomUUID()}),e=>e.code==='INVALID_QUANTITY');
   await assert.rejects(economy.shopOperation({...supply,operation:'buy',identity:outsider,quantity:1,idempotencyKey:randomUUID()}),e=>e.code==='LOCATION_REQUIRED');
   await move(outsider,'faccao_roxa_shop');
   await assert.rejects(economy.shopOperation({...supply,operation:'buy',identity:outsider,quantity:1,idempotencyKey:randomUUID()}),e=>e.code==='INSUFFICIENT_FUNDS');
   await economy.shopOperation({...supply,operation:'price',priceCents:3000,idempotencyKey:randomUUID()});
   await economy.shopOperation({...supply,operation:'price',priceCents:2500,idempotencyKey:randomUUID()});
   const buy={...supply,identity:member,operation:'buy',quantity:2,idempotencyKey:randomUUID()};
   await assert.rejects(economy.shopOperation({...buy,priceCents:100}),e=>e.code==='PRICE_CHANGED');
   const [a,b]=await Promise.all([economy.shopOperation(buy),economy.shopOperation(buy)]);
   assert.equal([a,b].filter(x=>x.idempotentReplay).length,1);
   assert.equal((await economy.state({identity:member})).cashCents,5000);
   assert.equal((await economy.state({identity:leader})).shops.find(x=>x.code===supply.shopCode).proceedsCents,5000);
   await assert.rejects(economy.shopOperation({...buy,idempotencyKey:randomUUID()}),e=>e.code==='SHOP_OUT_OF_STOCK');
   await assert.rejects(economy.shopOperation({...buy,quantity:1,priceCents:7500,idempotencyKey:randomUUID()}),e=>e.code==='PRICE_CHANGED');
   await economy.shopOperation({...supply,operation:'recall',quantity:1,idempotencyKey:randomUUID()});
   assert.equal((await economy.state({identity:leader})).warehouse.find(x=>x.itemCode===supply.itemCode).quantity,3);
   await economy.shopOperation({identity:leader,operation:'proceeds',shopCode:supply.shopCode,quantity:5000,idempotencyKey:randomUUID()});
   assert.equal((await economy.state({identity:leader})).cashCents,5000);
   await assert.rejects(economy.shopOperation({identity:leader,operation:'proceeds',shopCode:supply.shopCode,quantity:1,idempotencyKey:randomUUID()}),e=>e.code==='INSUFFICIENT_PROCEEDS');
   assert.ok((await economy.history({identity:member})).commerce.some(x=>x.operation==='buy'));
   await move(member,'urban_recycling_ls');
   await assert.rejects(economy.shopOperation({...buy,quantity:1,idempotencyKey:randomUUID()}),e=>e.code==='LOCATION_REQUIRED');
  });
  await t.test('all six organizations enforce exclusive recipes, timers, persistence and refunds',async()=>{
   const catalog=[['faccao_roxa','rp_parafal',1],['faccao_verde','rp_ak47',1],['faccao_vermelha','rp_m4',1],
    ['mafia_branca','rp_ammo_parafal',30],['mafia_laranja','rp_ammo_ak47',30],['mafia_preta','rp_ammo_m4',30]];
   const jobs=[];
   for(const [org,recipe,quantity] of catalog){
    await db.exec('SET ROLE hbrp_owner');
    const actor=await character(db,org+'@example.invalid','Teste '+org.split('_')[1]);
    await db.query(`INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code)
     SELECT id,$1,'lider' FROM hbrp.organizations WHERE code=$2`,[actor.characterId,org]);
    await db.query(`INSERT INTO hbrp.organization_warehouse_items(warehouse_id,item_code,quantity)
     SELECT w.id,v.code,20 FROM hbrp.organization_warehouses w JOIN hbrp.organizations o ON o.id=w.organization_id
     CROSS JOIN (VALUES ('pilot_scrap'),('pilot_crystal')) v(code) WHERE o.code=$1
     ON CONFLICT(warehouse_id,item_code) DO UPDATE SET quantity=20`,[org]);
    await db.exec('SET ROLE hbrp_app');
    const state=await economy.state({identity:actor});
    assert.ok(state.recipes.some(r=>r.code===recipe));
    assert.equal(state.recipes.filter(r=>r.code.startsWith('rp_')).length,1);
    await move(actor,org+'_craft');
    await assert.rejects(economy.craft({identity:actor,recipeCode:recipe==='rp_m4'?'rp_parafal':'rp_m4',idempotencyKey:randomUUID()}),e=>e.code==='RECIPE_NOT_AVAILABLE');
    const key=randomUUID(),job=await economy.craft({identity:actor,recipeCode:recipe,batches:1,idempotencyKey:key});
    assert.equal(job.output.quantity,quantity);
    assert.ok(new Date(job.readyAt)>now);
    const restored=createCriminalEconomy({pool,transaction:transaction(pool),clock:()=>new Date(now),getPresence:i=>positions.get(i.characterId)});
    assert.equal((await restored.state({identity:actor})).activeProduction.productionId,job.productionId);
    assert.equal((await restored.craft({identity:actor,recipeCode:recipe,idempotencyKey:key})).idempotentReplay,true);
    await assert.rejects(restored.craft({identity:actor,recipeCode:recipe,idempotencyKey:randomUUID()}),e=>e.code==='PRODUCTION_PENDING');
    await assert.rejects(restored.finishProduction({identity:org==='mafia_branca'?leader:outsider,productionId:job.productionId,idempotencyKey:randomUUID()}),e=>e.code==='PRODUCTION_NOT_FOUND');
    jobs.push({actor,job,org,quantity,recipe});
   }
   const first=jobs.shift();
   await db.exec('SET ROLE hbrp_owner');
   // Reserve the exact output capacity. A simultaneous deposit cannot consume it.
   await db.query(`UPDATE hbrp.organization_warehouses SET capacity_units=(SELECT sum(quantity) FROM hbrp.organization_warehouse_items WHERE warehouse_id=organization_warehouses.id)+1
    WHERE organization_id=(SELECT id FROM hbrp.organizations WHERE code=$1)`,[first.org]);
   await db.exec('SET ROLE hbrp_app');await move(first.actor,first.org+'_warehouse');
   await assert.rejects(economy.deposit({identity:first.actor,itemCode:'pilot_scrap',quantity:1,idempotencyKey:randomUUID()}),e=>e.code==='WAREHOUSE_FULL');
   await move(first.actor,first.org+'_craft');
   await assert.rejects(economy.finishProduction({identity:first.actor,productionId:first.job.productionId,cancel:true,idempotencyKey:randomUUID()}),e=>e.code==='WAREHOUSE_FULL');
   assert.equal((await economy.state({identity:first.actor})).activeProduction.productionId,first.job.productionId);
   await db.exec('SET ROLE hbrp_owner');await db.query(`UPDATE hbrp.organization_warehouses SET capacity_units=5000 WHERE organization_id=(SELECT id FROM hbrp.organizations WHERE code=$1)`,[first.org]);await db.exec('SET ROLE hbrp_app');
   const cancel={identity:first.actor,productionId:first.job.productionId,cancel:true,idempotencyKey:randomUUID()};
   await economy.finishProduction(cancel);assert.equal((await economy.finishProduction(cancel)).idempotentReplay,true);
   const refunded=await economy.state({identity:first.actor});
   assert.equal(refunded.activeProduction,null);assert.equal(refunded.warehouse.find(i=>i.itemCode==='pilot_scrap').quantity,20);
   assert.equal(refunded.warehouse.find(i=>i.itemCode==='pilot_crystal').quantity,20);
   now=new Date(now.getTime()+61000);
   for(const {actor,job,quantity} of jobs){
    const complete={identity:actor,productionId:job.productionId,idempotencyKey:randomUUID()};
    const results=await Promise.all([economy.finishProduction(complete),economy.finishProduction(complete)]);
    assert.equal(results.filter(r=>r.idempotentReplay).length,1);
    assert.equal(results[0].output.quantity,quantity);
    await assert.rejects(economy.finishProduction({...complete,idempotencyKey:randomUUID()}),e=>e.code==='PRODUCTION_CLOSED');
   }
  });
 }finally{await db.close();}
});
