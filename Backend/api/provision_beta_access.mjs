// Operator-only bootstrap. Run with the existing private DATABASE_URL in the
// environment. Plaintext credentials go only to the exclusive mode-0600 file.
import fs from 'node:fs';
import path from 'node:path';
import pg from 'pg';
import {bootstrapBetaBatch} from './beta-access.mjs';

function option(name){const index=process.argv.indexOf(name);return index<0?null:process.argv[index+1];}
const managerUid=option('--manager-uid'),batchId=option('--batch-id'),count=Number(option('--count')||5),output=option('--output');
let descriptor,pool,committed=false;
try{
 if(!managerUid||!batchId||!output||!path.isAbsolute(output)||!process.env.DATABASE_URL)throw new Error('PARAMETERS_REQUIRED');
 descriptor=fs.openSync(output,'wx',0o600);
 pool=new pg.Pool({connectionString:process.env.DATABASE_URL,max:1,connectionTimeoutMillis:10000});
 const result=await bootstrapBetaBatch({pool,managerUid,count,batchId});committed=true;
 fs.writeFileSync(descriptor,JSON.stringify({createdAt:new Date().toISOString(),batchId,...result},null,2));
 fs.fsyncSync(descriptor);fs.closeSync(descriptor);descriptor=undefined;
 console.log(JSON.stringify({created:result.credentials.length,managerUid:result.managerUid,uids:result.credentials.map(item=>item.uid),credentialsFile:output}));
}catch(error){
 if(descriptor!==undefined){fs.closeSync(descriptor);if(!committed)fs.unlinkSync(output);}
 console.error(JSON.stringify({event:'beta_bootstrap_failed',code:error.code||(error.message==='PARAMETERS_REQUIRED'?'PARAMETERS_OR_FILE_ERROR':'BOOTSTRAP_FAILED'),credentialsFileCommitted:committed}));
 process.exitCode=1;
}finally{if(pool)await pool.end();}
