import pathlib,json,shutil
root=pathlib.Path(r'C:\SistemaAdr\gta');out=root/'personagem_unity'
mapping={'Hips':' Pelvis','Spine':' Spine','Chest':' Spine1','Neck':' Neck','Head':' Head','LeftShoulder':'Bip01 L Clavicle','LeftUpperArm':' L UpperArm','LeftLowerArm':' L ForeArm','LeftHand':' L Hand','RightShoulder':'Bip01 R Clavicle','RightUpperArm':' R UpperArm','RightLowerArm':' R ForeArm','RightHand':' R Hand','LeftUpperLeg':' L Thigh','LeftLowerLeg':' L Calf','LeftFoot':' L Foot','LeftToes':' L Toe0','RightUpperLeg':' R Thigh','RightLowerLeg':' R Calf','RightFoot':' R Foot','RightToes':' R Toe0'}
(out/'humanoid_mapping.json').write_text(json.dumps(mapping,indent=2))
shutil.copyfile(root/'personagem/Autor-ByBeyby.txt',out/'Creditos-originais.txt')
(out/'LEIA-ME.txt').write_text('''PERSONAGEM BYBEYBY - CONVERSAO PARA O PROTOTIPO UNITY

ByBeyby.fbx: malha e esqueleto, sem animacoes.
Textures/: seis texturas PNG extraidas do TXD.
ByBeyby.fbm/: texturas referenciadas pelo exportador FBX; manter junto ao FBX.
ByBeyby.blend: fonte editavel, com texturas empacotadas.
preview.png: visualizacao da skin original convertida.
validation.json: inspecao e teste de deformacao no Blender.
fbx_roundtrip.json: verificacao de reimportacao do FBX.
humanoid_mapping.json: sugestao de mapeamento dos ossos para Unity (os espacos iniciais nos nomes sao originais).

Validado no Blender: 32 ossos, 24.930 vertices, 20.813 triangulos;
nenhum vertice sem peso, no maximo quatro influencias por vertice.
O teste de rotacao do antebraco movimentou a malha. Isso nao equivale a validar todos os movimentos.

Pendente: importar no Unity, configurar e validar Avatar Humanoid, ajustar T-pose,
conferir materiais no pipeline escolhido e adicionar Idle, Walk e Run.
A pose original tem os bracos inclinados. Nao foi alterada a pose de repouso.
A hierarquia de objetos foi ajustada para o rig ser pai da malha, preservando as matrizes mundiais.
Os pes foram posicionados no nivel do chao. Altura aproximada: 1,82 m.
Nenhuma animacao foi incluida no material enviado pelo amigo.

Para importar: copiar ByBeyby.fbx, ByBeyby.fbm e Textures para uma pasta em Assets.
Comecar com Rig Generic para inspecao; configurar Humanoid para retargeting depois.
A compatibilidade Humanoid ainda nao foi confirmada pelo Unity.
Os arquivos originais da pasta personagem foram preservados.
Creditos originais mantidos em Creditos-originais.txt.
''',encoding='utf-8')
# Keep the conversion script reproducible with the hierarchy fix.
p=root/'tools/convert_skin.py';s=p.read_text();needle='# Render a simple review image; staging is excluded from the delivered model.'
# A separate fix script follows convert_skin.py, before verify_fbx.py.
(root/'tools/CONVERSAO.txt').write_text('Ordem de reproducao com o Blender portatil:\n1. inspect_skin.py\n2. convert_skin.py\n3. fix_hierarchy.py\n4. verify_fbx.py\n',encoding='utf-8')
