# Recuperar o Horizonte Brasil RP após formatar

Backup preparado em 4 de novembro de 2026. Não é uma nova versão do jogo e não altera o atualizador Android.

## Arquivos no GitHub

Repositório: https://github.com/sopacrazy/horizonterp

Snapshot completo de fontes e recursos: https://github.com/sopacrazy/horizonterp/releases/tag/backup-2026-11-04

O Git contém o código atualizado. A release de backup contém ZIPs com os recursos completos, inclusive arquivos que antes não estavam versionados: mapa completo, setores, reparos, personagens, texturas, animações, projetos de teste, fontes de conversão e relatórios. Use o `manifest.json` da release para conferir tamanho, SHA-256 e quantidade de arquivos de cada ZIP.

1. Instale Git, Unity Hub e Unity **6000.6.7f1**. Para Android, instale Android Build Support, SDK/NDK e OpenJDK pelo Hub.
2. Clone o repositório, preferencialmente novamente em `C:\SistemaAdr\gta` para manter os caminhos dos scripts.
3. Baixe todos os ZIPs da release de backup e o manifesto. Confira os SHA-256 com `Get-FileHash -Algorithm SHA256`.
4. Extraia os ZIPs na raiz `C:\SistemaAdr\gta`. Cada ZIP já contém a pasta correspondente; não extraia dentro de uma pasta com o mesmo nome.

   Alternativa com PowerShell 7: depois de baixar os arquivos para uma pasta, execute `pwsh -File tools/Restore-FormatBackup.ps1 -BackupFolder D:\BackupBaixado -Destination C:\SistemaAdr\gta`. O script confere tamanho e SHA-256 antes de extrair.

   Se houver arquivos `.zip.part001`, `.zip.part002` etc., baixe todas as partes para a mesma pasta e use esse script: ele recompõe o ZIP antes da extração. As partes isoladas não são ZIPs independentes.
5. No Hub, adicione a pasta **TestePersonagem**, não `Assets` nem `Scenes`. Abra `Assets/Scenes/Entrada.unity` para o fluxo de login/jogo.
6. Aguarde a primeira importação. `Library`, `Temp`, `Logs`, `obj`, `node_modules`, builds e ferramentas instaláveis não fazem parte do snapshot e serão recriados.
7. Para o backend, instale Node.js 22 ou superior e execute `npm ci` na pasta `Backend/api`. Não execute migrações ou provisionamento sobre a VPS existente apenas para restaurar este computador.
8. Python e Blender precisam ser reinstalados se quiser converter personagens; o snapshot de ferramentas contém os scripts e fontes customizadas, não os SDKs/binários instaláveis.

## Backup privado obrigatório no HD

Pasta preparada: `C:\Users\Adriano\Desktop\backup\Horizonte-privado-2026-11-04`.

**Copie essa pasta para o HD antes de formatar. Ela não está no GitHub público.** O manifesto privado lista os arquivos copiados e hashes. Ela contém a chave de assinatura original do Android, configurações locais do Vivox, certificados/chaves locais de teste e backups locais do banco. Preserve o keystore original para continuar atualizando os aplicativos já instalados.

Os dados ativos dos jogadores e as configurações de produção continuam na VPS. Os dumps locais têm datas próprias e não devem substituir o banco atual sem análise. Senhas de contas externas, acesso à Hostinger, GitHub e Unity devem continuar acessíveis por sua conta/gerenciador de senhas; não estão incluídas neste repositório.

## Estado do trabalho preservado

Inclui as alterações locais de economia criminal e permissões administrativas. A skin Wuzimu permanece como prévia experimental; a tentativa cancelada de transferir somente sua textura não foi integrada ao personagem principal. Arquivos experimentais de conversão são preservados para referência. Este backup não declara resolvidos os problemas visuais da skin e não gera Windows/APK.

## Antes de formatar

- Confirme que os assets da release estão disponíveis e que o upload terminou.
- Copie o backup privado para o HD e confira os arquivos no próprio HD.
- Mantenha acesso às contas GitHub, Unity e Hostinger.
