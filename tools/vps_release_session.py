"""Interactive SSH bridge for the authorized HBRP host; credentials stay in memory."""
import sys,json,getpass,pathlib
sys.path.insert(0,str(pathlib.Path(__file__).parent/'ssh_libs'))
import paramiko
sys.stdout.reconfigure(encoding='utf-8')
client=paramiko.SSHClient()
client.load_host_keys(str(pathlib.Path(__file__).parent/'release-071/known_hosts'))
client.set_missing_host_key_policy(paramiko.RejectPolicy())
password=getpass.getpass('VPS password: ')
client.connect('187.41.106.219',username='root',password=password,timeout=20,allow_agent=False,look_for_keys=False)
password=None
client.get_transport().set_keepalive(20)
print('SSH_CONNECTED',flush=True)
while True:
 try: line=input()
 except EOFError: break
 try:
  request=json.loads(line)
  if request.get('exit'): break
  if 'upload' in request:
   with client.open_sftp() as sftp:sftp.put(request['upload'],request['destination'])
   print('UPLOAD_OK',flush=True);continue
  if 'download' in request:
   with client.open_sftp() as sftp:sftp.get(request['download'],request['destination'])
   print('DOWNLOAD_OK',flush=True);continue
  stdin,stdout,stderr=client.exec_command(request['command'],timeout=request.get('timeout',120))
  if 'stdin' in request: stdin.write(request['stdin']);stdin.flush()
  stdin.channel.shutdown_write()
  output=stdout.read().decode(errors='replace');error=stderr.read().decode(errors='replace')
  print(json.dumps({'exit':stdout.channel.recv_exit_status(),'stdout':output,'stderr':error},ensure_ascii=False),flush=True)
 except Exception as error: print(json.dumps({'error':str(error)}),flush=True)
client.close()
