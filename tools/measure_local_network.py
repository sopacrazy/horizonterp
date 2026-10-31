"""Read-only HTTPS timing from this computer. Does not create players or alter the VPS."""
from pathlib import Path
import http.client,ssl,time,json,statistics,hashlib
root=Path(__file__).resolve().parents[1]
config=json.loads((root/'TestePersonagem/Assets/Resources/ApiConfig.json').read_text(encoding='utf-8'))
from urllib.parse import urlsplit
url=urlsplit(config['baseUrl'])
context=ssl.create_default_context(cafile=str(root/'tools/release070-kvm1-ca.crt'))
def connection():return http.client.HTTPSConnection(url.hostname,url.port,context=context,timeout=5)
def request(conn):
    started=time.perf_counter();conn.request('GET','/health',headers={'User-Agent':'HBRP-readonly-latency-check'})
    reply=conn.getresponse();body=json.loads(reply.read());assert reply.status==200 and body['status']=='ok'
    return (time.perf_counter()-started)*1000
def stats(values):
    ordered=sorted(values)
    return {'samples':len(values),'minMs':round(min(values),1),'medianMs':round(statistics.median(values),1),'p95Ms':round(ordered[min(len(ordered)-1,int(len(ordered)*.95))],1),'maxMs':round(max(values),1)}
conn=connection();conn.connect()
assert hashlib.sha256(conn.sock.getpeercert(binary_form=True)).hexdigest()==config['certificateSha256']
warm=[];cold=[];errors=[]
for _ in range(30):
    try:warm.append(request(conn))
    except Exception as exc:errors.append(type(exc).__name__);conn.close();conn=connection()
    time.sleep(.1)
conn.close()
for _ in range(10):
    conn=connection()
    try:cold.append(request(conn))
    except Exception as exc:errors.append(type(exc).__name__)
    finally:conn.close()
    time.sleep(.1)
report={'server':url.hostname,'tlsVerified':True,'warmHttps':stats(warm),'newConnectionHttps':stats(cold),'errors':errors,'scope':'health only; actual game requests and the other player connection need client diagnostics'}
(root/'tools/local-network-latency.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
