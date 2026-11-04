const UUID=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const CODE=/^[a-z][a-z0-9_]{2,47}$/;

export class CriminalEconomyError extends Error{
 constructor(status,code,message){super(message);this.name='CriminalEconomyError';this.status=status;this.code=code;}
}
const fail=(status,code,message)=>{throw new CriminalEconomyError(status,code,message);};
const requestId=value=>typeof value==='string'&&UUID.test(value)?value:
 fail(400,'INVALID_REQUEST_ID','requestId deve ser um UUID válido.');
const code=value=>typeof value==='string'&&CODE.test(value)?value:fail(400,'INVALID_CODE','Código inválido.');
const positive=(value,name,max=1000)=>Number.isInteger(value)&&value>=1&&value<=max?value:
 fail(400,'INVALID_QUANTITY',`${name} deve ser um inteiro entre 1 e ${max}.`);
const identityOf=value=>value?.accountId&&value?.characterId?value:
 fail(401,'CHARACTER_REQUIRED','Entre com um personagem antes de usar a economia.');
const asObject=value=>typeof value==='string'?JSON.parse(value):value;
const exactBody=(value,keys)=>{
 const actual=value&&typeof value==='object'&&!Array.isArray(value)?Object.keys(value):[];
 if(!value||typeof value!=='object'||Array.isArray(value)||actual.length!==keys.length||
  actual.some(key=>!keys.includes(key))||keys.some(key=>!Object.hasOwn(value,key)))
  fail(400,'INVALID_BODY','Dados inválidos.');
 return value;
};

async function lockRequest(client,id){
 await client.query('SELECT pg_advisory_xact_lock(hashtextextended($1::text,0))',[id]);
}

async function inventory(client,characterId){
 await client.query('INSERT INTO hbrp.inventories(character_id) VALUES($1) ON CONFLICT(character_id) DO NOTHING',[characterId]);
 const result=await client.query('SELECT id,capacity FROM hbrp.inventories WHERE character_id=$1 FOR UPDATE',[characterId]);
 return result.rows[0];
}

async function itemDefinition(client,itemCode){
 const result=await client.query(`SELECT code,max_stack FROM hbrp.item_definitions
  WHERE code=$1 AND enabled=true AND metadata @> '{"criminalEconomyPilot":true}'::jsonb`,[itemCode]);
 if(!result.rowCount)fail(404,'PILOT_ITEM_NOT_FOUND','Item não pertence ao piloto econômico.');
 return result.rows[0];
}

async function addInventoryItem(client,characterId,itemCode,quantity){
 const inv=await inventory(client,characterId),definition=await itemDefinition(client,itemCode);
 const rows=(await client.query(`SELECT id,slot,quantity FROM hbrp.inventory_items
  WHERE inventory_id=$1 AND item_code=$2 AND metadata='{}'::jsonb ORDER BY slot FOR UPDATE`,[inv.id,itemCode])).rows;
 let remaining=quantity;
 for(const row of rows){
  if(!remaining)break;
  const room=Number(definition.max_stack)-Number(row.quantity),delta=Math.min(room,remaining);
  if(delta>0){await client.query(`UPDATE hbrp.inventory_items SET quantity=quantity+$2,version=version+1
    WHERE id=$1`,[row.id,delta]);remaining-=delta;}
 }
 if(remaining){
  const occupied=new Set((await client.query('SELECT slot FROM hbrp.inventory_items WHERE inventory_id=$1',[inv.id])).rows.map(row=>Number(row.slot)));
  for(let slot=0;slot<Number(inv.capacity)&&remaining;slot++)if(!occupied.has(slot)){
   const delta=Math.min(Number(definition.max_stack),remaining);
   await client.query(`INSERT INTO hbrp.inventory_items(inventory_id,item_code,slot,quantity)
    VALUES($1,$2,$3,$4)`,[inv.id,itemCode,slot,delta]);remaining-=delta;
  }
 }
 if(remaining)fail(409,'INVENTORY_FULL','Inventário sem espaço suficiente para concluir a operação.');
 const total=await client.query(`SELECT COALESCE(sum(quantity),0)::int AS quantity FROM hbrp.inventory_items
  WHERE inventory_id=$1 AND item_code=$2 AND metadata='{}'::jsonb`,[inv.id,itemCode]);
 return Number(total.rows[0].quantity);
}

async function removeInventoryItem(client,characterId,itemCode,quantity){
 const inv=await inventory(client,characterId);await itemDefinition(client,itemCode);
 const rows=(await client.query(`SELECT id,quantity FROM hbrp.inventory_items
  WHERE inventory_id=$1 AND item_code=$2 AND metadata='{}'::jsonb ORDER BY slot DESC FOR UPDATE`,[inv.id,itemCode])).rows;
 if(rows.reduce((sum,row)=>sum+Number(row.quantity),0)<quantity)
  fail(409,'INSUFFICIENT_INVENTORY','Quantidade insuficiente no inventário.');
 let remaining=quantity;
 for(const row of rows){
  if(!remaining)break;
  const delta=Math.min(Number(row.quantity),remaining),after=Number(row.quantity)-delta;
  if(after)await client.query('UPDATE hbrp.inventory_items SET quantity=$2,version=version+1 WHERE id=$1',[row.id,after]);
  else await client.query('DELETE FROM hbrp.inventory_items WHERE id=$1',[row.id]);
  remaining-=delta;
 }
 const total=await client.query(`SELECT COALESCE(sum(quantity),0)::int AS quantity FROM hbrp.inventory_items
  WHERE inventory_id=$1 AND item_code=$2 AND metadata='{}'::jsonb`,[inv.id,itemCode]);
 return Number(total.rows[0].quantity);
}

async function membership(client,identity,lock=true){
 const suffix=lock?' FOR UPDATE OF w,om':'';
 const result=await client.query(`SELECT o.id AS organization_id,o.code AS organization_code,o.name AS organization_name,
  om.rank_code,w.id AS warehouse_id,w.capacity_units,p.can_deposit,p.can_withdraw,p.can_craft,p.max_operation_quantity
  FROM hbrp.organization_members om JOIN hbrp.organizations o ON o.id=om.organization_id
  JOIN hbrp.organization_warehouses w ON w.organization_id=o.id
  LEFT JOIN hbrp.organization_economy_rank_permissions p ON p.organization_id=o.id AND p.rank_code=om.rank_code
  WHERE om.character_id=$1${suffix}`,[identity.characterId]);
 if(!result.rowCount)fail(403,'PILOT_ORGANIZATION_REQUIRED','O personagem não pertence a uma organização do piloto.');
 if(result.rows[0].can_deposit===null)fail(403,'ECONOMY_RANK_UNCONFIGURED','O cargo da organização ainda não possui permissões econômicas.');
 return result.rows[0];
}

