import {RoomError} from './multiplayer.mjs';
import {isPositionWithinWorld} from './world-bounds.mjs';

const error=(status,code,message)=>{throw new RoomError(status,code,message);};
const uid=value=>/^[1-9][0-9]{0,18}$/.test(value||'')?value:error(400,'COMMAND_USAGE','Informe o ID numérico do jogador.');

export function createPoliceCommands({pool,transaction,room}){
 async function target(client,value){
  const result=await client.query(`SELECT a.uid::text AS uid,c.id,c.name,c.first_name
   FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id AND c.deleted_at IS NULL
   WHERE a.uid::text=$1 AND a.status='active'`,[uid(value)]);
  if(!result.rowCount)error(404,'PLAYER_NOT_FOUND','Jogador não encontrado.');
  return result.rows[0];
 }
 async function command({name,args,identity}){
  if(name==='190'){
   const message=args.normalize('NFC').trim();
   if(message.length<3||message.length>180)error(400,'COMMAND_USAGE','Uso: /190 [MENSAGEM] (3 a 180 caracteres)');
   const member=room.findOnline(identity.uid);
   const position=isPositionWithinWorld(identity.world,member)?member:null;
   const call=await transaction(async client=>{
    const result=await client.query(`INSERT INTO hbrp.police_calls(caller_character_id,message,world_code,x,y,z)
     VALUES($1,$2,$3,$4,$5,$6) RETURNING id`,[identity.characterId,message,position?identity.world:null,
     position?.x??null,position?.y??null,position?.z??null]);
    return result.rows[0].id;
   });
   room.broadcast(identity,'police',`CHAMADA 190 · ID ${identity.uid}: ${message}`);
   return {name,status:'ok',message:'Chamada registrada. Aguarde um policial aceitar com /proc '+identity.uid+'.',callId:call};
  }
  if(identity.organization?.code!=='pm')error(403,'POLICE_ONLY','Você não pertence à Polícia Militar.');
  const parts=args.trim()?args.trim().split(/\s+/u):[];
  if(['m','f','d','gov'].includes(name)){
   const text=args.normalize('NFC').trim();
   if(!text||Array.from(text).length>160)error(400,'COMMAND_USAGE',`Uso: /${name} [MENSAGEM] (até 160 caracteres)`);
   if(name==='gov'&&(!Number.isInteger(identity.adminLevel)||(identity.adminLevel!==1&&identity.adminLevel<2)))
    error(403,'GOV_PERMISSION','Comunicados governamentais exigem administrador nível 2 ou superior.');
   return room.broadcast(identity,{m:'megaphone',f:'faction',d:'departments',gov:'government'}[name],text);
  }
  if(name==='ticket'){
   if(parts.length<3||!/^[0-9]{1,7}$/.test(parts[1]))error(400,'COMMAND_USAGE','Uso: /ticket [ID] [QUANTIA EM R$] [MOTIVO]');
   const reais=Number(parts[1]),reason=parts.slice(2).join(' ').normalize('NFC').trim();
   if(reais<1||reais>1000000||reason.length<3||reason.length>180)
    error(400,'COMMAND_USAGE','Multa entre R$ 1 e R$ 1.000.000; motivo de 3 a 180 caracteres.');
   const player=await transaction(async client=>{
    const found=await target(client,parts[0]);
    if(found.id===identity.characterId)error(403,'SELF_TARGET','Você não pode multar o próprio personagem.');
    const inserted=await client.query(`INSERT INTO hbrp.police_tickets(character_id,officer_account_id,amount_cents,reason)
     VALUES($1,$2,$3,$4) RETURNING id`,[found.id,identity.accountId,reais*100,reason]);
    await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
     VALUES($1,'police.ticket','character',$2,$3::jsonb)`,
     [identity.accountId,found.id,JSON.stringify({ticketId:inserted.rows[0].id,amountCents:reais*100,reason})]);
    return {...found,ticketId:inserted.rows[0].id};
   });
   if(room.findOnline(player.uid))room.privateMessage(player.id,`Multa de R$ ${reais} aplicada pela Polícia Militar. Motivo: ${reason}. Use /minhasmultas para consultar e pagar.`);
   return {name,status:'ok',message:`Multa pendente de R$ ${reais} aplicada ao ID ${player.uid}. Motivo: ${reason}`,
    lines:[`Código da multa: ${player.ticketId}`]};
  }
  if(name==='proc'){
   if(parts.length!==1)error(400,'COMMAND_USAGE','Uso: /proc [ID DO SOLICITANTE]');
   const player=await target(pool,parts[0]);
   const call=await transaction(async client=>{
    const result=await client.query(`SELECT id,message FROM hbrp.police_calls
     WHERE caller_character_id=$1 AND status='open' ORDER BY created_at DESC LIMIT 1 FOR UPDATE`,[player.id]);
    if(!result.rowCount)error(404,'CALL_NOT_FOUND','Não há chamada policial aberta deste jogador.');
    try{
     await client.query(`UPDATE hbrp.police_calls SET status='accepted',officer_account_id=$2,accepted_at=now()
      WHERE id=$1 AND status='open'`,[result.rows[0].id,identity.accountId]);
    }catch(cause){
     if(cause.code==='23505')error(409,'CALL_ALREADY_ASSIGNED','Encerre o atendimento atual antes de aceitar outro.');
     throw cause;
    }
    return result.rows[0];
   });
   if(room.findOnline(player.uid))room.privateMessage(player.id,`Sua chamada 190 foi aceita por ${identity.firstName||identity.name} (ID ${identity.uid}).`);
   room.broadcast(identity,'police',`Atendimento assumido por ${identity.firstName||identity.name}: ID ${player.uid} · ${call.message}`);
   return {name,status:'ok',message:`Chamada de ID ${player.uid} aceita: ${call.message}`};
  }
  if(name==='ide'){
   if(parts.length!==1)error(400,'COMMAND_USAGE','Uso: /ide [ID]');
   const player=await target(pool,parts[0]);
   if(!room.findOnline(player.uid))error(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
   room.privateMessage(player.id,`Identificação policial: ${identity.name} · ID ${identity.uid} · Polícia Militar.`);
   return {name,status:'ok',message:`Identificação mostrada a ${player.first_name||player.name} (ID ${player.uid}).`};
  }
  if(name==='wanted'){
   if(parts.length)error(400,'COMMAND_USAGE','Uso: /wanted');
   const result=await pool.query(`SELECT a.uid::text AS uid,c.name,w.stars,w.reason
    FROM hbrp.character_wanted w JOIN hbrp.characters c ON c.id=w.character_id AND c.deleted_at IS NULL
    JOIN hbrp.accounts a ON a.id=c.account_id ORDER BY w.stars DESC,w.updated_at DESC LIMIT 50`);
   return {name,status:'ok',message:result.rowCount?`${result.rowCount} procurado(s):`:'Nenhum procurado no momento.',
    wanted:result.rows.map(row=>({uid:row.uid,name:row.name,stars:Number(row.stars),reason:row.reason})),
    lines:result.rows.map(row=>`ID ${row.uid} · ${row.name} · ${row.stars} estrela(s) · ${row.reason}`)};
  }
  if(['cuff','uncuff','follow','stay'].includes(name)){
   if(name==='stay' ? parts.length!==0 : parts.length!==1)
    error(400,'COMMAND_USAGE',name==='stay'?'Uso: /stay':`Uso: /${name} [ID]`);
   return room.custody(identity,name,name==='stay'?null:parts[0]?.toUpperCase()==='BOT1'?'BOT1':uid(parts[0]));
  }
  if(['put','eject','carpolicia'].includes(name)){
   if(name==='carpolicia' ? parts.length!==0 : parts.length!==1)
    error(400,'COMMAND_USAGE',name==='carpolicia'?'Uso: /carpolicia':`Uso: /${name} [ID]`);
   return room.patrol(identity,name,name==='carpolicia'?null:uid(parts[0]));
  }
  if(name==='su'){
   if(parts.length<3||!/^\d+$/.test(parts[1]))error(400,'COMMAND_USAGE','Uso: /su [ID] [1-6 ESTRELAS] [MOTIVO]');
   const stars=Number(parts[1]),reason=parts.slice(2).join(' ').normalize('NFC').trim();
   if(stars<1||stars>6||reason.length<3||reason.length>180)
    error(400,'COMMAND_USAGE','Use de 1 a 6 estrelas e um motivo de 3 a 180 caracteres.');
   const player=await transaction(async client=>{
    const found=await target(client,parts[0]);
    await client.query(`INSERT INTO hbrp.character_wanted(character_id,stars,reason,issued_by)
     VALUES($1,$2,$3,$4) ON CONFLICT(character_id) DO UPDATE
     SET stars=EXCLUDED.stars,reason=EXCLUDED.reason,issued_by=EXCLUDED.issued_by,updated_at=now()`,
     [found.id,stars,reason,identity.accountId]);
    await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
     VALUES($1,'police.wanted_set','character',$2,$3::jsonb)`,
     [identity.accountId,found.id,JSON.stringify({uid:found.uid,stars,reason})]);
    return found;
   });
   return {name,status:'ok',message:`${player.name} (ID ${player.uid}) agora tem ${stars} estrela(s). Motivo: ${reason}`};
  }
  if(name==='clear'){
   if(parts.length!==1)error(400,'COMMAND_USAGE','Uso: /clear [ID]');
   const player=await transaction(async client=>{
    const found=await target(client,parts[0]);
    await client.query('DELETE FROM hbrp.character_wanted WHERE character_id=$1',[found.id]);
    await client.query(`INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details)
     VALUES($1,'police.wanted_clear','character',$2,$3::jsonb)`,
     [identity.accountId,found.id,JSON.stringify({uid:found.uid})]);
    return found;
   });
   return {name,status:'ok',message:`Procurado removido de ${player.name} (ID ${player.uid}).`};
  }
  error(400,'UNKNOWN_COMMAND','Comando policial desconhecido.');
 }
 return {command};
}
