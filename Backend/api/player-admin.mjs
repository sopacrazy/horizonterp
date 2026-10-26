export class AdminError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new AdminError(status,code,message);};
export function createPlayerAdmin({pool,session,transaction,removeSession}){
 async function actor(client,token){
  const auth=await session(client,token);
  const row=(await client.query('SELECT id,uid::text,account_role,admin_level FROM hbrp.accounts WHERE id=$1 AND NOT EXISTS (SELECT 1 FROM hbrp.characters WHERE account_id=$1 AND deleted_at IS NULL AND blocked_at IS NOT NULL AND (blocked_until IS NULL OR blocked_until>now()))',[auth.account_id])).rows[0];
  if(row?.account_role!=='admin'||row.admin_level<1)reject(403,'ADMIN_REQUIRED','Acesso exclusivo da administração.');
  return {...row,sessionId:auth.id};
 }
 function view(row,viewer){
  const isSelf=row.account_id===viewer.id;
  return {uid:row.uid,characterId:row.character_id,firstName:row.first_name||row.name.split(/\s+/u)[0],
   accountRole:row.account_role,adminLevel:row.admin_level,blocked:row.blocked_at!==null&&(!row.blocked_until||new Date(row.blocked_until).getTime()>Date.now()),
   blockedReason:row.blocked_reason||'',isSelf,canManage:!isSelf&&row.admin_level<viewer.admin_level};
 }
 const columns='a.id AS account_id,a.uid::text,a.account_role,a.admin_level,c.id AS character_id,c.name,c.first_name,c.blocked_at,c.blocked_until,c.blocked_reason';
 async function list(token,search='',offset=0){
  const viewer=await actor(pool,token);
  if(typeof search!=='string'||Array.from(search).length>64||/[\p{Cc}\p{Cf}]/u.test(search)||!Number.isSafeInteger(offset)||offset<0||offset>1000000)
   reject(400,'INVALID_SEARCH','Pesquisa inválida. Use nome ou ID.');
  search=search.normalize('NFC').trim();
  const pattern='%'+search.replace(/[\\%_]/g,'\\$&')+'%';
  const where="c.deleted_at IS NULL AND ($1='' OR a.uid::text=$1 OR coalesce(c.first_name,split_part(c.name,' ',1)) ILIKE $2)";
  const total=(await pool.query('SELECT count(*)::int AS total FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE '+where,[search,pattern])).rows[0].total;
  const rows=(await pool.query('SELECT '+columns+' FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE '+where+' ORDER BY a.uid DESC LIMIT 25 OFFSET $3',[search,pattern,offset])).rows;
  return {players:rows.map(row=>view(row,viewer)),total,offset,pageSize:25,viewerUid:viewer.uid,viewerAdminLevel:viewer.admin_level};
 }
 async function update(token,uid,body){
  if(!/^[1-9][0-9]{0,18}$/.test(uid)||!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!['characterId','action','adminLevel','reason','banDays'].includes(k))||
   !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(body.characterId||'')||!['set_admin','block','unblock'].includes(body.action))reject(400,'INVALID_ADMIN_ACTION','Ação administrativa inválida.');
  if(body.reason!==undefined&&typeof body.reason!=='string')reject(400,'INVALID_REASON','Motivo inválido.');
  const reason=typeof body.reason==='string'?body.reason.normalize('NFC').trim():'';
  if(Array.from(reason).length>180||/[\p{Cc}\p{Cf}]/u.test(reason)||(body.action==='block'&&reason.length<3))reject(400,'INVALID_REASON','Informe um motivo com 3 a 180 caracteres.');
  if(body.banDays!==undefined&&(body.action!=='block'||!Number.isSafeInteger(body.banDays)||body.banDays<1||body.banDays>3650))
   reject(400,'INVALID_BAN_DAYS','Escolha de 1 a 3650 dias.');
  const result=await transaction(async client=>{
   const initial=await actor(client,token);
   const required=body.action==='set_admin'||body.action==='unblock'?3:2;
   if(initial.admin_level<required)reject(403,'ADMIN_LEVEL_REQUIRED',`Esta ação exige nível ${required}.`);
   const target=(await client.query('SELECT a.id FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE a.uid::text=$1 AND c.id=$2 AND c.deleted_at IS NULL',[uid,body.characterId])).rows[0];
   if(!target)reject(404,'PLAYER_NOT_FOUND','Personagem não encontrado. Atualize a lista.');
   if(initial.id===target.id)reject(409,'SELF_MODIFICATION','Você não pode alterar a própria conta.');
   // Stable lock order across two-admin operations; recheck permission after waiting.
   await client.query('SELECT id FROM hbrp.accounts WHERE id IN ($1,$2) ORDER BY id FOR UPDATE',[initial.id,target.id]);
   const viewer=await actor(client,token);
   // Profile edits already lock session -> character. Keep the same order here.
   await client.query('SELECT id FROM hbrp.sessions WHERE account_id=$1 ORDER BY id FOR UPDATE',[target.id]);
   const row=(await client.query('SELECT '+columns+' FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE a.id=$1 AND c.id=$2 AND c.deleted_at IS NULL FOR UPDATE OF c',[target.id,body.characterId])).rows[0];
   if(!row)reject(404,'PLAYER_NOT_FOUND','Personagem não encontrado. Atualize a lista.');
   if(viewer.id===target.id)reject(409,'SELF_MODIFICATION','Você não pode alterar a própria conta.');
   if(row.admin_level>=viewer.admin_level)reject(403,'ADMIN_HIERARCHY','Só é permitido alterar jogadores e administradores de nível inferior.');
   const before={adminLevel:row.admin_level,blocked:row.blocked_at!==null};let revoked=[];
   if(body.action==='set_admin'){
    if(!Number.isSafeInteger(body.adminLevel)||body.adminLevel<0||body.adminLevel>viewer.admin_level)reject(400,'INVALID_ADMIN_LEVEL','Escolha um nível entre 0 e o seu nível administrativo.');
    await client.query('UPDATE hbrp.accounts SET account_role=$1,admin_level=$2 WHERE id=$3',[body.adminLevel>0?'admin':'user',body.adminLevel,target.id]);
   }else if(body.action==='block'){
    await client.query("UPDATE hbrp.characters SET blocked_at=now(),blocked_until=CASE WHEN $3::int IS NULL THEN NULL ELSE now()+($3::int * interval '1 day') END,blocked_by=$1,blocked_reason=$2 WHERE id=$4",[viewer.id,reason,body.banDays??null,body.characterId]);
    revoked=(await client.query('UPDATE hbrp.sessions SET revoked_at=now() WHERE account_id=$1 AND revoked_at IS NULL RETURNING id',[target.id])).rows;
   }else await client.query('UPDATE hbrp.characters SET blocked_at=NULL,blocked_until=NULL,blocked_by=NULL,blocked_reason=NULL WHERE id=$1',[body.characterId]);
   await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,$2,'character',$3,$4::jsonb)",
    [viewer.id,'admin.'+body.action,body.characterId,JSON.stringify({uid,reason,before,banDays:body.banDays??null,adminLevel:body.action==='set_admin'?body.adminLevel:row.admin_level})]);
   const updated=(await client.query('SELECT '+columns+' FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE c.id=$1',[body.characterId])).rows[0];
   return {player:view(updated,viewer),revoked};
  });
  for(const row of result.revoked)removeSession(row.id);
  return {player:result.player};
 }
 async function resolvePlayer(identifier){
  const rows=(await pool.query(`SELECT a.id,a.uid::text,c.id AS character_id,c.name,c.first_name,a.admin_level
   FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id
   WHERE c.deleted_at IS NULL AND (a.uid::text=$1 OR lower(c.name)=lower($1) OR lower(c.first_name)=lower($1))
   LIMIT 2`,[identifier])).rows;
  if(!rows.length)reject(404,'PLAYER_NOT_FOUND','Jogador não encontrado.');
  if(rows.length>1)reject(409,'AMBIGUOUS_PLAYER','Nome repetido. Use o ID do jogador.');
  return rows[0];
 }
 async function command(token,{name,args},room){
  const viewer=await actor(pool,token);
  if(name==='makeadmin'){
   if(viewer.admin_level<3)reject(403,'ADMIN_LEVEL_REQUIRED','/makeadmin exige nível 3.');
   const match=args.match(/^([1-9][0-9]{0,18})\s+([0-3])$/u);
   if(!match)reject(400,'COMMAND_USAGE','Uso: /makeadmin [ID] [nível 0-3]');
   const target=await resolvePlayer(match[1]);
   const reply=await update(token,target.uid,{characterId:target.character_id,action:'set_admin',adminLevel:Number(match[2])});
   return {name,status:'ok',message:`${target.first_name} agora é admin nível ${reply.player.adminLevel}.`};
  }
  if(name==='ban'){
   if(viewer.admin_level<2)reject(403,'ADMIN_LEVEL_REQUIRED','/ban exige nível 2.');
   const match=args.match(/^(.+?)\s+([0-9]{1,4})\s+(.+)$/u);
   if(!match)reject(400,'COMMAND_USAGE','Uso: /ban [ID/nome] [dias] [motivo]');
   const target=await resolvePlayer(match[1].trim());
   const days=Number(match[2]);
   const reply=await update(token,target.uid,{characterId:target.character_id,action:'block',banDays:days,reason:match[3].trim()});
   return {name,status:'ok',message:`${target.first_name} banido por ${days} dia(s). Motivo: ${reply.player.blockedReason}`};
  }
  if(name==='unban'){
   if(viewer.admin_level<3)reject(403,'ADMIN_LEVEL_REQUIRED','/unban exige nível 3.');
   if(!args.trim())reject(400,'COMMAND_USAGE','Uso: /unban [nome]');
   const target=await resolvePlayer(args.trim());
   await update(token,target.uid,{characterId:target.character_id,action:'unblock'});
   return {name,status:'ok',message:`Banimento de ${target.first_name} removido.`};
  }
  if(name==='kick'){
   const match=args.match(/^([1-9][0-9]{0,18})\s+(.+)$/u);
   if(!match||match[2].trim().length<3)reject(400,'COMMAND_USAGE','Uso: /kick [ID] [motivo]');
   if(!room.findOnline(match[1]))reject(404,'PLAYER_NOT_ONLINE','Jogador não está online nesta sala.');
   const reason=match[2].normalize('NFC').trim();
   if(Array.from(reason).length>180||/[\p{Cc}\p{Cf}]/u.test(reason))reject(400,'INVALID_REASON','Motivo inválido.');
   const target=await resolvePlayer(match[1]);
   const revoked=await transaction(async client=>{
    const actorNow=await actor(client,token);
    if(actorNow.id===target.id)reject(409,'SELF_MODIFICATION','Você não pode expulsar a si mesmo.');
    await client.query('SELECT id FROM hbrp.accounts WHERE id IN ($1,$2) ORDER BY id FOR UPDATE',[actorNow.id,target.id]);
    const fresh=await actor(client,token);
    const row=(await client.query('SELECT admin_level FROM hbrp.accounts WHERE id=$1 FOR UPDATE',[target.id])).rows[0];
    if(!row||row.admin_level>=fresh.admin_level)reject(403,'ADMIN_HIERARCHY','Só é permitido expulsar níveis inferiores.');
    const sessions=(await client.query('UPDATE hbrp.sessions SET revoked_at=now() WHERE account_id=$1 AND revoked_at IS NULL RETURNING id',[target.id])).rows;
    await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,'admin.kick','character',$2,$3::jsonb)",
     [fresh.id,target.character_id,JSON.stringify({uid:target.uid,reason})]);
    return sessions;
   });
   for(const row of revoked)removeSession(row.id);
   return {name,status:'ok',message:`${target.first_name} expulso. Motivo: ${reason}`};
  }
  reject(400,'UNKNOWN_COMMAND','Comando desconhecido.');
 }
 return {list,update,command};
}
