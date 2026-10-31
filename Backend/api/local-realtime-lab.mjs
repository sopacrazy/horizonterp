// Explicit local-only laboratory. No production connection string, data or service is used.
import {PGlite} from '@electric-sql/pglite';
import {createApiHandler} from './server.mjs';
import {readFile,writeFile,mkdir,readdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import https from 'node:https';
import {randomBytes,scryptSync,createHash} from 'node:crypto';
const root=fileURLToPath(new URL('../../',import.meta.url));
const folder=path.join(root,'tools/realtime-integration/lab'),credentials=path.join(root,'tools/realtime-integration/credentials');
await mkdir(folder,{recursive:true});
const db=new PGlite();
await db.exec('CREATE ROLE hbrp_owner NOLOGIN; CREATE ROLE hbrp_app LOGIN; GRANT CREATE ON DATABASE postgres TO hbrp_owner; SET ROLE hbrp_owner;');
for(const name of (await readdir(new URL('../database/',import.meta.url))).filter(n=>/^\d{3}_.*\.sql$/.test(n)).sort())
 await db.exec(await readFile(new URL('../database/'+name,import.meta.url),'utf8'));
await db.exec('RESET ROLE');
const password=randomBytes(24).toString('base64url'),salt=randomBytes(16).toString('hex');
const hash='scrypt$131072$8$1$'+salt+'$'+scryptSync(password,salt,64,{N:131072,r:8,p:1,maxmem:192*1024*1024}).toString('hex');
for(let i=0;i<2;i++){
 const email=`local-player-${i}@example.invalid`,first=i===0?'Lucas':'Julia';
 const account=(await db.query('INSERT INTO hbrp.accounts(email,account_role,admin_level) VALUES($1,$2,$3) RETURNING id',[email,i===0?'admin':'user',i===0?3:0])).rows[0].id;
 await db.query('INSERT INTO hbrp.password_credentials(account_id,password_hash) VALUES($1,$2)',[account,hash]);
 const character=(await db.query("INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender) VALUES($1,$2,$3,'Teste',$4) RETURNING id",[account,first+' Teste',first,i===0?'male':'female'])).rows[0].id;
 await db.query('INSERT INTO hbrp.character_stats(character_id) VALUES($1)',[character]);
 await db.query('INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z,yaw,camera_yaw,camera_pitch) SELECT $1,id,$2,12.37,1668.5,40,40,12 FROM hbrp.worlds',[character,2489.3+i*2]);
 await db.query("INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES($1,'bybeyby')",[character]);
 await db.query("INSERT INTO hbrp.wallets(character_id,currency,balance) VALUES($1,'cash',100000),($1,'bank',0)",[character]);
 await db.query('INSERT INTO hbrp.inventories(character_id) VALUES($1)',[character]);
 await writeFile(path.join(folder,`player-${i}.json`),JSON.stringify({email,password,role:i}),{mode:0o600});
}
await db.exec('SET ROLE hbrp_app');
class Pool{
 constructor(){this.tail=Promise.resolve();}
 async connect(){const previous=this.tail;let release;this.tail=new Promise(done=>release=done);await previous;return {query:(sql,args)=>db.query(sql,args),release};}
 async query(sql,args){const client=await this.connect();try{return await client.query(sql,args);}finally{client.release();}}
}
const cert=await readFile(path.join(credentials,'localhost.pem')),key=await readFile(path.join(credentials,'localhost.key'));
const servicesEnv={HBRP_REALTIME_SECRET:(await readFile(path.join(credentials,'test-secret.txt'),'utf8')).trim(),HBRP_UDP_HOST:'127.0.0.1',HBRP_UDP_PORT:'7777',HBRP_UDP_SERVER_NAME:'localhost',HBRP_UDP_CA_FILE:path.join(credentials,'localhost-ca.pem')};
const der=Buffer.from(cert.toString().replace(/-----[^-]+-----|\s/g,''),'base64');
await writeFile(path.join(folder,'public-config.json'),JSON.stringify({baseUrl:'https://127.0.0.1:18443',certificateSha256:createHash('sha256').update(der).digest('hex')}));
const server=https.createServer({cert,key},createApiHandler(new Pool(),{servicesEnv,log:()=>{},errorLog:line=>console.error(line)}));
server.listen(18443,'127.0.0.1',()=>console.log('LOCAL_REALTIME_API_READY port=18443 database=memory players=2 vivox=disabled'));
async function stop(){server.close();await db.close();process.exit(0);}
process.on('SIGTERM',stop);process.on('SIGINT',stop);
