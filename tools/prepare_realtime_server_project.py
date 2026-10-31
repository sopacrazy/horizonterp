"""Minimal dedicated relay project; imports no game map or client assets."""
from pathlib import Path
import shutil,json
root=Path(__file__).resolve().parents[1];source=root/'TestePersonagem';target=root/'tools/realtime-server-project'
(target/'Assets/Editor').mkdir(parents=True,exist_ok=True)
(target/'Assets/Runtime').mkdir(exist_ok=True)
for name in ['UtpWire.cs','RealtimeTicket.cs','UtpRoomServer.cs','RealtimeServerHost.cs']:
 shutil.copy2(source/'Assets/Scripts/Realtime'/name,target/'Assets/Runtime'/name)
shutil.copy2(root/'Backend/realtime/BuildRelay.cs',target/'Assets/Editor/BuildRelay.cs')
(target/'Packages').mkdir(exist_ok=True);(target/'ProjectSettings').mkdir(exist_ok=True)
shutil.copy2(source/'ProjectSettings/ProjectVersion.txt',target/'ProjectSettings/ProjectVersion.txt')
(target/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.transport':'6.6.0','com.unity.modules.jsonserialize':'1.0.0'}},indent=2))
print('MINIMAL_RELAY_PROJECT_READY')
