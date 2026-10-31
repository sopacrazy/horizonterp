import test from 'node:test';
import assert from 'node:assert/strict';
import {createHmac} from 'node:crypto';
import {createRealtimeAccess} from '../realtime-access.mjs';
const actor={characterId:'11111111-1111-4111-8111-111111111111',uid:'18',world:'centro_teste'};
const clientId='22222222-2222-4222-8222-222222222222',auth={id:'33333333-3333-4333-8333-333333333333'};
const env={HBRP_REALTIME_SECRET:'local-test-secret-only-12345678901234567890',HBRP_UDP_HOST:'127.0.0.1',HBRP_UDP_PORT:'7777',HBRP_UDP_SERVER_NAME:'localhost',HBRP_UDP_CA:'-----BEGIN CERTIFICATE-----\ntest-public-ca\n-----END CERTIFICATE-----',
 VIVOX_SERVER:'https://example.vivox.com/api2',VIVOX_DOMAIN:'example.vivox.com',VIVOX_ISSUER:'test-issuer',VIVOX_SIGNING_KEY:'test-key-never-used-for-real-service'};
const room={findOnline:()=>({sessionId:auth.id,clientId}),radio:()=>({policeDuty:true,radioChannel:2})};

test('UDP ticket binds authenticated identity, client, session and a short expiry; never exposes secret',()=>{
 const service=createRealtimeAccess({room,env,clock:()=>10000000});
 const reply=service.ticket(actor,auth,{clientId});assert.equal(reply.available,true);
 const [payload,signature]=reply.ticket.split('.');const claims=JSON.parse(Buffer.from(payload,'base64url'));
 assert.equal(claims.characterId,actor.characterId);assert.equal(claims.clientId,clientId);assert.equal(claims.sessionId,auth.id);assert.equal(claims.exp,10015);
 assert.equal(signature,createHmac('sha256',env.HBRP_REALTIME_SECRET).update('hbrp-utp-v1.'+payload).digest('base64url'));
 assert.ok(!JSON.stringify(reply).includes(env.HBRP_REALTIME_SECRET));
 assert.throws(()=>service.ticket(actor,auth,{clientId,uid:'999'}),e=>e.code==='INVALID_REQUEST');
 assert.throws(()=>service.ticket(actor,{id:'other'},{clientId}),e=>e.code==='NOT_IN_ROOM');
 assert.equal(createRealtimeAccess({room,env:{}}).ticket(actor,auth,{clientId}).available,false);
});
test('Vivox VAT scope permits only the own identity, proximity and the server-selected police channel',()=>{
 const service=createRealtimeAccess({room,env,clock:()=>10000000});
 const from='sip:.test-issuer.hbrp-18.@example.vivox.com';
 const login=service.voiceToken(actor,auth,{action:'login',fromUserUri:from});const parts=login.token.split('.');
 assert.equal(Buffer.from(parts[0],'base64url').toString(),'{}');
 const payload=JSON.parse(Buffer.from(parts[1],'base64url'));assert.equal(payload.f,from);assert.equal(payload.exp,10060);assert.equal(payload.vxa,'login');assert.ok(Number.isSafeInteger(payload.vxi));
 assert.equal(parts[2],createHmac('sha256',env.VIVOX_SIGNING_KEY).update(parts[0]+'.'+parts[1]).digest('base64url'));
 for(const uri of ['sip:confctl-d-test-issuer.hbrp-centro_teste!p-35-2-1.000-1@example.vivox.com','sip:confctl-g-test-issuer.hbrp-police-centro_teste-2@example.vivox.com'])
  assert.ok(service.voiceToken(actor,auth,{action:'join',fromUserUri:from,channelUri:uri}).token);
 for(const body of [{action:'kick',fromUserUri:from},{action:'login',fromUserUri:from.replace('18','19')},
  {action:'join',fromUserUri:from,channelUri:'sip:confctl-g-test-issuer.hbrp-police-centro_teste-3@example.vivox.com'},
  {action:'join',fromUserUri:from,channelUri:'sip:confctl-d-test-issuer.hbrp-centro_teste!p-999-2-1.000-1@example.vivox.com'}])assert.throws(()=>service.voiceToken(actor,auth,body));
 const publicConfig=JSON.stringify(service.config(actor));assert.ok(!publicConfig.includes(env.VIVOX_SIGNING_KEY));assert.ok(!publicConfig.includes(env.HBRP_REALTIME_SECRET));
 assert.throws(()=>createRealtimeAccess({room,env:{}}).voiceToken(actor,auth,{}),e=>e.code==='VIVOX_NOT_CONFIGURED');
});
