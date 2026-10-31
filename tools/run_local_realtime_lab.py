"""Run a fully isolated two-player laboratory (temporary memory DB, localhost API and DTLS).
Use --verify for automated game-client tests. Close both game windows to stop manual mode.
"""
from pathlib import Path
import argparse,subprocess,os,time,ssl,urllib.request,json,sys
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--verify',action='store_true');args=parser.parse_args()
folder=root/'tools/realtime-integration/lab';folder.mkdir(parents=True,exist_ok=True)
credentials=root/'tools/realtime-integration/credentials'
client=root/'TestePersonagem/Builds/WindowsRelease/Horizonte.exe'
server=root/'TestePersonagem/Builds/RealtimeServer/HbrpRealtimeServer.exe'
assert client.is_file() and server.is_file(),'Build clients/server with RealtimeIntegrationVerification.BuildAllLocal first'
if not (credentials/'localhost.pem').exists():subprocess.run([sys.executable,str(root/'tools/prepare_local_realtime.py')],cwd=root,check=True)
env=os.environ.copy();env.pop('DATABASE_URL',None);env['HBRP_REALTIME_SECRET']=(credentials/'test-secret.txt').read_text().strip()
processes=[];files=[]
def launch(command,name,environment=None,hidden=True):
    output=(folder/(name+'.log')).open('w',encoding='utf-8');files.append(output)
    kwargs={'cwd':root,'env':environment,'stdout':output,'stderr':subprocess.STDOUT}
    if os.name=='nt' and hidden:kwargs['creationflags']=subprocess.CREATE_NO_WINDOW
    process=subprocess.Popen([str(x) for x in command],**kwargs);processes.append(process);return process
try:
    api=launch(['node',root/'Backend/api/local-realtime-lab.mjs'],'api')
    context=ssl.create_default_context(cafile=str(credentials/'localhost-ca.pem'))
    deadline=time.monotonic()+45
    while True:
        if api.poll() is not None:raise RuntimeError('Local API failed; inspect api.log')
        try:
            with urllib.request.urlopen('https://127.0.0.1:18443/health',context=context,timeout=2) as response:
                if json.load(response)['status']=='ok':break
        except (OSError,ValueError):pass
        if time.monotonic()>deadline:raise TimeoutError('Local API startup timeout')
        time.sleep(.25)
    relay=launch([server,'-batchmode','-nographics','--hbrp-udp-server-config',credentials/'server.json','-logFile',folder/'udp-server.log'],'udp-console',env)
    players=[]
    for role in range(2):
        output=folder/f'player-{role}-result';output.mkdir(exist_ok=True)
        (output/f'validation-{role}.json').unlink(missing_ok=True)
        command=[client,'--hbrp-local-lab-config',folder/'public-config.json','--hbrp-local-player',folder/f'player-{role}.json',
                 '-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',folder/f'player-{role}.log']
        if args.verify:command+=['-batchmode','-nographics','--verify-multiplayer','--multiplayer-fixture',folder/f'player-{role}.json','--multiplayer-output',output]
        players.append(launch(command,f'player-{role}-console',hidden=args.verify))
    print('LOCAL_LAB_RUNNING players=2 api=localhost udp=localhost production=untouched',flush=True)
    deadline=time.monotonic()+(200 if args.verify else 86400)
    while any(p.poll() is None for p in players):
        if api.poll() is not None or relay.poll() is not None:raise RuntimeError('Local server stopped unexpectedly')
        if time.monotonic()>deadline:raise TimeoutError('Local clients timed out')
        time.sleep(.5)
    if args.verify:
        reports=[json.loads((folder/f'player-{i}-result'/f'validation-{i}.json').read_text(encoding='utf-8')) for i in range(2)]
        report={'passed':all(r['passed'] and r['udp'] and r['udpSnapshots']>20 for r in reports),'productionChanged':False,'database':'temporary memory','players':reports}
        (root/'tools/realtime-integration/game-validation.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
        print(json.dumps(report,indent=2,ensure_ascii=False),flush=True)
        if not report['passed']:raise RuntimeError('Local game client checks failed')
finally:
    for process in reversed(processes):
        if process.poll() is None:
            process.terminate()
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:process.kill();process.wait()
    for output in files:output.close()
    for fixture in folder.glob('player-*.json'):fixture.unlink()
