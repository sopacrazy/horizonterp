import {randomUUID} from 'node:crypto';
import {RoomError} from './multiplayer.mjs';
import {isPositionWithinWorld} from './world-bounds.mjs';

export const POLICE_STATION = Object.freeze({world:'centro_teste',x:1546,y:12.62,z:1676,yaw:90});
export const POLICE_HOLDING = Object.freeze({world:POLICE_STATION.world,x:-5,y:-99.9,z:42.8,yaw:180});
export const POLICE_LEGACY_HOLDING = Object.freeze({world:POLICE_STATION.world,x:1544,y:12.62,z:1678,yaw:90});
export const POLICE_CELLS = Object.freeze([-5,0,5].map(x=>Object.freeze({...POLICE_HOLDING,x})));
export const POLICE_ENTRY = Object.freeze({world:POLICE_STATION.world,x:0,y:-99.9,z:35.5,yaw:0});
export const POLICE_RANKS = Object.freeze(['recruta','oficial','comando']);

const fail = (status,code,message) => { throw new RoomError(status,code,message); };
const numericUid = value => /^[1-9][0-9]{0,18}$/.test(value || '');
const money = cents => 'R$ ' + (Number(cents)/100).toFixed(2).replace('.',',');
const admin = identity => identity.accountRole==='admin' && [1,2,3].includes(identity.adminLevel);
const commandRank = identity => identity.adminLevel!==4 && identity.organization?.code==='pm' && identity.organization.rank==='comando';
const officerRank = identity => identity.adminLevel!==4 && identity.organization?.code==='pm' && ['oficial','comando'].includes(identity.organization.rank);
const distance = (a,b) => Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);