function allow(member,kind){
 const column={deposit:'can_deposit',withdraw:'can_withdraw',craft:'can_craft'}[kind];
 if(!member[column])fail(403,'ECONOMY_PERMISSION_DENIED','Seu cargo não permite esta operação.');
}

function operationLimit(member,quantity){
 if(quantity>Number(member.max_operation_quantity))
  fail(403,'ECONOMY_OPERATION_LIMIT','Quantidade acima do limite configurado para seu cargo.');
}

function permission(member,kind,quantity){allow(member,kind);operationLimit(member,quantity);}

async function warehouseTotal(client,warehouseId){
 const result=await client.query(`SELECT
  (SELECT COALESCE(sum(quantity),0) FROM hbrp.organization_warehouse_items WHERE warehouse_id=$1)+
  (SELECT COALESCE(sum(j.output_quantity),0) FROM hbrp.criminal_production_jobs j
   JOIN hbrp.organization_warehouses w ON w.organization_id=j.organization_id
   WHERE w.id=$1 AND j.status='pending') AS quantity`,[warehouseId]);
 return Number(result.rows[0].quantity);
}

async function changeWarehouseItem(client,warehouseId,itemCode,delta){
 await itemDefinition(client,itemCode);
 const current=await client.query(`SELECT quantity FROM hbrp.organization_warehouse_items
  WHERE warehouse_id=$1 AND item_code=$2 FOR UPDATE`,[warehouseId,itemCode]);
 const after=(current.rowCount?Number(current.rows[0].quantity):0)+delta;
 if(after<0)fail(409,'INSUFFICIENT_WAREHOUSE','Quantidade insuficiente no depósito da organização.');
 if(after>1000000)fail(409,'WAREHOUSE_ITEM_LIMIT','Limite do item no depósito excedido.');
 if(after===0)await client.query('DELETE FROM hbrp.organization_warehouse_items WHERE warehouse_id=$1 AND item_code=$2',[warehouseId,itemCode]);
 else await client.query(`INSERT INTO hbrp.organization_warehouse_items(warehouse_id,item_code,quantity)
  VALUES($1,$2,$3) ON CONFLICT(warehouse_id,item_code) DO UPDATE
  SET quantity=EXCLUDED.quantity,version=organization_warehouse_items.version+1,updated_at=now()`,[warehouseId,itemCode,after]);
 return after;
}

async function operationReplay(client,{id,identity,type,payload}){
 const result=await client.query(`SELECT result,character_id::text=$2 AS same_actor,
  operation_type=$3 AS same_type,request_payload=$4::jsonb AS same_payload
  FROM hbrp.organization_warehouse_operations WHERE request_id=$1`,[id,identity.characterId,type,JSON.stringify(payload)]);
 if(!result.rowCount)return null;
 const row=result.rows[0];
 if(!row.same_actor||!row.same_type||!row.same_payload)
  fail(409,'IDEMPOTENCY_CONFLICT','requestId já foi usado em outra operação.');
 return {...asObject(row.result),idempotentReplay:true};
}

