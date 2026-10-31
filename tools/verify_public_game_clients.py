"""Two synthetic accounts in two real game processes, reaching the authorized live services.
Does not read the user's saved login. Temporary database accounts are cleaned by the admin verifier.
"""
from pathlib import Path
import subprocess,json,time,os
root=Path(__file__).resolve().parents[1];folder=root/'tools/release-071';private=folder/'credentials'
fixture=json.loads((private/'udp-probe.json').read_text(encoding='utf-8'))
client=root/'TestePersonagem/Builds/WindowsRelease/Horizonte.exe';assert client.is_file()
processes=[];handles=[]
try:
 for role in range(2):
  auth=private/f'game-player-{role}.json'
  auth.write_text(json.dumps({'email':fixture['emails'][role],'password':fixture['password'],'role':role}),encoding='utf-8')
  output=folder/f'game-player-{role}';output.mkdir(exist_ok=True)
  (output/f'validation-{role}.json').unlink(missing_ok=True)
  handle=(output/'console.log').open('w',encoding='utf-8');handles.append(handle)
  command=[str(client),'-batchmode','-nographics','-job-worker-count','2','--verify-multiplayer','--verify-vivox',
           '--multiplayer-fixture',str(auth),'--multiplayer-output',str(output),'-logFile',str(output/'player.log')]
  process=subprocess.Popen(command,cwd=root,stdout=handle,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
  processes.append(process)
 deadline=time.monotonic()+200
 while any(p.poll() is None for p in processes):
  if time.monotonic()>deadline:raise TimeoutError('Two-player live validation timeout; inspect logs')
  time.sleep(.5)
 reports=[json.loads((folder/f'game-player-{i}'/f'validation-{i}.json').read_text(encoding='utf-8')) for i in range(2)]
 passed=all(r['passed'] and r['udp'] and r['vivox'] and r['vivoxParticipants']>=2 for r in reports)
 result={'passed':passed,'liveVps':True,'audioCaptured':False,'physicalAndroidTested':False,'players':reports}
 (folder/'game-public-validation.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
 print(json.dumps(result,ensure_ascii=False),flush=True)
 if not passed:raise RuntimeError('Live game validation failed')
finally:
 for p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=5)
   except subprocess.TimeoutExpired:p.kill();p.wait()
 for handle in handles:handle.close()
 for auth in private.glob('game-player-*.json'):auth.unlink(missing_ok=True)
