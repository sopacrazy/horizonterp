# Personagem Wuzimu

Modelo convertido de `personagem/wuzimu.dff` com texturas de `personagem/wuzimu.txd`.

- 32 ossos, 17.297 vértices; nenhum vértice sem peso.
- Materiais reconstruídos usando as divisões de materiais do DFF; PNGs extraídas do TXD.
- FBX em `TestePersonagem/Assets/Resources/Characters/Wuzimu/Wuzimu.fbx`.
- Avatar Humanoid válido. Caminhada atual transferida e amostrada no esqueleto: `WUZIMU_WALK_RETARGET_OK`, diferença angular de 4,83° entre amostras.
- Controlador e overrides atuais são preservados ao trocar, reutilizando parado, andar, correr, pular e os estados existentes. A validação automatizada amostrou a caminhada; as demais ações ainda devem ser avaliadas no Play.

## Comandos para testar no Play

- `/wuzimu` ou `/skin wuzimu`: troca o visual local.
- `/skin padrao`: restaura o masculino/feminino correspondente ao personagem salvo.

Trocar a pé e sem arma equipada. O comando não altera sexo, identidade nem modelo salvo no banco. É uma prévia local; outros jogadores ainda não recebem esta escolha de skin. Não foi gerada build nem publicado APK.

Prévia com textura: `tools/wuzimu-preview.png`.
Validação isolada: `tools/wuzimu-isolated-animations-final.log`.

## Correção da orientação durante animações

A gravação enviada mostrou o personagem deitado ao se movimentar. O importador preservava uma rotação de 90 graus na raiz do FBX. A verificação anterior de movimento da perna não cobria essa postura; o teste foi ampliado.

- Rotação e escala da conversão DFF aplicadas à pose de referência.
- Conversão de eixos ativada no ModelImporter, preservando o GUID do modelo no projeto principal.
- Raiz visual zerada ao instalar a skin e depois do Rebind/Update inicial. Não há correção acumulada a cada quadro.
- Play isolado com as animações atuais: parado, caminhada, corrida e pulo. Todos permaneceram verticais com raiz (0,0,0); menor alinhamento vertical do tronco: 0,9669.
- Evidência: `tools/wuzimu-review/runtime-all.log` e `tools/wuzimu-review/runtime-validation.txt`.
- Não foi gerada build Windows/APK. Reiniciar o Play no projeto principal e usar `/wuzimu` para avaliar o resultado com o mapa.

## Transferência ao esqueleto padrão (atual)

- A malha texturizada foi transferida para os 101 ossos do GangsterMusculo. Os grupos e pesos foram remapeados, com ajuste da pose e orientação da malha. Não é apenas uma troca de Avatar no Unity.
- Conversão reproduzível: `tools/rig_wuzimu_standard.py`; fonte editável: `tools/wuzimu-review/WuzimuStandard.blend`.
- Importação Humanoid usa o mapeamento padrão, sem o acessório Raybans e sem referências de raiz do FBX original.
- Teste com o mesmo GangsterPlayerVisual, Locomocao.controller e overrides reais: parado/caminhada/corrida passaram; altura inicial 1,641501 m para referência 1,641486 m. No idle a mão ficou em 0,8953 m e o ombro em 1,3374 m, cobrindo o defeito anterior de braços levantados. Evidência `tools/wuzimu-review/standard-final.log`.
- Prévia da malha inspecionada: `tools/wuzimu-review/standard-preview.png`. Arquivo e meta validados integrados ao projeto principal preservando GUID. Backup anterior em `tools/wuzimu-review/backup-before-standard-rig`.
- Não houve build ou publicação; ainda requer avaliação visual no Play do mapa principal pelo usuário. Combate, arma e veículo não foram validados nesta transferência.

## Correção do tamanho

### Reprodução com controlador completo

O teste simplificado anterior não cobria a troca real. `ExactSkinSwapProbe` executou o mesmo `GangsterPlayerVisual` com `Locomocao.controller` e os overrides reais e reproduziu altura de 7,45–7,58 m. A pose importada antes da avaliação Humanoid resultava em uma escala incorreta de 3,77.

A troca agora mede novamente a geometria depois de Rebind/Play/Update do controlador real. O teste corrigido mediu 1,6414–1,6715 m (referência 1,6415 m), escala estável 0,83. Evidência: `tools/wuzimu-review/exact-swap-fixed.log`, `EXACT_SWAP_PASS`. Compilação estática do projeto principal passou. A avaliação visual final no mapa fica para o próximo Play do usuário; nenhuma build foi gerada.

A gravação seguinte mostrou escala exagerada. O ramo de troca de skin ainda usava Renderer.bounds, cuja caixa de descarte estava desatualizada após a correção de eixos. Foi substituída pela medição dos vértices realmente deformados pelo esqueleto, usando CharacterGeometry e CharacterSkinSizing.

- A altura e o nível dos pés usam a referência masculina já estabelecida no jogo.
- Escala e posição calculadas são preservadas após Rebind/Update inicial, sem redimensionamento por quadro ou por estado de animação.
- O FBX do Wuzimu tem Read/Write habilitado para essa medição única. Outros modelos/controladores não foram alterados.
- Teste em Play isolado com o mesmo modelo masculino de referência: altura geométrica de referência 1,604035 m; Wuzimu normalizado 1,604035 m. As caixas de renderização incluem margens e não representam essa altura física.
- Parado, caminhada, corrida e pulo passaram no teste de postura/limites de escala. Evidência: tools/wuzimu-review/size-check.log.
- Comandos permanecem /wuzimu e /skin padrao. Sem build; testar reiniciando o Play.
