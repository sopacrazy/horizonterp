export class AdminError extends Error {
 constructor(status,code,message){super(message);this.status=status;this.code=code;}
}
const reject=(status,code,message)=>{throw new AdminError(status,code,message);};
export function createPlayerAdmin({pool,session,transaction,removeSession}){
 async function actor(client,token){
  const auth=await session(client,token);
  const row=(await client.query('SELECT id,uid::text,account_role,admin_level FROM hbrp.accounts WHERE id=$1 AND NOT EXISTS (SELECT 1 FROM hbrp.characters WHERE account_id=$1 AND deleted_at IS NULL AND blocked_at IS NOT NULL)',[auth.account_id])).rows[0];
  if(row?.account_role!=='admin'||row.admin_level<1)reject(403,'ADMIN_REQUIRED','Acesso exclusivo da administração.');
  return {...row,sessionId:auth.id};
 }
 function view(row,viewer){
  const isSelf=row.account_id===viewer.id;
  return {uid:row.uid,characterId:row.character_id,firstName:row.first_name||row.name.split(/\s+/u)[0],
   accountRole:row.account_role,adminLevel:row.admin_level,blocked:row.blocked_at!==null,
   blockedReason:row.blocked_reason||'',isSelf,canManage:!isSelf&&row.admin_level<viewer.admin_level};
 }
 const columns='a.id AS account_id,a.uid::text,a.account_role,a.admin_level,c.id AS character_id,c.name,c.first_name,c.blocked_at,c.blocked_reason';
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
  if(!/^[1-9][0-9]{0,18}$/.test(uid)||!body||typeof body!=='object'||Array.isArray(body)||Object.keys(body).some(k=>!['characterId','action','adminLevel','reason'].includes(k))||
   !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(body.characterId||'')||!['set_admin','block','unblock'].includes(body.action))reject(400,'INVALID_ADMIN_ACTION','Ação administrativa inválida.');
  if(body.reason!==undefined&&typeof body.reason!=='string')reject(400,'INVALID_REASON','Motivo inválido.');
  const reason=typeof body.reason==='string'?body.reason.normalize('NFC').trim():'';
  if(Array.from(reason).length>180||/[\p{Cc}\p{Cf}]/u.test(reason)||(body.action==='block'&&reason.length<3))reject(400,'INVALID_REASON','Informe um motivo com 3 a 180 caracteres.');
  const result=await transaction(async client=>{
   const initial=await actor(client,token);
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
    await client.query('UPDATE hbrp.characters SET blocked_at=now(),blocked_by=$1,blocked_reason=$2 WHERE id=$3',[viewer.id,reason,body.characterId]);
    revoked=(await client.query('UPDATE hbrp.sessions SET revoked_at=now() WHERE account_id=$1 AND revoked_at IS NULL RETURNING id',[target.id])).rows;
   }else await client.query('UPDATE hbrp.characters SET blocked_at=NULL,blocked_by=NULL,blocked_reason=NULL WHERE id=$1',[body.characterId]);
   await client.query("INSERT INTO hbrp.audit_events(actor_account_id,action,entity_type,entity_id,details) VALUES($1,$2,'character',$3,$4::jsonb)",
    [viewer.id,'admin.'+body.action,body.characterId,JSON.stringify({uid,reason,before,adminLevel:body.action==='set_admin'?body.adminLevel:row.admin_level})]);
   const updated=(await client.query('SELECT '+columns+' FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id WHERE c.id=$1',[body.characterId])).rows[0];
   return {player:view(updated,viewer),revoked};
  });
  for(const row of result.revoked)removeSession(row.id);
  return {player:result.player};
 }
 return {list,update};
}
