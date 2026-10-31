import {createHmac,randomUUID,randomInt} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {isIP} from 'node:net';
export class RealtimeError extends Error{constructor(status,code,message){super(message);Object.assign(this,{status,code});}}
const reject=(code,message,status=403)=>{throw new RealtimeError(status,code,message);};
const b64=value=>Buffer.from(value).toString('base64url');
export function createRealtimeAccess({room,env=process.env,clock=Date.now}={}){
 let voiceSerial=randomInt(1,1000000000);
 const udp={host:env.HBRP_UDP_HOST||'',port:Number(env.HBRP_UDP_PORT||7777),serverName:env.HBRP_UDP_SERVER_NAME||'',
  caCertificate:env.HBRP_UDP_CA_FILE?readFileSync(env.HBRP_UDP_CA_FILE,'utf8'):env.HBRP_UDP_CA||''};
 const secret=env.HBRP_REALTIME_SECRET||'';
 const udpReady=secret.length>=32&&isIP(udp.host)!==0&&udp.port>0&&udp.port<=65535&&udp.serverName&&udp.caCertificate.includes('BEGIN CERTIFICATE');
 const issuer=env.VIVOX_ISSUER||'',domain=env.VIVOX_DOMAIN||'',server=env.VIVOX_SERVER||'',key=env.VIVOX_SIGNING_KEY||'';
 const environmentId=env.VIVOX_ENVIRONMENT_ID||'';
 const voiceReady=!!(issuer&&domain&&server.startsWith('https://')&&key.length>=16);
 function channels(identity){return {proximity:'hbrp-'+identity.world,radioPrefix:'hbrp-police-'+identity.world+'-'};}
 function voiceConfig(identity){return {available:voiceReady,server,domain,issuer,environmentId,
  projectId:env.VIVOX_PROJECT_ID||'',playerId:'hbrp-'+identity.uid,...channels(identity)};}
 function config(identity){return {udp:{available:!!udpReady},vivox:voiceConfig(identity)};}
 function ticket(identity,auth,body){
  if(!udpReady)return {available:false};
  if(!body||Object.keys(body).some(k=>k!=='clientId')||!/^[a-f0-9-]{36}$/i.test(body.clientId||''))reject('INVALID_REQUEST','Cliente inválido.',400);
  const member=room.findOnline(identity.uid);
  if(!member||member.sessionId!==auth.id||member.clientId!==body.clientId)reject('NOT_IN_ROOM','Entre na sala antes de conectar ao UDP.',409);
  const claims={v:1,characterId:identity.characterId,uid:identity.uid,world:identity.world,clientId:body.clientId,sessionId:auth.id,jti:randomUUID(),exp:Math.floor(clock()/1000)+15};
  const payload=b64(JSON.stringify(claims));
  return {available:true,...udp,ticket:payload+'.'+createHmac('sha256',secret).update('hbrp-utp-v1.'+payload).digest('base64url'),expiresAt:claims.exp};
 }
 function voiceToken(identity,auth,body){
  if(!voiceReady)reject('VIVOX_NOT_CONFIGURED','Vivox ainda não foi ativado.',503);
  if(!body||Object.keys(body).some(k=>!['action','channelUri','fromUserUri'].includes(k))||!['login','join'].includes(body.action))
   reject('VIVOX_ACTION_DENIED','Operação de voz inválida.');
  const suffix=environmentId?'.'+environmentId:'';
  const from=`sip:.${issuer}.hbrp-${identity.uid}${suffix}.@${domain}`;
  if(body.fromUserUri!==from)reject('VIVOX_IDENTITY_DENIED','A identidade de voz não pertence à conta.');
  const c=channels(identity);
  if(body.action==='join'){
   const prefix=`sip:confctl-d-${issuer}.${c.proximity}${suffix}!p-35-2-1.000-`;
   const proximity=[0,1,2,3].some(model=>body.channelUri===prefix+model+'@'+domain);
   if(!proximity){
    const radio=room.radio(identity,auth.id);
    const expected=`sip:confctl-g-${issuer}.${c.radioPrefix}${radio.radioChannel}${suffix}@${domain}`;
    if(!radio.policeDuty||radio.radioChannel<1||body.channelUri!==expected)reject('VIVOX_CHANNEL_DENIED','Canal de rádio não autorizado.');
   }
  }else if(body.channelUri)reject('VIVOX_CHANNEL_DENIED','Canal inesperado.');
  const claims={iss:issuer,exp:Math.floor(clock()/1000)+60,vxa:body.action,vxi:++voiceSerial,f:from};
  if(body.action==='join')claims.t=body.channelUri;
  const input=b64('{}')+'.'+b64(JSON.stringify(claims));
  return {token:input+'.'+createHmac('sha256',key).update(input).digest('base64url')};
 }
 return {config,ticket,voiceToken};
}
