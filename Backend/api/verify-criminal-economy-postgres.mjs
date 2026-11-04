// Run as postgres on the HBRP host. Every fixture/change is rolled back.
import assert from 'node:assert/strict';
import {randomUUID,randomBytes,createHash} from 'node:crypto';
import {Readable} from 'node:stream';
import pg from '/srv/hbrp/api/node_modules/pg/lib/index.js';
import {createCriminalEconomy,createCriminalEconomyRouteHandler} from '/srv/hbrp/api/criminal-economy.mjs';
import {createApiHandler} from '/srv/hbrp/api/server.mjs';

const db=new pg.Client({host:'/var/run/postgresql',port:55432,database:'hbrp',user:'postgres'});
await db.connect();
const checks=[];
const fixtures=[];
let rolledBack=false;
let sequence=0;
const stack=[];
const pool={
 query:(sql,args)=>db.query(sql,args),
 async connect(){return {release(){},async query(sql,args){
  const command=sql.trim().toUpperCase();
  if(command==='BEGIN'){const key='economy_verify_'+(++sequence);stack.push(key);return db.query('SAVEPOINT '+key);}
  if(command==='COMMIT')return db.query('RELEASE SAVEPOINT '+stack.pop());
  if(command==='ROLLBACK'){const key=stack.pop();await db.query('ROLLBACK TO SAVEPOINT '+key);return db.query('RELEASE SAVEPOINT '+key);}
  return db.query(sql,args);
 }};}
};
const transaction=async fn=>{const client=await pool.connect();await client.query('BEGIN');
 try{const result=await fn(client);await client.query('COMMIT');return result;}
 catch(error){await client.query('ROLLBACK');throw error;}};
const hash=token=>createHash('sha256').update(token).digest();

async function fixture(index,level=0){
 const accountId=randomUUID(),characterId=randomUUID(),token=randomBytes(32).toString('base64url');
 const uid=String(900000000000000000n+BigInt(index));
 const suffix=randomBytes(4).toString('hex');
 await db.query(`INSERT INTO hbrp.accounts(id,uid,email,account_role,admin_level) OVERRIDING SYSTEM VALUE
  VALUES($1,$2,$3,$4,$5)`,[accountId,uid,'economy-'+suffix+'@example.invalid',level?'admin':'user',level]);
 await db.query(`INSERT INTO hbrp.characters(id,account_id,name,first_name,last_name,gender)
  VALUES($1,$2,$3,'Verifica',$4,'male')`,[characterId,accountId,'Verifica '+suffix,suffix]);
 await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[characterId]);
 await db.query(`INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z)
  SELECT $1,id,2489.3,12.37,1668.5 FROM hbrp.worlds WHERE code='centro_teste'`,[characterId]);
 await db.query('INSERT INTO hbrp.character_appearance(character_id) VALUES($1)',[characterId]);
 await db.query('INSERT INTO hbrp.inventories(character_id) VALUES($1)',[characterId]);
 await db.query(`INSERT INTO hbrp.beta_access(account_id,label,password_hash)
  VALUES($1,'Verificação transacional',$2)`,[accountId,'unusable-verification-hash-'.repeat(3)]);
 await db.query(`INSERT INTO hbrp.sessions(account_id,token_hash,expires_at,beta_access_version)
  VALUES($1,$2,now()+interval '5 minutes',1)`,[accountId,hash(token)]);
 const result={accountId,characterId,uid,token,accountRole:level?'admin':'user',adminLevel:level,world:'centro_teste',health:100};
 fixtures.push(result);return result;
}