export function createPoliceOperations({pool,transaction,room,setDuty}) {
 async function audit(client,actor,action,id,details={}) {
  await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
   VALUES($1,$2,'character',$3,$4::jsonb)`,[actor.accountId,'police.'+action,id,JSON.stringify(details)]);
 }
 async function target(client,uid,lock=false) {
  if(!numericUid(uid))fail(400,'COMMAND_USAGE','Informe o ID numérico do jogador.');
  const result=await client.query(`SELECT c.id,c.name,a.id AS account_id,a.uid::text AS uid FROM hbrp.characters c
   JOIN hbrp.accounts a ON a.id=c.account_id WHERE c.deleted_at IS NULL AND a.status='active'
   AND a.uid::text=$1 ${lock?'FOR UPDATE OF c':''}`,[uid]);
  if(!result.rowCount)fail(404,'PLAYER_NOT_FOUND','Jogador não encontrado.');
  return result.rows[0];
 }
 function presence(identity) {
  const member=room.findOnline(identity.uid);
  if(!member || member.characterId!==identity.characterId)fail(409,'NOT_IN_ROOM','Entre na cidade.');
  return member;
 }
 function nearby(identity,uid) {
  if(!numericUid(uid)&&uid!=='BOT1')fail(400,'COMMAND_USAGE','Informe o ID numérico do jogador ou BOT1.');
  const actor=presence(identity),found=room.findOnline(uid);
  if(!found || found.world!==actor.world || found.characterId===actor.characterId || distance(actor,found)>4)
   fail(409,'TARGET_TOO_FAR','Aproxime-se a até 4 metros de outro jogador.');
  return found;
 }
 function inPrison(member) {return member.world===POLICE_STATION.world&&member.y< -94&&member.y> -105&&Math.abs(member.x)<10&&member.z>31&&member.z<48;}
 function station(identity,allowInterior=false) {
  const member=presence(identity);
  if(member.world!==POLICE_STATION.world || (distance(member,POLICE_STATION)>18&&!(allowInterior&&inPrison(member))) || member.driving)
   fail(409,'POLICE_STATION_REQUIRED','Vá a pé até a delegacia. Use /delegacia para ver no mapa.');
 }
 function requireOfficer(identity) {
  if(!officerRank(identity))fail(403,'POLICE_RANK_REQUIRED','Esta ação exige patente Oficial ou Comando.');
 }
 function checkReason(reason) {
  if(reason.length<3||reason.length>180)fail(400,'COMMAND_USAGE','Informe um motivo de 3 a 180 caracteres.');
 }
 function mapResult(point) {return {status:'police_map',x:point.x,y:point.y,z:point.z};}
 async function sentence(identity,uid,minutes,reason,atStation) {
  if(!/^[0-9]{1,3}$/.test(minutes||'')||Number(minutes)<1||Number(minutes)>120)
   fail(400,'COMMAND_USAGE','Informe uma pena entre 1 e 120 minutos e o motivo.');
  checkReason(reason);
  let prisonReady=false;
  if(atStation){
   requireOfficer(identity);
   station(identity,true);
   const detained=nearby(identity,uid);
   room.requirePoliceCustody(identity,detained.characterId);
   prisonReady=detained.prisonReady===true||detained.isBot===true;
  }
  else prisonReady=room.findOnline(uid)?.prisonReady===true;
  if(uid==='BOT1'){
   if(!atStation)fail(403,'POLICE_STATION_REQUIRED','Use o bot na delegacia para testar a custódia.');
   const occupied=await pool.query(`SELECT p.x FROM hbrp.police_sentences s
    JOIN hbrp.character_positions p ON p.character_id=s.character_id
    WHERE s.released_at IS NULL AND s.ends_at>now() AND p.y< -94 AND p.z BETWEEN 40 AND 46`);
   const cell=POLICE_CELLS.find(candidate=>!occupied.rows.some(row=>Math.abs(Number(row.x)-candidate.x)<1));
   if(!cell)fail(409,'PRISON_FULL','As três celas estão ocupadas. Aguarde uma vaga.');
   room.jailTestBot(identity,minutes,cell);
   return `BOT1 colocado na cela por ${minutes} min. Teste local, sem registro no banco.`;
  }
  const prisoner=await transaction(async client=>{
   const found=await target(client,uid,true);
   if(found.id===identity.characterId)fail(403,'SELF_TARGET','Escolha outro jogador.');
   const active=await client.query(`SELECT 1 FROM hbrp.police_sentences
    WHERE character_id=$1 AND released_at IS NULL AND ends_at>now()`,[found.id]);
   if(active.rowCount)fail(409,'ALREADY_JAILED','O jogador já está cumprindo pena.');
   let cell=POLICE_LEGACY_HOLDING;
   if(prisonReady){
    const occupied=await client.query(`SELECT p.x FROM hbrp.police_sentences s
     JOIN hbrp.character_positions p ON p.character_id=s.character_id
     WHERE s.released_at IS NULL AND s.ends_at>now() AND p.y< -94 AND p.z BETWEEN 40 AND 46`);
    cell=POLICE_CELLS.find(candidate=>!occupied.rows.some(row=>Math.abs(Number(row.x)-candidate.x)<1));
    if(!cell)fail(409,'PRISON_FULL','As três celas estão ocupadas. Aguarde uma vaga.');
   }
   await client.query(`INSERT INTO hbrp.police_sentences(character_id,officer_account_id,reason,ends_at)
    VALUES($1,$2,$3,now()+$4::int*interval '1 minute')`,[found.id,identity.accountId,reason,Number(minutes)]);
   const moved=await client.query(`UPDATE hbrp.character_positions SET x=$2,y=$3,z=$4,yaw=$5,camera_yaw=$5,
    version=version+1,updated_at=now() WHERE character_id=$1 AND world_id=(SELECT id FROM hbrp.worlds WHERE code=$6)`,
    [found.id,cell.x,cell.y,cell.z,cell.yaw,cell.world]);
   if(!moved.rowCount)fail(409,'WORLD_UNAVAILABLE','O jogador não pertence a esta cidade.');
   await audit(client,identity,'sentence',found.id,{minutes:Number(minutes),reason});
   return found;
  });
  room.applyPoliceSentence(prisoner.id);
  room.privateMessage(prisoner.id,`Pena de ${minutes} min. Motivo: ${reason}.`);
  return `Ocorrência registrada. ID ${prisoner.uid} preso por ${minutes} min.`;
 }
 async function command({name,args,identity,sessionId}) {
  const parts=args.trim().split(/\s+/u).filter(Boolean);
  const [first,second]=parts;
  const ok=(message,extra={})=>({name,status:'ok',message,...extra});
  if(name==='servico'){
   if(parts.length!==1||!['on','off'].includes(first))fail(400,'COMMAND_USAGE','Uso: /servico [on|off]');
   const state=await setDuty(identity,sessionId,{enabled:first==='on'});
   return ok(state.policeDuty?'Você entrou em serviço na PM.':'Você saiu de serviço.');
  }
  if(['pmrecrutar','pmpatente','pmdemitir'].includes(name)){
   if(!admin(identity)&&!commandRank(identity))fail(403,'POLICE_RANK_REQUIRED','A gestão de membros exige Comando ou administração.');
   if(parts.length!==(name==='pmpatente'?2:1)||name==='pmpatente'&&!POLICE_RANKS.includes(second))
    fail(400,'COMMAND_USAGE',name==='pmpatente'?'Uso: /pmpatente [ID] [recruta|oficial|comando]':`Uso: /${name} [ID]`);
   const found=await transaction(async client=>{
    const member=await target(client,first,true);
    if(member.id===identity.characterId)fail(403,'SELF_TARGET','Escolha outro jogador.');
    const current=(await client.query(`SELECT o.id,o.code,m.rank_code FROM hbrp.organization_members m
     JOIN hbrp.organizations o ON o.id=m.organization_id WHERE m.character_id=$1`,[member.id])).rows[0];
    if(!admin(identity)&&(second==='comando'||current?.rank_code==='comando'))
     fail(403,'POLICE_RANK_REQUIRED','Somente a administração pode alterar o Comando.');
    if(name==='pmrecrutar'){
     if(current)fail(409,'ORGANIZATION_CONFLICT','O jogador já pertence a uma organização.');
     await client.query(`INSERT INTO hbrp.organization_members(organization_id,character_id,rank_code)
      SELECT id,$1,'recruta' FROM hbrp.organizations WHERE code='pm'`,[member.id]);
    }else{
     if(current?.code!=='pm')fail(404,'POLICE_MEMBER_NOT_FOUND','Jogador não pertence à PM.');
     if(name==='pmpatente')await client.query('UPDATE hbrp.organization_members SET rank_code=$2 WHERE character_id=$1',[member.id,second]);
    else {
     await client.query('DELETE FROM hbrp.organization_members WHERE character_id=$1',[member.id]);
     await client.query(`UPDATE hbrp.police_calls SET status='open',officer_account_id=NULL,accepted_at=NULL
      WHERE officer_account_id=$1 AND status='accepted'`,[member.account_id]);
    }
    }
    await audit(client,identity,name,member.id,{rank:second||null});
    return member;
   });
   if(name==='pmdemitir')room.revokePoliceDuty(found.id);
   room.privateMessage(found.id,`Polícia Militar: sua situação foi atualizada (/${name}).`);
   return ok(`Polícia Militar atualizada para o ID ${first}.`);
  }
  if(name==='cela'){
   requireOfficer(identity);
   const member=presence(identity);
   if(!member.policeDuty||!inPrison(member))fail(403,'POLICE_STATION_REQUIRED','Entre na custódia em serviço para colocar o preso na cela.');
   const detained=room.followingDetainee(identity);
   if(!detained)fail(409,'NOT_YOUR_DETAINEE','Conduza um preso algemado com /follow até a custódia.');
   return ok(await sentence(identity,detained.uid,first,parts.slice(1).join(' '),true));
  }
  if(name==='jail'||name==='prender'){
   if(name==='jail'&&!admin(identity))fail(403,'ADMIN_ONLY','Comando administrativo.');
   return ok(await sentence(identity,first,second,parts.slice(2).join(' '),name==='prender'));
  }
  if(name==='soltar'&&first?.toUpperCase()==='BOT1'){
   requireOfficer(identity);station(identity,true);
   if(parts.length!==1)fail(400,'COMMAND_USAGE','Uso: /soltar BOT1');
   room.releaseTestBot(identity);
   return ok('BOT1 liberado da cela de teste.');
  }
  if(name==='unjail'||name==='soltar'){
   if(name==='unjail'){if(!admin(identity))fail(403,'ADMIN_ONLY','Comando administrativo.');}
   else {requireOfficer(identity);station(identity,true);}
   if(parts.length!==1)fail(400,'COMMAND_USAGE',`Uso: /${name} [ID]`);
   await transaction(async client=>{
    const found=await target(client,first,true);
    if(name==='soltar'&&found.id===identity.characterId)fail(403,'SELF_TARGET','Escolha outro jogador.');
    const released=await client.query(`UPDATE hbrp.police_sentences SET released_at=now(),released_by=$2
     WHERE character_id=$1 AND ends_at>now() AND released_at IS NULL`,[found.id,identity.accountId]);
    if(!released.rowCount)fail(404,'SENTENCE_NOT_FOUND','O jogador não está preso.');
    await audit(client,identity,'release',found.id);
   });
   const player=await target(pool,first);
   room.privateMessage(player.id,'Sua pena foi encerrada por um policial. Você pode sair da prisão.');
   return ok(`ID ${first} liberado.`);
  }
  if(name==='minhasmultas'){
   if(parts.length)fail(400,'COMMAND_USAGE','Uso: /minhasmultas');
   const {rows}=await pool.query(`SELECT id,amount_cents,reason,status FROM hbrp.police_tickets
    WHERE character_id=$1 ORDER BY created_at DESC LIMIT 20`,[identity.characterId]);
   const labels={pending:'pendente',paid:'paga',cancelled:'cancelada'};
   return ok(rows.length?'Suas últimas multas:':'Você não tem multas.',{status:'police_fines',
    policeFines:rows.map(row=>({id:row.id,amount:money(row.amount_cents),reason:row.reason,status:row.status})),
    lines:rows.map(row=>`${row.id} | ${money(row.amount_cents)} | ${labels[row.status]} | ${row.reason}`)});
  }
  if(name==='ocorrencias'){
   requireOfficer(identity);
   if(parts.length!==1)fail(400,'COMMAND_USAGE','Uso: /ocorrencias [ID]');
   const player=await target(pool,first);
   const {rows}=await pool.query(`SELECT id,reason,started_at,
    CASE WHEN released_at IS NOT NULL THEN 'liberação antecipada' WHEN ends_at<=now() THEN 'cumprida' ELSE 'em andamento' END AS status
    FROM hbrp.police_sentences WHERE character_id=$1 ORDER BY started_at DESC LIMIT 10`,[player.id]);
   return ok(rows.length?`Ocorrências do ID ${first}:`:'Nenhuma ocorrência registrada.',
    {lines:rows.map(row=>`${new Date(row.started_at).toISOString().slice(0,10)} | ${row.status} | ${row.reason} | ${row.id}`)});
  }
  if(name==='pagarmulta'||name==='cancelarmulta'){
   if(parts.length!==1||!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(first||''))
    fail(400,'COMMAND_USAGE',`Uso: /${name} [código completo da multa]`);
   const paying=name==='pagarmulta';
   if(!paying)requireOfficer(identity);
   return transaction(async client=>{
    const {rows}=await client.query('SELECT * FROM hbrp.police_tickets WHERE id=$1 FOR UPDATE',[first]);
    const ticket=rows[0];
    if(!ticket||paying&&ticket.character_id!==identity.characterId||
     !paying&&!commandRank(identity)&&ticket.officer_account_id!==identity.accountId)
     fail(404,'TICKET_NOT_FOUND','Multa não encontrada para esta operação.');
    const status=paying?'paid':'cancelled';
    if(ticket.status===status)return ok(paying?'Multa já paga.':'Multa já cancelada.');
    if(ticket.status!=='pending')fail(409,'TICKET_NOT_PENDING','A multa não está pendente.');
    if(paying){
     const wallet=(await client.query("SELECT id,balance FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[identity.characterId])).rows[0];
     if(!wallet||BigInt(wallet.balance)<BigInt(ticket.amount_cents))fail(409,'INSUFFICIENT_FUNDS','Saldo em dinheiro insuficiente.');
     try{
      await client.query('SELECT hbrp.change_balance($1,$2,$3,$4)',[wallet.id,-Number(ticket.amount_cents),randomUUID(),'police.ticket '+ticket.id]);
     }catch(error){
      if(error.code==='P0001'&&/saldo/i.test(error.message))fail(409,'INSUFFICIENT_FUNDS','Saldo em dinheiro insuficiente.');
      throw error;
     }
    }
    await client.query('UPDATE hbrp.police_tickets SET status=$2,paid_at=CASE WHEN $2=\'paid\' THEN now() ELSE NULL END WHERE id=$1',[ticket.id,status]);
    await audit(client,identity,paying?'ticket_paid':'ticket_cancelled',ticket.character_id,{ticketId:ticket.id,amountCents:Number(ticket.amount_cents)});
    if(!paying)return ok('Multa cancelada.');
    const cash=(await client.query("SELECT balance::text FROM hbrp.wallets WHERE character_id=$1 AND currency='cash'",[identity.characterId])).rows[0];
    return ok(`Multa paga: ${money(ticket.amount_cents)}.`,{cashCents:cash.balance});
   });
  }
  if(name==='chamados'){
   if(parts.length)fail(400,'COMMAND_USAGE','Uso: /chamados');
   const {rows}=await pool.query(`SELECT a.uid::text,p.message,p.status FROM hbrp.police_calls p
    JOIN hbrp.characters c ON c.id=p.caller_character_id JOIN hbrp.accounts a ON a.id=c.account_id
    WHERE p.status='open' OR (p.status='accepted' AND p.officer_account_id=$1)
    ORDER BY p.created_at LIMIT 20`,[identity.accountId]);
   return ok(rows.length?'Chamados 190 da central:':'Nenhum chamado aguardando.',{status:'police_dispatch',
    policeCalls:rows,lines:rows.map(row=>`ID ${row.uid} | ${row.status==='open'?'Aguardando':'Com sua patrulha'} | ${row.message}`)});
  }
  if(name==='gpsocorrencia'){
   if(parts.length)fail(400,'COMMAND_USAGE','Uso: /gpsocorrencia');
   const {rows}=await pool.query(`SELECT world_code AS world,x,y,z FROM hbrp.police_calls
    WHERE officer_account_id=$1 AND status='accepted' AND world_code=$2 ORDER BY accepted_at DESC LIMIT 1`,
   [identity.accountId,identity.world]);
   if(!isPositionWithinWorld(identity.world,rows[0]??null))fail(404,'CALL_LOCATION_MISSING','Nenhum chamado com localização na sua patrulha.');
   return ok('Ocorrência marcada no mapa.',mapResult(rows[0]));
  }
  if(name==='despachar190'){
   if(!commandRank(identity))fail(403,'POLICE_RANK_REQUIRED','O despacho para outra patrulha exige patente Comando.');
   if(parts.length!==2||!numericUid(first)||!numericUid(second))
    fail(400,'COMMAND_USAGE','Uso: /despachar190 [ID solicitante] [ID policial]');
   const caller=await target(pool,first),officer=await target(pool,second);
   const patrol=room.findOnline(second);
   if(patrol?.characterId!==officer.id||!patrol.policeDuty||patrol.orgCode!=='pm')
    fail(409,'OFFICER_NOT_AVAILABLE','O policial deve estar em serviço na cidade.');
   let assigned;
   try{
    assigned=await pool.query(`UPDATE hbrp.police_calls SET status='accepted',officer_account_id=$2,accepted_at=now()
     WHERE id=(SELECT id FROM hbrp.police_calls WHERE caller_character_id=$1 AND status='open'
     ORDER BY created_at DESC LIMIT 1 FOR UPDATE) RETURNING id`,[caller.id,officer.account_id]);
   }catch(error){
    if(error.code==='23505')fail(409,'CALL_ALREADY_ASSIGNED','O policial já tem um chamado em andamento.');
    throw error;
   }
   if(!assigned.rowCount)fail(404,'CALL_NOT_FOUND','O jogador não tem um chamado aguardando.');
   room.privateMessage(officer.id,`Chamado 190 do ID ${first} atribuído à sua patrulha. Use /gpsocorrencia.`);
   room.privateMessage(caller.id,`A patrulha do ID ${second} foi enviada para atender seu chamado.`);
   return ok(`Chamado do ID ${first} enviado à patrulha do ID ${second}.`);
  }
  if(name==='cancelar190'||name==='encerrar190'){
   if(parts.length)fail(400,'COMMAND_USAGE',`Uso: /${name}`);
   const cancel=name==='cancelar190';
   const result=await pool.query(`UPDATE hbrp.police_calls SET status=$2,finished_at=now(),finished_by=$3
    WHERE id=(SELECT id FROM hbrp.police_calls WHERE
     ${cancel?"caller_character_id=$1 AND status IN ('open','accepted')":"officer_account_id=$1 AND status='accepted'"}
     ORDER BY created_at DESC LIMIT 1 FOR UPDATE)
    RETURNING caller_character_id`,[cancel?identity.characterId:identity.accountId,cancel?'cancelled':'closed',identity.accountId]);
   if(!result.rowCount)fail(404,'CALL_NOT_FOUND','Nenhum chamado em aberto.');
   room.privateMessage(result.rows[0].caller_character_id,cancel?'Chamado 190 cancelado.':'Seu chamado 190 foi encerrado pela PM.');
   return ok(cancel?'Chamado cancelado.':'Atendimento encerrado.');
  }
  if(name==='delegacia'){
   if(parts.length)fail(400,'COMMAND_USAGE','Uso: /delegacia');
   const member=presence(identity);
   if(inPrison(member)){
    requireOfficer(identity);
    if(!member.policeDuty)fail(403,'POLICE_ONLY','Entre em serviço na PM.');
    room.teleportPoliceEscort(identity,POLICE_STATION);
    return {...ok('Saindo da custódia.'),status:'teleport',x:POLICE_STATION.x,y:POLICE_STATION.y,z:POLICE_STATION.z};
   }
   if(member.world===POLICE_STATION.world&&distance(member,POLICE_STATION)<=18&&!member.driving&&officerRank(identity)&&member.policeDuty&&member.prisonReady){
    room.teleportPoliceEscort(identity,POLICE_ENTRY);
    return {...ok('Entrando na custódia.'),status:'teleport',x:POLICE_ENTRY.x,y:POLICE_ENTRY.y,z:POLICE_ENTRY.z};
   }
   return ok('Delegacia marcada no mapa.',mapResult(POLICE_STATION));
  }
  if(name==='documentos'||name==='revistar'||name==='apreender'){
   if(!first || name!=='apreender'&&parts.length!==1 || name==='apreender'&&(parts.length!==3||!/^\d{1,4}$/.test(parts[2])||Number(parts[2])<1))
    fail(400,'COMMAND_USAGE',name==='apreender'?'Uso: /apreender [ID] [item] [quantidade]':`Uso: /${name} [ID]`);
   const subject=nearby(identity,first);
   if(name==='documentos'){
    const {rows}=await pool.query(`SELECT state->>'phase' AS phase FROM hbrp.character_progress
     WHERE character_id=$1 AND category='mission' AND code='arrival_welcome_v1'`,[subject.characterId]);
    const phase=rows[0]?.phase;
    return ok(`ID ${subject.uid} | ${subject.name} | RG: ${['rg_issued','driving_school','completed'].includes(phase)?'emitido':'não emitido'} | CNH: ${phase==='completed'?'emitida':'não emitida'}.`);
   }
   room.requirePoliceCustody(identity,subject.characterId);
   if(name==='revistar'){
    const {rows}=await pool.query(`SELECT i.item_code,d.name,sum(i.quantity)::int AS quantity FROM hbrp.inventory_items i
     JOIN hbrp.inventories v ON v.id=i.inventory_id JOIN hbrp.item_definitions d ON d.code=i.item_code
     WHERE v.character_id=$1 GROUP BY i.item_code,d.name ORDER BY i.item_code`,[subject.characterId]);
    return ok(rows.length?`Inventário do ID ${first}:`:'Inventário vazio.',{lines:rows.map(row=>
     `${row.quantity} x ${row.name} [${row.item_code}]`)});
   }
   requireOfficer(identity);
   const quantity=Number(parts[2]);
   await transaction(async client=>{
    const inventory=(await client.query('SELECT id FROM hbrp.inventories WHERE character_id=$1 FOR UPDATE',[subject.characterId])).rows[0];
    if(!inventory)fail(404,'ITEM_NOT_FOUND','Inventário vazio.');
    const {rows}=await client.query(`SELECT id,quantity FROM hbrp.inventory_items
     WHERE inventory_id=$1 AND item_code=$2 ORDER BY slot FOR UPDATE`,[inventory.id,second]);
    if(rows.reduce((total,row)=>total+row.quantity,0)<quantity)fail(409,'ITEM_NOT_FOUND','Quantidade indisponível.');
    let remaining=quantity;
    for(const row of rows){
     const taken=Math.min(row.quantity,remaining);
     if(taken===row.quantity)await client.query('DELETE FROM hbrp.inventory_items WHERE id=$1',[row.id]);
     else await client.query('UPDATE hbrp.inventory_items SET quantity=quantity-$2,version=version+1 WHERE id=$1',[row.id,taken]);
     remaining-=taken;
     if(!remaining)break;
    }
    await audit(client,identity,'seizure',subject.characterId,{item:second,quantity});
   });
   room.privateMessage(subject.characterId,`A PM apreendeu ${quantity} x ${second}.`);
   return ok(`${quantity} x ${second} apreendido(s) do ID ${first}.`);
  }
  if(name==='placa'){
   if(parts.length!==1||!/^[a-z0-9-]{1,22}$/i.test(first||''))fail(400,'COMMAND_USAGE','Uso: /placa [placa ou ID da viatura]');
   const patrol=room.findPoliceCar(first.toLowerCase());
   if(patrol)return ok(`${patrol.id.toUpperCase()} | Polícia Militar | Responsável: ID ${patrol.ownerUid}.`);
   const {rows}=await pool.query(`SELECT v.plate,v.model_code,a.uid::text AS uid FROM hbrp.player_vehicles v
    JOIN hbrp.characters c ON c.id=v.character_id JOIN hbrp.accounts a ON a.id=c.account_id
    WHERE upper(v.plate)=upper($1) AND c.deleted_at IS NULL LIMIT 1`,[first]);
   if(!rows[0])fail(404,'PLATE_NOT_FOUND','Placa não encontrada.');
   return ok(`${rows[0].plate} | ${rows[0].model_code} | Proprietário: ID ${rows[0].uid}.`);
  }
  if(name==='arsenal'){
   if(parts.length!==1||!['pistola','rifle','guardar'].includes(first))fail(400,'COMMAND_USAGE','Uso: /arsenal [pistola|rifle|guardar]');
   station(identity);
   if(first==='rifle')requireOfficer(identity);
   room.grantPoliceWeapon(identity,{pistola:'pistol',rifle:'rifle',guardar:null}[first]);
   return ok(first==='guardar'?'Equipamento devolvido.':`Equipamento liberado: ${first}.`,
    {status:'police_weapon',weapon:first});
  }
  if(name==='sirene'||name==='giroflex'){
   if(parts.length)fail(400,'COMMAND_USAGE',`Uso: /${name}`);
   const enabled=room.togglePoliceEquipment(identity,name);
   return ok(`${name==='sirene'?'Sirene':'Giroflex'} ${enabled?'ligado':'desligado'}.`);
  }
  fail(400,'UNKNOWN_COMMAND','Comando policial desconhecido.');
 }
 return {command};
}