async function recordOperation(client,{id,identity,member,type,payload,result,movements}){
 const inserted=await client.query(`INSERT INTO hbrp.organization_warehouse_operations
  (request_id,warehouse_id,organization_id,character_id,actor_account_id,operation_type,request_payload,result)
  VALUES($1,$2,$3,$4,$5,$6,$7::jsonb,$8::jsonb) RETURNING id`,
  [id,member.warehouse_id,member.organization_id,identity.characterId,identity.accountId,type,
   JSON.stringify(payload),JSON.stringify(result)]);
 for(const movement of movements)await client.query(`INSERT INTO hbrp.organization_warehouse_movements
  (operation_id,warehouse_id,item_code,quantity_delta,balance_after) VALUES($1,$2,$3,$4,$5)`,
  [inserted.rows[0].id,member.warehouse_id,movement.itemCode,movement.delta,movement.balanceAfter]);
 await client.query(`UPDATE hbrp.organization_warehouses SET version=version+1,updated_at=now() WHERE id=$1`,[member.warehouse_id]);
 await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
  VALUES($1,$2,'organization_warehouse',$3,$4::jsonb)`,[identity.accountId,`criminal_economy.${type}`,
   member.warehouse_id,JSON.stringify({requestId:id,organization:member.organization_code,...payload})]);
 return inserted.rows[0].id;
}

const locationView=row=>({code:row.code,kind:row.kind,activityCode:row.activity_code??null,
 organizationCode:row.organization_code??null,world:row.world,label:row.label,
 x:Number(row.x),y:Number(row.y),z:Number(row.z),radius:Number(row.radius)});
const sessionView=row=>({activityRunId:row.id,activityCode:row.activity_code,
 startedAt:new Date(row.started_at).toISOString(),readyAt:new Date(row.ready_at).toISOString(),
 expiresAt:new Date(row.expires_at).toISOString(),location:locationView(row)});
const locationColumns='l.code,l.kind,l.activity_code,o.code AS organization_code,l.world,l.label,l.x,l.y,l.z,l.radius';
const productionView=row=>({productionId:row.id,requestId:row.request_id,recipeCode:row.recipe_code,batches:Number(row.batches),
 output:{itemCode:row.output_item_code,quantity:Number(row.output_quantity)},
 startedAt:new Date(row.started_at).toISOString(),readyAt:new Date(row.ready_at).toISOString(),status:row.status});

export function createCriminalEconomy({pool,transaction,clock=()=>new Date(),getPresence}){
 if(!pool?.query||typeof transaction!=='function')throw new TypeError('pool e transaction são obrigatórios.');

 async function liveCharacter(client,identity){
  const row=(await client.query(`SELECT c.id,s.health FROM hbrp.characters c
   JOIN hbrp.accounts a ON a.id=c.account_id JOIN hbrp.character_stats s ON s.character_id=c.id
   WHERE c.id=$1 AND c.account_id=$2 AND c.deleted_at IS NULL AND a.status='active'
   AND (c.blocked_at IS NULL OR c.blocked_until<=now()) FOR UPDATE OF c,s`,[identity.characterId,identity.accountId])).rows[0];
  if(!row)fail(403,'CHARACTER_UNAVAILABLE','Personagem indisponível.');
  if(!(Number(identity.health)>0)||!(Number(row.health)>0))fail(409,'CHARACTER_DEAD','O personagem precisa estar vivo.');
 }

 async function nearby(client,identity,{kind,activityCode=null,organizationId=null,locationCode=null}){
  if(typeof getPresence!=='function')fail(503,'PRESENCE_UNAVAILABLE','Presença do personagem indisponível.');
  const presence=await getPresence(identity);
  if(!presence||!['x','y','z'].every(axis=>typeof presence[axis]==='number'&&Number.isFinite(presence[axis]))||typeof presence.world!=='string')
   fail(409,'PRESENCE_REQUIRED','Entre no mundo antes de usar este ponto.');
  if(presence.driving===true)fail(409,'ON_FOOT_REQUIRED','Saia do veículo para usar este ponto.');
  const rows=(await client.query(`SELECT ${locationColumns} FROM hbrp.criminal_economy_locations l
   LEFT JOIN hbrp.organizations o ON o.id=l.organization_id
   WHERE l.enabled=true AND l.kind=$1 AND ($2::text IS NULL OR l.activity_code=$2)
   AND ($3::uuid IS NULL OR l.organization_id=$3) AND ($4::text IS NULL OR l.code=$4)
   ORDER BY l.code`,[kind,activityCode,organizationId,locationCode])).rows;
  const location=rows.find(row=>row.world===presence.world&&
   Math.hypot(presence.x-Number(row.x),presence.y-Number(row.y),presence.z-Number(row.z))<=Number(row.radius));
  if(!location)fail(409,'LOCATION_REQUIRED','Aproxime-se do ponto correto no mundo.');
  return location;
 }

 async function cooldown(client,identity,activity,now){
  const latest=(await client.query(`SELECT completed_at FROM hbrp.criminal_activity_runs
   WHERE character_id=$1 AND activity_code=$2 ORDER BY completed_at DESC LIMIT 1`,[identity.characterId,activity.code])).rows[0];
  const readyAt=latest?new Date(latest.completed_at).getTime()+Number(activity.cooldown_seconds)*1000:0;
  if(now.getTime()<readyAt)fail(429,'ACTIVITY_COOLDOWN',`Aguarde ${Math.ceil((readyAt-now.getTime())/1000)} segundo(s).`);
 }

 async function state({identity}){
  identity=identityOf(identity);
  const activities=(await pool.query(`SELECT code,name,reward_item_code,reward_quantity,cooldown_seconds,duration_seconds
   FROM hbrp.criminal_activity_definitions WHERE enabled=true ORDER BY code`)).rows;
  const inventoryRows=(await pool.query(`SELECT ii.item_code,sum(ii.quantity)::int AS quantity
   FROM hbrp.inventories i JOIN hbrp.inventory_items ii ON ii.inventory_id=i.id
   JOIN hbrp.item_definitions d ON d.code=ii.item_code
   WHERE i.character_id=$1 AND d.metadata @> '{"criminalEconomyPilot":true}'::jsonb
   GROUP BY ii.item_code ORDER BY ii.item_code`,[identity.characterId])).rows;
  let member=null,warehouseItems=[],recipes=[];
  try{member=await membership(pool,identity,false);}catch(error){if(error.code!=='PILOT_ORGANIZATION_REQUIRED'&&error.code!=='ECONOMY_RANK_UNCONFIGURED')throw error;}
  if(member){
   warehouseItems=(await pool.query(`SELECT item_code,quantity::int FROM hbrp.organization_warehouse_items
    WHERE warehouse_id=$1 ORDER BY item_code`,[member.warehouse_id])).rows;
   recipes=(await pool.query(`SELECT r.code,r.name,r.output_item_code,r.output_quantity,r.duration_seconds,
    COALESCE(jsonb_agg(jsonb_build_object('itemCode',i.item_code,'quantity',i.quantity)
     ORDER BY i.item_code) FILTER(WHERE i.item_code IS NOT NULL),'[]'::jsonb) AS ingredients
    FROM hbrp.criminal_recipe_definitions r
    JOIN hbrp.criminal_recipe_organizations access ON access.recipe_code=r.code AND access.organization_id=$1
    LEFT JOIN hbrp.criminal_recipe_ingredients i ON i.recipe_code=r.code
    WHERE r.enabled=true GROUP BY r.code ORDER BY r.code`,[member.organization_id])).rows;
  }
  const locations=(await pool.query(`SELECT ${locationColumns} FROM hbrp.criminal_economy_locations l
   LEFT JOIN hbrp.organizations o ON o.id=l.organization_id WHERE l.enabled=true
   AND (l.kind IN ('activity','shop') OR l.organization_id=$1) ORDER BY l.code`,[member?.organization_id??null])).rows.map(locationView);
  const active=(await pool.query(`SELECT s.id,s.activity_code,s.started_at,s.ready_at,s.expires_at,${locationColumns}
   FROM hbrp.criminal_activity_sessions s JOIN hbrp.criminal_economy_locations l ON l.code=s.location_code
   LEFT JOIN hbrp.organizations o ON o.id=l.organization_id
   WHERE s.character_id=$1 AND s.completed_at IS NULL AND s.cancelled_at IS NULL AND s.expires_at>$2`,[identity.characterId,clock()])).rows[0];
  const shops=(await pool.query(`SELECT s.code,o.code AS organization,o.name,s.proceeds,
   COALESCE((SELECT jsonb_agg(jsonb_build_object('itemCode',p.item_code,'quantity',p.quantity,'priceCents',p.price_cents) ORDER BY p.item_code)
   FROM hbrp.criminal_shop_stock p WHERE p.shop_code=s.code),'[]'::jsonb) AS stock
   FROM hbrp.criminal_shops s JOIN hbrp.organizations o ON o.id=s.organization_id
   JOIN hbrp.criminal_economy_locations l ON l.code=s.code WHERE l.enabled=true ORDER BY s.code`)).rows
   .map(row=>({code:row.code,organization:row.organization,name:row.name,proceedsCents:member?.organization_code===row.organization?Number(row.proceeds):0,stock:asObject(row.stock)}));
  const cash=(await pool.query("SELECT balance FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[identity.characterId])).rows[0];
  const job=member?(await pool.query("SELECT * FROM hbrp.criminal_production_jobs WHERE organization_id=$1 AND status='pending'",[member.organization_id])).rows[0]:null;
  return {pilot:true,serverTime:clock().toISOString(),shops,cashCents:Number(cash?.balance??0),locations,activeProduction:job?productionView(job):null,activeActivity:active?sessionView(active):null,organization:member?{code:member.organization_code,name:member.organization_name,rank:member.rank_code,
   permissions:{deposit:member.can_deposit,withdraw:member.can_withdraw,craft:member.can_craft}}:null,
   activities:activities.map(row=>({...row,reward_quantity:Number(row.reward_quantity),cooldown_seconds:Number(row.cooldown_seconds),duration_seconds:Number(row.duration_seconds)})),
   inventory:inventoryRows.map(row=>({itemCode:row.item_code,quantity:Number(row.quantity)})),
   warehouse:warehouseItems.map(row=>({itemCode:row.item_code,quantity:Number(row.quantity)})),
   recipes:recipes.map(row=>({code:row.code,name:row.name,outputItemCode:row.output_item_code,
    outputQuantity:Number(row.output_quantity),durationSeconds:Number(row.duration_seconds),ingredients:asObject(row.ingredients)}))};
 }

 async function startActivity({identity,activityCode,idempotencyKey}){
  identity=identityOf(identity);activityCode=code(activityCode);idempotencyKey=requestId(idempotencyKey);
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);
   await liveCharacter(client,identity);
   const replay=(await client.query(`SELECT s.*,${locationColumns} FROM hbrp.criminal_activity_sessions s
    JOIN hbrp.criminal_economy_locations l ON l.code=s.location_code
    LEFT JOIN hbrp.organizations o ON o.id=l.organization_id WHERE s.request_id=$1`,[idempotencyKey])).rows[0];
   if(replay){
    if(replay.character_id!==identity.characterId||replay.activity_code!==activityCode)
     fail(409,'IDEMPOTENCY_CONFLICT','requestId já foi usado em outra atividade.');
    if(replay.cancelled_at||clock().getTime()>=new Date(replay.expires_at).getTime())fail(409,'ACTIVITY_CLOSED','Atividade cancelada ou expirada.');
    await nearby(client,identity,{kind:'activity',activityCode,locationCode:replay.location_code});
    return {...sessionView(replay),idempotentReplay:true};
   }
   const definition=await client.query(`SELECT code,cooldown_seconds,duration_seconds
    FROM hbrp.criminal_activity_definitions WHERE code=$1 AND enabled=true`,[activityCode]);
   if(!definition.rowCount)fail(404,'ACTIVITY_NOT_FOUND','Atividade do piloto não encontrada.');
   const location=await nearby(client,identity,{kind:'activity',activityCode});
   await client.query(`UPDATE hbrp.criminal_activity_sessions SET cancelled_at=$2
    WHERE character_id=$1 AND completed_at IS NULL AND cancelled_at IS NULL AND expires_at<=$2`,[identity.characterId,clock()]);
   const active=await client.query(`SELECT id FROM hbrp.criminal_activity_sessions WHERE character_id=$1 AND completed_at IS NULL AND cancelled_at IS NULL`,[identity.characterId]);
   if(active.rowCount)fail(409,'ACTIVITY_ALREADY_ACTIVE','Conclua sua atividade em andamento.');
   const startedAt=clock(),activity=definition.rows[0];await cooldown(client,identity,activity,startedAt);
   const readyAt=new Date(startedAt.getTime()+Number(activity.duration_seconds)*1000);
   const session=(await client.query(`INSERT INTO hbrp.criminal_activity_sessions
    (request_id,character_id,activity_code,location_code,started_at,ready_at,expires_at)
    VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id,activity_code,started_at,ready_at,expires_at`,
    [idempotencyKey,identity.characterId,activityCode,location.code,startedAt,readyAt,new Date(readyAt.getTime()+900000)])).rows[0];
   await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
    VALUES($1,'criminal_economy.activity_start','character',$2,$3::jsonb)`,
    [identity.accountId,identity.characterId,JSON.stringify({requestId:idempotencyKey,activityRunId:session.id,activityCode,location:location.code})]);
   return sessionView({...location,...session});
  });
 }

 async function completeActivity({identity,activityCode,activityRunId,idempotencyKey}){
  identity=identityOf(identity);activityCode=code(activityCode);activityRunId=requestId(activityRunId);idempotencyKey=requestId(idempotencyKey);
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);await liveCharacter(client,identity);
   const replay=(await client.query(`SELECT character_id::text=$2 AS same_actor,activity_code=$3 AS same_activity,
    activity_session_id::text=$4 AS same_session,result FROM hbrp.criminal_activity_runs WHERE request_id=$1`,
    [idempotencyKey,identity.characterId,activityCode,activityRunId])).rows[0];
   if(replay&&(!replay.same_actor||!replay.same_activity||!replay.same_session))
    fail(409,'IDEMPOTENCY_CONFLICT','requestId já foi usado em outra atividade.');
   const session=(await client.query(`SELECT * FROM hbrp.criminal_activity_sessions
    WHERE id=$1 AND character_id=$2 AND activity_code=$3 FOR UPDATE`,[activityRunId,identity.characterId,activityCode])).rows[0];
   if(!session)fail(404,'ACTIVITY_RUN_NOT_FOUND','Atividade iniciada não encontrada.');
   await nearby(client,identity,{kind:'activity',activityCode,locationCode:session.location_code});
   if(replay)return {...asObject(replay.result),idempotentReplay:true};
   if(session.completed_at)fail(409,'ACTIVITY_ALREADY_COMPLETED','Esta atividade já foi concluída.');
   if(session.cancelled_at||clock().getTime()>=new Date(session.expires_at).getTime())fail(409,'ACTIVITY_CLOSED','Atividade cancelada ou expirada. Inicie outra coleta.');
   const completedAt=clock();
   if(completedAt.getTime()<new Date(session.ready_at).getTime())fail(409,'ACTIVITY_NOT_READY','A atividade ainda está em andamento.');
   const activity=(await client.query(`SELECT code,reward_item_code,reward_quantity,cooldown_seconds
    FROM hbrp.criminal_activity_definitions WHERE code=$1 AND enabled=true`,[activityCode])).rows[0];
   if(!activity)fail(404,'ACTIVITY_NOT_FOUND','Atividade não encontrada.');
   await cooldown(client,identity,activity,completedAt);
   const inventoryQuantity=await addInventoryItem(client,identity.characterId,activity.reward_item_code,Number(activity.reward_quantity));
   const result={activityCode,activityRunId,reward:{itemCode:activity.reward_item_code,quantity:Number(activity.reward_quantity)},inventoryQuantity};
   await client.query(`INSERT INTO hbrp.criminal_activity_runs
    (request_id,character_id,activity_code,reward_item_code,reward_quantity,result,completed_at,activity_session_id)
    VALUES($1,$2,$3,$4,$5,$6::jsonb,$7,$8)`,[idempotencyKey,identity.characterId,activityCode,
    activity.reward_item_code,activity.reward_quantity,JSON.stringify(result),completedAt,activityRunId]);
   await client.query('UPDATE hbrp.criminal_activity_sessions SET completed_at=$2 WHERE id=$1',[activityRunId,completedAt]);
   await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
    VALUES($1,'criminal_economy.activity_complete','character',$2,$3::jsonb)`,
    [identity.accountId,identity.characterId,JSON.stringify({requestId:idempotencyKey,activityRunId,activityCode,reward:result.reward})]);
   return result;
  });
 }

 async function transfer({identity,type,itemCode,quantity,idempotencyKey}){
  identity=identityOf(identity);itemCode=code(itemCode);quantity=positive(quantity,'quantity');idempotencyKey=requestId(idempotencyKey);
  if(type!=='deposit'&&type!=='withdraw')throw new TypeError('type inválido');
  const payload={itemCode,quantity};
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);
   await liveCharacter(client,identity);
   const member=await membership(client,identity,true);permission(member,type,quantity);
   await nearby(client,identity,{kind:'warehouse',organizationId:member.organization_id});
   const replay=await operationReplay(client,{id:idempotencyKey,identity,type,payload});if(replay)return replay;
   await itemDefinition(client,itemCode);
   let inventoryQuantity,warehouseQuantity;
   if(type==='deposit'){
    if(await warehouseTotal(client,member.warehouse_id)+quantity>Number(member.capacity_units))
     fail(409,'WAREHOUSE_FULL','Depósito da organização sem espaço suficiente.');
    inventoryQuantity=await removeInventoryItem(client,identity.characterId,itemCode,quantity);
    warehouseQuantity=await changeWarehouseItem(client,member.warehouse_id,itemCode,quantity);
   }else{
    warehouseQuantity=await changeWarehouseItem(client,member.warehouse_id,itemCode,-quantity);
    inventoryQuantity=await addInventoryItem(client,identity.characterId,itemCode,quantity);
   }
   const result={operation:type,itemCode,quantity,inventoryQuantity,warehouseQuantity,organization:member.organization_code};
   await recordOperation(client,{id:idempotencyKey,identity,member,type,payload,result,movements:[{itemCode,
    delta:type==='deposit'?quantity:-quantity,balanceAfter:warehouseQuantity}]});
   return result;
  });
 }

 async function craft({identity,recipeCode,batches=1,idempotencyKey}){
  identity=identityOf(identity);recipeCode=code(recipeCode);batches=positive(batches,'batches',1000);idempotencyKey=requestId(idempotencyKey);
  const payload={recipeCode,batches};
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);
   await liveCharacter(client,identity);
   const member=await membership(client,identity,true);allow(member,'craft');
   await nearby(client,identity,{kind:'craft',organizationId:member.organization_id});
   const replay=await commerceReplay(client,identity,idempotencyKey,'production_start',payload);if(replay)return replay;
   if((await client.query("SELECT id FROM hbrp.criminal_production_jobs WHERE organization_id=$1 AND status='pending'",[member.organization_id])).rowCount)
    fail(409,'PRODUCTION_PENDING','Conclua ou cancele a produção em andamento da organização.');
   const recipeResult=await client.query(`SELECT r.code,r.name,r.output_item_code,r.output_quantity,r.duration_seconds
    FROM hbrp.criminal_recipe_definitions r JOIN hbrp.criminal_recipe_organizations access
    ON access.recipe_code=r.code AND access.organization_id=$2
    WHERE r.code=$1 AND r.enabled=true`,[recipeCode,member.organization_id]);
   if(!recipeResult.rowCount)fail(404,'RECIPE_NOT_AVAILABLE','Receita não disponível para esta organização.');
   const recipe=recipeResult.rows[0],ingredients=(await client.query(`SELECT item_code,quantity
    FROM hbrp.criminal_recipe_ingredients WHERE recipe_code=$1 ORDER BY item_code`,[recipeCode])).rows;
   const consumed=ingredients.reduce((sum,row)=>sum+Number(row.quantity)*batches,0),produced=Number(recipe.output_quantity)*batches;
   operationLimit(member,consumed+produced);
   if(await warehouseTotal(client,member.warehouse_id)-consumed+produced>Number(member.capacity_units))
    fail(409,'WAREHOUSE_FULL','Depósito sem espaço para o resultado da produção.');
   const movementDraft=[];
   for(const ingredient of ingredients){
    const amount=Number(ingredient.quantity)*batches;
    const after=await changeWarehouseItem(client,member.warehouse_id,ingredient.item_code,-amount);
    movementDraft.push({itemCode:ingredient.item_code,delta:-amount,balanceAfter:after});
   }
   const location=await nearby(client,identity,{kind:'craft',organizationId:member.organization_id});
   const started=clock(),ready=new Date(started.getTime()+Number(recipe.duration_seconds)*batches*1000);
   const snapshot=ingredients.map(i=>({itemCode:i.item_code,quantity:Number(i.quantity)*batches}));
   const job=(await client.query(`INSERT INTO hbrp.criminal_production_jobs
    (request_id,organization_id,character_id,recipe_code,batches,location_code,output_item_code,output_quantity,ingredients,started_at,ready_at)
    VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10,$11) RETURNING *`,
    [idempotencyKey,member.organization_id,identity.characterId,recipeCode,batches,location.code,recipe.output_item_code,produced,JSON.stringify(snapshot),started,ready])).rows[0];
   const result={operation:'production_start',...productionView(job),organization:member.organization_code};
   await recordOperation(client,{id:idempotencyKey,identity,member,type:'production_start',payload,result,movements:movementDraft});
   return commerceRecord(client,identity,idempotencyKey,'production_start',payload,result,member.organization_id);
  });
 }

 async function finishProduction({identity,productionId,cancel=false,idempotencyKey}){
  identity=identityOf(identity);productionId=requestId(productionId);idempotencyKey=requestId(idempotencyKey);
  const operation=cancel?'production_cancel':'production_complete',payload={productionId};
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);await liveCharacter(client,identity);
   const member=await membership(client,identity,true);allow(member,'craft');
   const replay=await commerceReplay(client,identity,idempotencyKey,operation,payload);if(replay)return replay;
   const job=(await client.query('SELECT * FROM hbrp.criminal_production_jobs WHERE id=$1 AND organization_id=$2 FOR UPDATE',[productionId,member.organization_id])).rows[0];
   if(!job)fail(404,'PRODUCTION_NOT_FOUND','Produção não encontrada nesta organização.');
   if(job.status!=='pending')fail(409,'PRODUCTION_CLOSED','Produção já concluída ou cancelada.');
   await nearby(client,identity,{kind:'craft',organizationId:member.organization_id,locationCode:job.location_code});
   const snapshot=asObject(job.ingredients);
   operationLimit(member,snapshot.reduce((sum,i)=>sum+i.quantity,0)+Number(job.output_quantity));
   if(!cancel&&clock().getTime()<new Date(job.ready_at).getTime())fail(409,'PRODUCTION_NOT_READY','Produção em andamento. Aguarde o tempo restante.');
   const additions=cancel?snapshot:[{itemCode:job.output_item_code,quantity:Number(job.output_quantity)}];
   const amount=additions.reduce((sum,i)=>sum+i.quantity,0);
   // Pending output has already reserved capacity. Cancellation must also fit refunded inputs.
   if(await warehouseTotal(client,member.warehouse_id)-Number(job.output_quantity)+amount>Number(member.capacity_units))
    fail(409,'WAREHOUSE_FULL',cancel?'Libere espaço no depósito para devolver os materiais.':'Depósito sem espaço.');
   const movements=[];
   for(const item of additions){
    const after=await changeWarehouseItem(client,member.warehouse_id,item.itemCode,item.quantity);
    movements.push({itemCode:item.itemCode,delta:item.quantity,balanceAfter:after});
   }
   const result={operation,productionId,recipeCode:job.recipe_code,batches:Number(job.batches),organization:member.organization_code,
    ...(cancel?{returned:snapshot}:{output:{itemCode:job.output_item_code,quantity:Number(job.output_quantity)}})};
   const operationId=await recordOperation(client,{id:idempotencyKey,identity,member,type:cancel?'production_cancel':'craft',payload,result,movements});
   if(!cancel)await client.query(`INSERT INTO hbrp.criminal_production_runs
    (operation_id,organization_id,character_id,recipe_code,batches,output_item_code,output_quantity)
    VALUES($1,$2,$3,$4,$5,$6,$7)`,[operationId,member.organization_id,identity.characterId,job.recipe_code,job.batches,job.output_item_code,job.output_quantity]);
   await client.query('UPDATE hbrp.criminal_production_jobs SET status=$2,closed_at=$3 WHERE id=$1',[productionId,cancel?'cancelled':'completed',clock()]);
   return commerceRecord(client,identity,idempotencyKey,operation,payload,result,member.organization_id);
  });
 }

 async function commerceReplay(client,identity,id,operation,payload){
  const row=(await client.query(`SELECT *,character_id::text=$2 AS same_actor,
   payload=$3::jsonb AS same_payload FROM hbrp.criminal_commerce_operations WHERE request_id=$1`,[id,identity.characterId,JSON.stringify(payload)])).rows[0];
  if(!row)return null;
  if(!row.same_actor||!row.same_payload||row.operation!==operation)fail(409,'IDEMPOTENCY_CONFLICT','Solicitação já utilizada.');
  return {...asObject(row.result),idempotentReplay:true};
 }
 async function commerceRecord(client,identity,id,operation,payload,result,organizationId=null){
  await client.query(`INSERT INTO hbrp.criminal_commerce_operations(request_id,character_id,organization_id,operation,payload,result)
   VALUES($1,$2,$3,$4,$5::jsonb,$6::jsonb)`,[id,identity.characterId,organizationId,operation,JSON.stringify(payload),JSON.stringify(result)]);
  await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
   VALUES($1,$2,'character',$3,$4::jsonb)`,[identity.accountId,'criminal_economy.'+operation,identity.characterId,JSON.stringify({requestId:id,...payload,...result})]);
  return result;
 }
 async function cancelActivity({identity,activityRunId,idempotencyKey}){
  identity=identityOf(identity);activityRunId=requestId(activityRunId);idempotencyKey=requestId(idempotencyKey);
  const payload={activityRunId};
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);
   // Cancellation remains available when dead or away from the collection point.
   const replay=await commerceReplay(client,identity,idempotencyKey,'cancel',payload);if(replay)return replay;
   const row=(await client.query(`SELECT * FROM hbrp.criminal_activity_sessions WHERE id=$1 AND character_id=$2 FOR UPDATE`,[activityRunId,identity.characterId])).rows[0];
   if(!row)fail(404,'ACTIVITY_RUN_NOT_FOUND','Atividade não encontrada.');
   if(row.completed_at)fail(409,'ACTIVITY_ALREADY_COMPLETED','Atividade já concluída.');
   if(!row.cancelled_at)await client.query('UPDATE hbrp.criminal_activity_sessions SET cancelled_at=$2 WHERE id=$1',[activityRunId,clock()]);
   return commerceRecord(client,identity,idempotencyKey,'cancel',payload,{operation:'cancel',activityRunId});
  });
 }
 async function shopOperation({identity,operation,shopCode,itemCode,quantity,priceCents,idempotencyKey}){
  identity=identityOf(identity);shopCode=code(shopCode);idempotencyKey=requestId(idempotencyKey);
  if(operation!=='price')quantity=positive(quantity,'quantity',operation==='proceeds'?1000000000:1000);
  const payload=operation==='price'?{shopCode}:{shopCode,quantity};
  if(operation!=='proceeds'){
   itemCode=code(itemCode);payload.itemCode=itemCode;
   if(['supply','buy','price'].includes(operation)){priceCents=positive(priceCents,'priceCents',100000000);if(priceCents<100)fail(400,'INVALID_PRICE','Preço mínimo: R$ 1.');payload.priceCents=priceCents;}
  }
  return transaction(async client=>{
   await lockRequest(client,idempotencyKey);await liveCharacter(client,identity);
   const replay=await commerceReplay(client,identity,idempotencyKey,operation,payload);if(replay)return replay;
   let member=null;
   if(operation!=='buy'){member=await membership(client,identity,true);permission(member,'withdraw',['proceeds','price'].includes(operation)?1:quantity);}
   const shop=(await client.query(`SELECT s.* FROM hbrp.criminal_shops s JOIN hbrp.criminal_economy_locations l ON l.code=s.code
    WHERE s.code=$1 AND l.enabled=true FOR UPDATE OF s`,[shopCode])).rows[0];
   if(!shop)fail(404,'SHOP_NOT_FOUND','Loja não encontrada.');
   if(member&&member.organization_id!==shop.organization_id)fail(403,'SHOP_PERMISSION_DENIED','A loja pertence a outra organização.');
   await nearby(client,identity,{kind:'shop',locationCode:shopCode,organizationId:shop.organization_id});
   if(operation==='proceeds'){
    if(!['lider','sublider'].includes(member.rank_code))fail(403,'SHOP_PERMISSION_DENIED','Somente líder ou sublíder pode recolher o caixa.');
    if(Number(shop.proceeds)<quantity)fail(409,'INSUFFICIENT_PROCEEDS','Caixa insuficiente.');
    const wallet=await cashWallet(client,identity);
    if(Number(wallet.balance)+quantity>9000000000000000)fail(409,'WALLET_LIMIT','Limite da carteira.');
    await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,quantity,idempotencyKey,'criminal_shop_proceeds']);
    await client.query('UPDATE hbrp.criminal_shops SET proceeds=proceeds-$2 WHERE code=$1',[shopCode,quantity]);
    return commerceRecord(client,identity,idempotencyKey,operation,payload,{operation,shopCode,amountCents:quantity},shop.organization_id);
   }
   await itemDefinition(client,itemCode);
   const definition=(await client.query("SELECT category FROM hbrp.item_definitions WHERE code=$1",[itemCode])).rows[0];
   if(definition.category!=='pilot_product')fail(400,'PRODUCT_REQUIRED','A loja aceita produtos fabricados, não matérias-primas.');
   const stock=(await client.query('SELECT * FROM hbrp.criminal_shop_stock WHERE shop_code=$1 AND item_code=$2 FOR UPDATE',[shopCode,itemCode])).rows[0];
   const existing=Number(stock?.quantity??0);
   let result={operation,shopCode,itemCode,...(operation==='price'?{priceCents}:{quantity})};
   if(operation==='buy'){
    if(existing<quantity)fail(409,'SHOP_OUT_OF_STOCK','Estoque insuficiente.');
    if(Number(stock.price_cents)!==priceCents)fail(409,'PRICE_CHANGED','Preço alterado. Atualize a loja antes de comprar.');
    const total=quantity*priceCents;
    if(Number(shop.proceeds)+total>1000000000000)fail(409,'SHOP_CASH_FULL','Caixa da loja atingiu o limite.');
    const wallet=await cashWallet(client,identity);
    if(Number(wallet.balance)<total)fail(409,'INSUFFICIENT_FUNDS','Dinheiro insuficiente.');
    await addInventoryItem(client,identity.characterId,itemCode,quantity);
    await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,-total,idempotencyKey,'criminal_shop_purchase']);
    await client.query('UPDATE hbrp.criminal_shop_stock SET quantity=quantity-$3 WHERE shop_code=$1 AND item_code=$2',[shopCode,itemCode,quantity]);
    await client.query('UPDATE hbrp.criminal_shops SET proceeds=proceeds+$2 WHERE code=$1',[shopCode,total]);
    result.totalCents=total;
   }else if(operation==='price'){
    if(!stock)fail(404,'SHOP_PRODUCT_NOT_FOUND','Abasteça o produto antes de definir o preço.');
    await client.query('UPDATE hbrp.criminal_shop_stock SET price_cents=$3 WHERE shop_code=$1 AND item_code=$2',[shopCode,itemCode,priceCents]);
   }else if(operation==='supply'){
    if(existing+quantity>1000000)fail(409,'SHOP_STOCK_LIMIT','Limite de estoque.');
    await changeWarehouseItem(client,member.warehouse_id,itemCode,-quantity);
    await client.query(`INSERT INTO hbrp.criminal_shop_stock(shop_code,item_code,quantity,price_cents) VALUES($1,$2,$3,$4)
     ON CONFLICT(shop_code,item_code) DO UPDATE SET quantity=criminal_shop_stock.quantity+EXCLUDED.quantity,price_cents=EXCLUDED.price_cents`,[shopCode,itemCode,quantity,priceCents]);
   }else if(operation==='recall'){
    if(existing<quantity)fail(409,'SHOP_OUT_OF_STOCK','Estoque insuficiente.');
    if(await warehouseTotal(client,member.warehouse_id)+quantity>Number(member.capacity_units))fail(409,'WAREHOUSE_FULL','Depósito cheio.');
    await changeWarehouseItem(client,member.warehouse_id,itemCode,quantity);
    await client.query('UPDATE hbrp.criminal_shop_stock SET quantity=quantity-$3 WHERE shop_code=$1 AND item_code=$2',[shopCode,itemCode,quantity]);
   }else throw new TypeError('Operação da loja inválida');
   return commerceRecord(client,identity,idempotencyKey,operation,payload,result,shop.organization_id);
  });
 }
 async function cashWallet(client,identity){
  await client.query("INSERT INTO hbrp.wallets(character_id,currency) VALUES($1,'cash') ON CONFLICT DO NOTHING",[identity.characterId]);
  return (await client.query("SELECT id,balance FROM hbrp.wallets WHERE character_id=$1 AND currency='cash' FOR UPDATE",[identity.characterId])).rows[0];
 }

 async function history({identity}){
  identity=identityOf(identity);
  const activities=(await pool.query(`SELECT activity_session_id,activity_code,request_id,result,completed_at
   FROM hbrp.criminal_activity_runs WHERE character_id=$1 ORDER BY completed_at DESC,id DESC LIMIT 50`,[identity.characterId])).rows
   .map(row=>({...asObject(row.result),activityRunId:row.activity_session_id,activityCode:row.activity_code,
    requestId:row.request_id,completedAt:new Date(row.completed_at).toISOString()}));
  const operations=(await pool.query(`SELECT p.operation_type,p.request_id,p.result,p.created_at
   FROM hbrp.organization_warehouse_operations p WHERE p.organization_id IN
   (SELECT organization_id FROM hbrp.organization_members WHERE character_id=$1)
   ORDER BY p.created_at DESC,p.id DESC LIMIT 50`,[identity.characterId])).rows
   .map(row=>({...asObject(row.result),operation:row.operation_type,type:row.operation_type,
    requestId:row.request_id,createdAt:new Date(row.created_at).toISOString()}));
  const commerce=(await pool.query(`SELECT request_id,operation,result,created_at FROM hbrp.criminal_commerce_operations
   WHERE character_id=$1 OR organization_id IN (SELECT organization_id FROM hbrp.organization_members WHERE character_id=$1)
   ORDER BY created_at DESC,request_id DESC LIMIT 50`,[identity.characterId])).rows
   .map(row=>({...asObject(row.result),operation:row.operation,requestId:row.request_id,createdAt:new Date(row.created_at).toISOString()}));
  return {activities,operations,commerce};
 }

 async function adminActor(client,identity){
  const row=(await client.query(`SELECT id,account_role,admin_level FROM hbrp.accounts
   WHERE id=$1 AND status='active'`,[identity.accountId])).rows[0];
  if(row?.account_role!=='admin'||![1,3].includes(Number(row.admin_level)))
   fail(403,'ECONOMY_ADMIN_REQUIRED','Esta ação exige administrador nível 1 ou 3.');
  return row;
 }

 async function adminOrganizations({identity}){
  identity=identityOf(identity);await adminActor(pool,identity);
  const organizations=(await pool.query(`SELECT o.id,o.code,o.name FROM hbrp.organizations o
   JOIN hbrp.organization_warehouses w ON w.organization_id=o.id ORDER BY o.code`)).rows;
  const output=[];
  for(const organization of organizations){
   const ranks=(await pool.query(`SELECT rank_code FROM hbrp.organization_economy_rank_permissions
    WHERE organization_id=$1 ORDER BY max_operation_quantity DESC,rank_code`,[organization.id])).rows.map(row=>row.rank_code);
   const members=(await pool.query(`SELECT a.uid::text AS uid,coalesce(c.first_name,split_part(c.name,' ',1)) AS first_name,m.rank_code
    FROM hbrp.organization_members m JOIN hbrp.characters c ON c.id=m.character_id
    JOIN hbrp.accounts a ON a.id=c.account_id WHERE m.organization_id=$1 AND c.deleted_at IS NULL
    ORDER BY a.uid`,[organization.id])).rows.map(row=>({uid:row.uid,firstName:row.first_name,rank:row.rank_code}));
   output.push({code:organization.code,name:organization.name,ranks,members});
  }
  return {organizations:output};
 }

 async function setOrganization({identity,uid,organizationCode,rankCode}){
  identity=identityOf(identity);
  if(typeof uid!=='string'||!/^[1-9][0-9]{0,18}$/.test(uid))fail(400,'INVALID_UID','ID de jogador inválido.');
  organizationCode=code(organizationCode);rankCode=code(rankCode);
  if((organizationCode==='none')!==(rankCode==='none'))fail(400,'INVALID_ORGANIZATION_RANK','Escolha organização e cargo válidos.');
  return transaction(async client=>{
   // Lock permission owners in stable order, then reread both roles after waiting.
   await adminActor(client,identity);
   const target=(await client.query('SELECT id FROM hbrp.accounts WHERE uid::text=$1',[uid])).rows[0];
   if(!target)fail(404,'PLAYER_NOT_FOUND','Jogador não encontrado.');
   await client.query('SELECT id FROM hbrp.accounts WHERE id IN ($1,$2) ORDER BY id FOR NO KEY UPDATE',[identity.accountId,target.id]);
   const actor=await adminActor(client,identity);
   const fresh=(await client.query('SELECT account_role,admin_level FROM hbrp.accounts WHERE id=$1',[target.id])).rows[0];
   if(Number(actor.admin_level)!==1&&(Number(fresh.admin_level)===1||Number(fresh.admin_level)>=Number(actor.admin_level)))
    fail(403,'ADMIN_HIERARCHY','Só é permitido alterar jogadores e administradores de nível inferior.');
   await client.query('SELECT id FROM hbrp.sessions WHERE account_id=$1 ORDER BY id FOR UPDATE',[target.id]);
   const characters=(await client.query(`SELECT id FROM hbrp.characters
    WHERE account_id=$1 AND deleted_at IS NULL ORDER BY created_at,id FOR UPDATE`,[target.id])).rows;
   if(characters.length!==1)fail(409,'PLAYER_CHARACTER_REQUIRED','O jogador precisa ter um personagem ativo.');
   const character=characters[0];
   let organization=null;
   if(organizationCode!=='none'){
    organization=(await client.query(`SELECT o.id,o.code,o.name FROM hbrp.organizations o
     JOIN hbrp.organization_warehouses w ON w.organization_id=o.id
     JOIN hbrp.organization_economy_rank_permissions p ON p.organization_id=o.id AND p.rank_code=$2
     WHERE o.code=$1`,[organizationCode,rankCode])).rows[0];
    if(!organization)fail(400,'INVALID_ORGANIZATION_RANK','Organização ou cargo indisponível.');
   }
   const before=(await client.query(`SELECT o.code,m.rank_code FROM hbrp.organization_members m
    JOIN hbrp.organizations o ON o.id=m.organization_id WHERE m.character_id=$1 FOR UPDATE OF m`,[character.id])).rows[0]??null;
   await client.query('DELETE FROM hbrp.organization_members WHERE character_id=$1',[character.id]);
   if(organization)await client.query(`INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code)
    VALUES($1,$2,$3)`,[organization.id,character.id,rankCode]);
   await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
    VALUES($1,'criminal_economy.organization_set','character',$2,$3::jsonb)`,
    [identity.accountId,character.id,JSON.stringify({uid,before,organizationCode,rankCode})]);
   return {uid,organization:organization?{code:organization.code,name:organization.name,rank:rankCode}:null};
  });
 }

 return {state,startActivity,completeActivity,cancelActivity,shopOperation,finishProduction,history,adminOrganizations,setOrganization,
  deposit:input=>transfer({...input,type:'deposit'}),
  withdraw:input=>transfer({...input,type:'withdraw'}),craft};
}

// Isolated adapter for later server.mjs mounting. Unknown routes return null.
export function createCriminalEconomyRouteHandler(economy){
 return async function handle({method,path,identity,body={}}){
  if(method==='GET'&&path==='/v1/criminal-economy')return economy.state({identity});
  if(method==='GET'&&path==='/v1/criminal-economy/history')return economy.history({identity});
  if(method==='GET'&&path==='/v1/criminal-economy/admin/organizations')return economy.adminOrganizations({identity});
  if(method==='POST'&&path==='/v1/criminal-economy/admin/organization'){
   body=exactBody(body,['uid','organizationCode','rankCode']);
   return economy.setOrganization({identity,uid:body.uid,organizationCode:body.organizationCode,rankCode:body.rankCode});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/activities/start'){
   body=exactBody(body,['activityCode','requestId']);
   return economy.startActivity({identity,activityCode:body.activityCode,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/activities/complete'){
   body=exactBody(body,['activityCode','activityRunId','requestId']);
   return economy.completeActivity({identity,activityCode:body.activityCode,activityRunId:body.activityRunId,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/activities/cancel'){
   body=exactBody(body,['activityRunId','requestId']);
   return economy.cancelActivity({identity,activityRunId:body.activityRunId,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&/^\/v1\/criminal-economy\/shops\/(supply|buy|recall|proceeds|price)$/.test(path)){
   const operation=path.split('/').pop();
   body=exactBody(body,operation==='proceeds'?['shopCode','quantity','requestId']:
    operation==='price'?['shopCode','itemCode','priceCents','requestId']:operation==='recall'?['shopCode','itemCode','quantity','requestId']:['shopCode','itemCode','quantity','priceCents','requestId']);
   return economy.shopOperation({identity,operation,...body,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/warehouse/deposit'){
   body=exactBody(body,['itemCode','quantity','requestId']);
   return economy.deposit({identity,itemCode:body.itemCode,quantity:body.quantity,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/warehouse/withdraw'){
   body=exactBody(body,['itemCode','quantity','requestId']);
   return economy.withdraw({identity,itemCode:body.itemCode,quantity:body.quantity,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&path==='/v1/criminal-economy/production/craft'){
   body=exactBody(body,['recipeCode','batches','requestId']);
   return economy.craft({identity,recipeCode:body.recipeCode,batches:body.batches,idempotencyKey:body.requestId});
  }
  if(method==='POST'&&['/v1/criminal-economy/production/complete','/v1/criminal-economy/production/cancel'].includes(path)){
   body=exactBody(body,['productionId','requestId']);
   return economy.finishProduction({identity,productionId:body.productionId,cancel:path.endsWith('/cancel'),idempotencyKey:body.requestId});
  }
  return null;
 };
}
