import {RoomError} from './multiplayer.mjs';
export function createPlayerCommands({pool,transaction,room}){
 return {async command({name,args,identity}){
  if(name==='myreports'){
   const result=await pool.query('SELECT id,message,status,answer,created_at FROM hbrp.player_reports WHERE character_id=$1 ORDER BY created_at DESC LIMIT 20',[identity.characterId]);
   const status={open:'Aberto',answered:'Respondido',closed:'Encerrado'};
   return {name,status:'ok',message:result.rows.length?'Seus relatórios mais recentes:':'Você ainda não enviou relatórios.',
    lines:result.rows.map(r=>`${r.id} · ${status[r.status]} · ${new Date(r.created_at).toISOString()} · ${r.message}${r.answer?' · Resposta: '+r.answer:''}`)};
  }
  if(name==='report'){
   if(Array.from(args).length<3)throw new RoomError(400,'COMMAND_USAGE','Uso: /report [mensagem com pelo menos 3 caracteres]');
   const report=await transaction(async client=>{
    // Serialize submissions per account so reconnects/concurrent requests cannot bypass the cooldown.
    await client.query('SELECT id FROM hbrp.accounts WHERE id=$1 FOR UPDATE',[identity.accountId]);
    const recent=await client.query("SELECT 1 FROM hbrp.player_reports WHERE character_id=$1 AND created_at>now()-interval '60 seconds' LIMIT 1",[identity.characterId]);
    if(recent.rowCount)throw new RoomError(429,'REPORT_RATE_LIMITED','Aguarde um minuto antes de enviar outro relatório.');
    return (await client.query('INSERT INTO hbrp.player_reports(character_id,message) VALUES($1,$2) RETURNING id',[identity.characterId,args])).rows[0];
   });
   room.broadcast(identity,'admin',`[REPORT ${report.id}] ID ${identity.uid}: ${args}`);
   return {name,status:'ok',message:`Relatório ${report.id} registrado. A administração foi avisada.`};
  }
  throw new RoomError(400,'UNKNOWN_COMMAND','Comando de jogador desconhecido. Use /ajuda.');
 }};
}