try{
 await db.query('BEGIN');
 await db.query("SET LOCAL lock_timeout='5s'");
 await db.query("SET LOCAL statement_timeout='15s'");
 const manager=await fixture(1,1),player=await fixture(2),other=await fixture(3),limited=await fixture(4,4);
 await db.query('SET LOCAL ROLE hbrp_app');
 let now=new Date();
 const positions=new Map();
 const economy=createCriminalEconomy({pool,transaction,clock:()=>new Date(now),getPresence:who=>positions.get(who.characterId)||null});
 const handle=createCriminalEconomyRouteHandler(economy);
 const route=(who,path,body)=>handle({method:body===undefined?'GET':'POST',path:'/v1/criminal-economy'+path,identity:who,body});
 const view=await route(player,'');
 assert.ok(Array.isArray(view.activities)&&Array.isArray(view.inventory));
 assert.equal(view.organization,null);
 assert.ok(view.locations.length>=2);
 checks.push('persistent state/catalog/locations');
 await assert.rejects(route(player,'/admin/organizations'),error=>error.status===403);
 await assert.rejects(route(limited,'/admin/organizations'),error=>error.status===403);
 const organizations=await route(manager,'/admin/organizations');
 assert.equal(organizations.organizations.length,6);
 assert.ok(organizations.organizations.some(org=>org.code==='faccao_roxa'));
 await route(manager,'/admin/organization',{uid:player.uid,organizationCode:'faccao_roxa',rankCode:'lider'});
 assert.equal((await route(player,'')).organization.code,'faccao_roxa');
 checks.push('admin assignment and ordinary/rank4 denial');
 const activity=view.activities.find(item=>item.code==='urban_recycling');
 const location=view.locations.find(item=>item.activityCode===activity.code);
 const presence=point=>({x:point.x,y:point.y,z:point.z,world:point.world||'centro_teste',speed:0,driving:false,jumping:false});
 await assert.rejects(route(player,'/activities/start',{activityCode:activity.code,requestId:randomUUID()}));
 positions.set(player.characterId,presence({...location,x:location.x+1000}));
 await assert.rejects(route(player,'/activities/start',{activityCode:activity.code,requestId:randomUUID()}));
 positions.set(player.characterId,presence(location));
 const startKey=randomUUID();
 const started=await route(player,'/activities/start',{activityCode:activity.code,requestId:startKey});
 const startReplay=await route(player,'/activities/start',{activityCode:activity.code,requestId:startKey});
 assert.equal(startReplay.activityRunId,started.activityRunId);
 assert.ok(new Date(started.readyAt)>now);
 const complete={activityCode:activity.code,activityRunId:started.activityRunId,requestId:randomUUID()};
 await assert.rejects(route(player,'/activities/complete',complete));
 now=new Date(new Date(started.readyAt).getTime()+1000);
 const completed=await route(player,'/activities/complete',complete);
 assert.equal(completed.reward.quantity,3);
 assert.equal((await route(player,'/activities/complete',complete)).idempotentReplay,true);
 assert.equal((await route(other,'')).inventory.length,0);
 checks.push('server timer, proximity, replay and inventory isolation');
 const memberView=await route(player,'');
 const warehouse=memberView.locations.find(item=>item.kind==='warehouse'&&item.organizationCode==='faccao_roxa');
 assert.ok(warehouse);
 positions.set(player.characterId,presence(warehouse));
 const deposit={itemCode:'pilot_scrap',quantity:3,requestId:randomUUID()};
 const deposited=await route(player,'/warehouse/deposit',deposit);
 assert.equal((await route(player,'/warehouse/deposit',deposit)).idempotentReplay,true);
 assert.equal(deposited.inventoryQuantity,0);
 await assert.rejects(route(other,'/warehouse/withdraw',{itemCode:'pilot_scrap',quantity:1,requestId:randomUUID()}));
 // Seed only the transient fixture inventory, never a real player's inventory.
 await db.query('SET LOCAL ROLE hbrp_owner');
 await db.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
  SELECT id,'pilot_scrap',10,6 FROM hbrp.inventories WHERE character_id=$1`,[player.characterId]);
 await db.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
  SELECT id,'pilot_crystal',11,4 FROM hbrp.inventories WHERE character_id=$1`,[player.characterId]);
 await db.query('SET LOCAL ROLE hbrp_app');
 await route(player,'/warehouse/deposit',{itemCode:'pilot_scrap',quantity:6,requestId:randomUUID()});
 await route(player,'/warehouse/deposit',{itemCode:'pilot_crystal',quantity:4,requestId:randomUUID()});
 const crafting=memberView.locations.find(item=>item.kind==='craft'&&item.organizationCode==='faccao_roxa');
 assert.ok(crafting);positions.set(player.characterId,presence(crafting));
 const craft={recipeCode:'pilot_logistics_package',batches:1,requestId:randomUUID()};
 const job=await route(player,'/production/craft',craft);
 assert.equal(job.output.quantity,1);
 assert.equal((await route(player,'/production/craft',craft)).idempotentReplay,true);
 await assert.rejects(route(player,'/production/complete',{productionId:job.productionId,requestId:randomUUID()}),e=>e.code==='PRODUCTION_NOT_READY');
 now=new Date(job.readyAt);
 const finish={productionId:job.productionId,requestId:randomUUID()};
 assert.equal((await route(player,'/production/complete',finish)).output.quantity,1);
 assert.equal((await route(player,'/production/complete',finish)).idempotentReplay,true);
 const history=await route(player,'/history');
 assert.ok(history.activities.length>0&&history.operations.length>=4);
 checks.push('warehouse transfer, recipe, real history and atomic idempotency');
 assert.ok((await route(player,'')).recipes.some(r=>r.code==='rp_parafal'&&r.durationSeconds===60));
 await db.query('SET LOCAL ROLE hbrp_owner');
 await db.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
  SELECT id,'pilot_scrap',8,6 FROM hbrp.inventories WHERE character_id=$1`,[player.characterId]);
 await db.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
  SELECT id,'pilot_crystal',9,4 FROM hbrp.inventories WHERE character_id=$1`,[player.characterId]);
 await db.query('SET LOCAL ROLE hbrp_app');positions.set(player.characterId,presence(warehouse));
 await route(player,'/warehouse/deposit',{itemCode:'pilot_scrap',quantity:6,requestId:randomUUID()});
 await route(player,'/warehouse/deposit',{itemCode:'pilot_crystal',quantity:4,requestId:randomUUID()});
 positions.set(player.characterId,presence(crafting));
 await assert.rejects(route(player,'/production/craft',{recipeCode:'rp_ak47',batches:1,requestId:randomUUID()}),e=>e.code==='RECIPE_NOT_AVAILABLE');
 const beforeProduction=await route(player,'');
 const exclusive=await route(player,'/production/craft',{recipeCode:'rp_parafal',batches:1,requestId:randomUUID()});
 assert.equal((await route(player,'')).activeProduction.productionId,exclusive.productionId);
 const undo={productionId:exclusive.productionId,requestId:randomUUID()};
 await route(player,'/production/cancel',undo);assert.equal((await route(player,'/production/cancel',undo)).idempotentReplay,true);
 assert.deepEqual((await route(player,'')).warehouse,beforeProduction.warehouse);
 checks.push('six organizations, exclusive RP catalog, persisted timer and cancellation refund');
 const shop=(await route(player,'')).locations.find(p=>p.kind==='shop'&&p.organizationCode==='faccao_roxa');
 assert.ok(shop);positions.set(player.characterId,presence(shop));positions.set(other.characterId,presence(shop));
 const supply={shopCode:shop.code,itemCode:'pilot_logistics_crate',quantity:1,priceCents:1000,requestId:randomUUID()};
 await route(player,'/shops/supply',supply);
 assert.equal((await route(player,'/shops/supply',supply)).idempotentReplay,true);
 await assert.rejects(route(other,'/shops/supply',{...supply,requestId:randomUUID()}),e=>e.status===403);
 const wallet=(await db.query("INSERT INTO hbrp.wallets(character_id,currency) VALUES($1,'cash') RETURNING id",[other.characterId])).rows[0];
 await db.query("SELECT hbrp.change_balance($1,2000,$2,'verification_fixture')",[wallet.id,randomUUID()]);
 const buy={...supply,requestId:randomUUID()};
 await assert.rejects(route(other,'/shops/buy',{...buy,priceCents:100}),e=>e.code==='PRICE_CHANGED');
 await route(other,'/shops/buy',buy);assert.equal((await route(other,'/shops/buy',buy)).idempotentReplay,true);
 assert.equal((await route(other,'')).cashCents,1000);
 assert.equal((await route(other,'')).inventory.find(p=>p.itemCode==='pilot_logistics_crate').quantity,1);
 await route(player,'/shops/proceeds',{shopCode:shop.code,quantity:1000,requestId:randomUUID()});
 assert.equal((await route(player,'')).cashCents,1000);
 checks.push('player supplied store, stock/cash conservation, treasury, permissions and idempotent purchase');
 now=new Date(now.getTime()+60000);positions.set(player.characterId,presence(location));
 const abandoned=await route(player,'/activities/start',{activityCode:activity.code,requestId:randomUUID()});
 const cancel={activityRunId:abandoned.activityRunId,requestId:randomUUID()};
 await route(player,'/activities/cancel',cancel);assert.equal((await route(player,'/activities/cancel',cancel)).idempotentReplay,true);
 assert.equal((await route(player,'')).activeActivity,null);
 const expired=await route(player,'/activities/start',{activityCode:activity.code,requestId:randomUUID()});
 now=new Date(expired.expiresAt);assert.equal((await route(player,'')).activeActivity,null);
 await assert.rejects(route(player,'/activities/complete',{activityCode:activity.code,activityRunId:expired.activityRunId,requestId:randomUUID()}),e=>e.code==='ACTIVITY_CLOSED');
 checks.push('activity cancel and automatic expiry without reward');
 // Exercise installed server authentication and its mounted GET route without
 // binding ports or committing temporary sessions.
 const handler=createApiHandler(pool,{log(){},errorLog(){}});
 async function apiGet(token){
  const req=Readable.from([]);req.method='GET';req.url='/v1/criminal-economy';
  req.socket={remoteAddress:'127.0.0.1'};req.headers=token?{authorization:'Bearer '+token}:{};
  let status,reply;
  await handler(req,{writeHead(code){status=code;},end(data){reply=JSON.parse(data);}});
  return {status,reply};
 }
 assert.equal((await apiGet(null)).status,401);
 assert.equal((await apiGet(player.token)).status,200);
 checks.push('installed API route and beta Bearer authorization');
}finally{
 await db.query('ROLLBACK');rolledBack=true;
 const remaining=await db.query('SELECT count(*)::int AS n FROM hbrp.accounts WHERE id=ANY($1::uuid[])',[fixtures.map(row=>row.accountId)]);
 assert.equal(remaining.rows[0].n,0);
 await db.end();
 if(rolledBack)console.log(JSON.stringify({checks,temporaryAccountsRemaining:remaining.rows[0].n,fixtureTransactionRolledBack:true}));
}
