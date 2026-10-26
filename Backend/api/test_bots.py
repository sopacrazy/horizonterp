"""Five persistent test players, using only the authenticated public HBRP API."""
import argparse, concurrent.futures, http.client, json, math, os, pathlib, random, secrets, ssl, threading, time, uuid

ROOT=pathlib.Path('/srv/hbrp')
CONFIG=ROOT/'bots/accounts.json'
NAMES=['Lucas','Marina','Rafael','Julia','Pedro']
AUTH_LOCK=threading.Lock()

class Api:
 def __init__(self):
  self.context=ssl.create_default_context(cafile=str(ROOT/'api/tls/server.crt'))
  self.connection=None
 def request(self,method,path,body=None,token=None):
  if self.connection is None:self.connection=http.client.HTTPSConnection('127.0.0.1',8443,context=self.context,timeout=8)
  headers={'Content-Type':'application/json'}
  if token:headers['Authorization']='Bearer '+token
  try:
   self.connection.request(method,path,json.dumps(body) if body is not None else None,headers)
   response=self.connection.getresponse();data=json.loads(response.read());return response.status,data
  except Exception:
   self.connection.close();self.connection=None;raise

def save_config(accounts):
 CONFIG.parent.mkdir(mode=0o750,parents=True,exist_ok=True)
 temporary=CONFIG.with_suffix('.tmp')
 fd=os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
 with os.fdopen(fd,'w') as out:json.dump(accounts,out)
 os.replace(temporary,CONFIG)

def provision():
 accounts=json.loads(CONFIG.read_text()) if CONFIG.exists() else []
 api=Api()
 for index,name in enumerate(NAMES):
  existing=next((a for a in accounts if a['email']==f'bot-{name.lower()}@bots.hbrp.invalid'),None)
  if existing is None:
   existing={'email':f'bot-{name.lower()}@bots.hbrp.invalid','password':secrets.token_urlsafe(32),'firstName':'Bot'+name}
   accounts.append(existing);save_config(accounts) # Persist secret before creating the account.
  status,reply=api.request('POST','/v1/auth/login',{'email':existing['email'],'password':existing['password']})
  if status==401:
   status,reply=api.request('POST','/v1/auth/register',{'email':existing['email'],'password':existing['password']})
  if status not in (200,201):raise RuntimeError('Provision failed: '+str(reply.get('code')))
  token=reply['token']
  if reply.get('needsCharacter'):
   status,reply=api.request('POST','/v1/characters',{'firstName':existing['firstName'],'lastName':'Teste','gender':'female' if index in (1,3) else 'male'},token)
   if status!=201:raise RuntimeError('Character provision failed: '+str(reply.get('code')))
  state=reply['state']
  if state['firstName']!=existing['firstName']:raise RuntimeError('Bot identity differs; refusing to replace existing data')
  if state['accountRole']!='user' or state['adminLevel']!=0:raise RuntimeError('Test bot must remain a normal player')
  existing.update(uid=state['uid'],characterId=state['characterId'])
  save_config(accounts)
 print(json.dumps({'createdOrVerified':len(accounts),'players':[{'uid':a['uid'],'name':a['firstName']} for a in accounts]}),flush=True)

