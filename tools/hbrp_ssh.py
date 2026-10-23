import sys,json,getpass,base64,hashlib,pathlib
sys.path.insert(0,str(pathlib.Path(__file__).parent/'ssh_libs'))
import paramiko
sys.stdout.reconfigure(encoding='utf-8')
client=paramiko.SSHClient()
known=pathlib.Path.home()/'.ssh'/'known_hosts'
if known.exists():client.load_host_keys(str(known))
class FirstUse(paramiko.MissingHostKeyPolicy):
 def missing_host_key(self,client,hostname,key):
  print('SSH_HOST_KEY SHA256:'+base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode().rstrip('='),flush=True)
client.set_missing_host_key_policy(FirstUse())
password=getpass.getpass('VPS password: ')
client.connect('70.38.101.147',username='root',password=password,timeout=20,allow_agent=False,look_for_keys=False)
client.get_transport().set_keepalive(20)
password=None
print('SSH_CONNECTED',flush=True)
while True:
 try: line=input()
 except EOFError:break
 try:
  request=json.loads(line)
  if request.get('exit'):break
  if 'upload' in request:
   sftp=client.open_sftp();sftp.put(request['upload'],request['destination']);sftp.close();print('UPLOAD_OK',flush=True);continue
  if 'download' in request:
   sftp=client.open_sftp();sftp.get(request['download'],request['destination']);sftp.close();print('DOWNLOAD_OK',flush=True);continue
  stdin,stdout,stderr=client.exec_command(request['command'],timeout=request.get('timeout',120))
  if 'stdin' in request:stdin.write(request['stdin']);stdin.flush()
  stdin.channel.shutdown_write()
  output=stdout.read().decode(errors='replace');error=stderr.read().decode(errors='replace')
  print(json.dumps({'exit':stdout.channel.recv_exit_status(),'stdout':output,'stderr':error},ensure_ascii=False),flush=True)
 except Exception as e:print(json.dumps({'error':str(e)}),flush=True)
client.close()
