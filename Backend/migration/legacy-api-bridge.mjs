// Preserve the legacy Android certificate while both clients use the Brazil API.
import https from 'node:https';
import fs from 'node:fs';
const destination=new URL('https://187.41.106.219:8443');
const agent=new https.Agent({keepAlive:true,maxSockets:32,ca:fs.readFileSync('/srv/hbrp/api/tls/kvm1-ca.crt')});
const server=https.createServer({key:fs.readFileSync(process.env.TLS_KEY),cert:fs.readFileSync(process.env.TLS_CERT),minVersion:'TLSv1.2'},(req,res)=>{
 const path=req.url||'/';
 if(path!=='/health'&&!path.startsWith('/v1/')){res.writeHead(404);res.end();return;}
 const headers={...req.headers,host:destination.host};delete headers.connection;
 const upstream=https.request({hostname:destination.hostname,port:destination.port,path,method:req.method,headers,agent,timeout:20000},incoming=>{
  res.writeHead(incoming.statusCode,incoming.headers);incoming.pipe(res);
 });
 upstream.on('timeout',()=>upstream.destroy(new Error('UPSTREAM_TIMEOUT')));
 upstream.on('error',()=>{if(!res.headersSent){res.writeHead(503,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'Conexão temporariamente indisponível. Tente novamente.',code:'BUSY'}));}else res.destroy();});
 req.on('aborted',()=>upstream.destroy());req.pipe(upstream);
});
server.requestTimeout=30000;server.headersTimeout=10000;
server.listen(Number(process.env.PORT||8443),'0.0.0.0',()=>console.log('{"event":"hbrp_legacy_bridge_started"}'));
process.on('SIGTERM',()=>server.close(()=>{agent.destroy();process.exit(0);}));