class Motion:
 def __init__(self,index,rng=None):
  # Each bot owns its RNG, destination and pause clock; no group movement timer.
  self.rng=rng if rng is not None else random.Random(secrets.randbits(128))
  self.x=-10+index*5;self.z=self.rng.uniform(-2,2)
  self.yaw=self.rng.uniform(0,360);self.speed=0.;self.elapsed=0.
  self.pause_until=0.;self.next_pause=self.rng.uniform(9,38)
  self.target=self.destination();self.retarget_at=self.rng.uniform(8,27)
 def destination(self):
  for _ in range(20):
   target=(self.rng.uniform(-13,13),self.rng.uniform(-2,2))
   if math.hypot(target[0]-self.x,target[1]-self.z)>=4:return target
  return (-self.x,-self.z)
 def step(self,dt):
  dt=max(0,min(dt,.15));self.elapsed+=dt
  if self.elapsed>=self.next_pause:
   self.pause_until=self.elapsed+self.rng.uniform(.7,3.2)
   self.next_pause=self.pause_until+self.rng.uniform(14,43)
  desired_speed=0. if self.elapsed<self.pause_until else 1.65
  self.speed+=max(-3*dt,min(3*dt,desired_speed-self.speed))
  if math.hypot(self.target[0]-self.x,self.target[1]-self.z)<.4 or self.elapsed>=self.retarget_at:
   self.target=self.destination();self.retarget_at=self.elapsed+self.rng.uniform(8,27)
  dx,dz=self.target[0]-self.x,self.target[1]-self.z
  desired=math.degrees(math.atan2(dx,dz))%360
  turn=(desired-self.yaw+180)%360-180
  # Turn in place at a corner rather than walking sideways or clamping into a wall.
  self.yaw=(self.yaw+max(-200*dt,min(200*dt,turn)))%360
  distance=min(self.speed*dt,math.hypot(dx,dz)) if abs(turn)<20 else 0.
  length=math.hypot(dx,dz)
  if length>0:self.x+=dx/length*distance;self.z+=dz/length*distance
  actual_speed=distance/dt if dt else 0.
  return dict(x=self.x,y=-.10,z=self.z,yaw=self.yaw,speed=actual_speed,jumping=False,jumpPhase=0.)

def run_bot(index,account,stop):
 api=Api()
 with AUTH_LOCK:status,reply=api.request('POST','/v1/auth/login',{'email':account['email'],'password':account['password']})
 if status!=200:
  print(json.dumps({'bot':account['uid'],'event':'login_refused','code':reply.get('code')}),flush=True);return
 token=reply['token'];state=reply['state'];motion=Motion(index);client=str(uuid.uuid4());sequence=0;last=time.monotonic();checkpoint=last;report_at=last+10;seen_modes=set();saw_jump=False
 print(json.dumps({'bot':account['uid'],'event':'connected'}),flush=True)
 while not stop.is_set():
  started=time.monotonic();frame=motion.step(started-last);last=started;sequence+=1
  try:
   status,reply=api.request('POST','/v1/multiplayer/sync',dict(frame,clientId=client,sequence=sequence),token)
   if status in (401,403) or reply.get('code')=='SESSION_IN_USE':
    print(json.dumps({'bot':account['uid'],'event':'stopped_by_auth','code':reply.get('code')}),flush=True);return
   if status!=200:stop.wait(2);continue
   seen_modes.add(round(frame['speed'],2));saw_jump|=frame['jumping']
   if started>=report_at:
    print(json.dumps({'bot':account['uid'],'event':'presence','count':reply['count'],'capacity':reply['capacity'],'frames':sequence,'modes':sorted(seen_modes),'jumped':saw_jump,'nextPauseIn':round(motion.next_pause-motion.elapsed,1),'target':[round(v,2) for v in motion.target]}),flush=True);report_at=started+60
   if started-checkpoint>=60 and not frame['jumping']:
    status,saved=api.request('PUT','/v1/player/checkpoint',dict(requestId=str(uuid.uuid4()),version=state['version'],x=frame['x'],y=-.10,z=frame['z'],yaw=frame['yaw'],cameraYaw=frame['yaw'],cameraPitch=16),token)
    if status==200:
     state=saved['state'];print(json.dumps({'bot':account['uid'],'event':'checkpoint_saved','version':state['version']}),flush=True)
    checkpoint=started
  except (OSError,ValueError,http.client.HTTPException):stop.wait(2)
  stop.wait(max(0,.1-(time.monotonic()-started)))
 try:api.request('POST','/v1/multiplayer/leave',{'clientId':client},token)
 except Exception:pass

def run():
 import signal
 accounts=json.loads(CONFIG.read_text());assert len(accounts)==5
 stop=threading.Event()
 signal.signal(signal.SIGTERM,lambda *_:stop.set());signal.signal(signal.SIGINT,lambda *_:stop.set())
 with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
  futures=[executor.submit(run_bot,i,a,stop) for i,a in enumerate(accounts)]
  while not stop.wait(1):
   if all(f.done() for f in futures):break
  stop.set()
  for f in futures:
   try:f.result()
   except Exception:print('{"event":"bot_worker_failed"}',flush=True)

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--provision',action='store_true');args=parser.parse_args()
 provision() if args.provision else run()
