import sys
from io_scene_fbx import parse_fbx
r,v=parse_fbx.parse(r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.fbx')
for e in r.elems:
 print(e.id,len(e.elems))
 if e.id in (b'Objects',b'Connections'):
  for c in e.elems[:45]:print(' ',c.id,c.props[:3])
