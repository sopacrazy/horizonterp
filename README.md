# Horizonte Brasil RP

Protótipo de jogo em terceira pessoa para Windows e Android, desenvolvido em Unity.

Versão 0.7.0 (Android código 19). Downloads e histórico em [Releases](https://github.com/sopacrazy/horizonterp/releases); detalhes em [notas da 0.7.0](releases/notes-0.7.0.md).

## Estrutura

- `TestePersonagem/`: projeto Unity, cenas, scripts, animações e recursos visuais.
- `Backend/api/`: API Node.js de contas, personagens, persistência, sala de teste e chat.
- `Backend/database/`: migrações PostgreSQL, backup e verificações.
- `personagem/`: arquivos originais do personagem, animações e artes.
- `personagem_unity/`: arquivos de conversão do personagem e créditos.
- `tools/`: scripts de conversão e verificação; programas e SDKs instalados não são versionados.

## Abrir no Unity

1. Instale o Unity Editor **6000.6.7f1** pelo Unity Hub.
2. No Hub, adicione a pasta `TestePersonagem` como projeto existente.
3. Aguarde a importação dos recursos e pacotes.
4. Abra as cenas indicadas em `TestePersonagem/Assets/Editor/GameUpdateSetup.cs`.

Para Android, instale também Android Build Support, SDK, NDK e OpenJDK compatíveis com o editor. A configuração de build está em `Assets/Editor/AndroidSetup.cs`; confira os caminhos locais das ferramentas antes de compilar em outro computador.

No Windows, `AbrirProjeto.bat` abre o projeto na instalação padrão do editor. `Jogar.bat` executa uma build local já gerada; builds não estão incluídas neste repositório.

## Funcionalidades do protótipo

- Personagem com animações de parado, caminhada, corrida e pulo; câmera em terceira pessoa.
- HUD, controles por toque, introdução e lobby de criação de personagem.
- Cidade oficial na cena `TestePersonagem`, com cenário recuperado, materiais reconstruídos e colisões revisadas. O bairro anterior fica em backup local durante a integração.
- Cadastro e login, separação entre conta e personagem e salvamento de progresso.
- Sala experimental com dez vagas, sincronização de movimento e chat. Bots automáticos desativados.
- Tipos de conta e nível administrativo, painel de jogadores e guia `/helpadmin`. Alterações administrativas respeitam a hierarquia e são auditadas no servidor.
- Serviço policial, procurados, identificação, algemas, condução, viatura, multas e canais de comunicação; voz por proximidade e rádio por canais.
- Comandos públicos no `/ajuda`, grito por proximidade, sussurro privado e relatórios persistidos. Recursos futuros são sinalizados como pendentes.
- Mapa ampliado com arrasto; administradores podem marcar e confirmar um destino para teleporte com validação do chão.
- Modo de velocidade para administradores: no painel ADMIN, toque em **Velocidade** e escolha x2, x4, x6, x8, x10 ou x12. A opção x1 volta à velocidade normal; o modo rápido é desativado ao perder o acesso de administrador.
- Chat com eco local imediato e destaque verde para comandos administrativos.
- A partir da 0.5.0: versão no HUD/loading e atualização Android pelo GitHub Releases, com confirmação de instalação pelo Android.

A rede usa Unity Transport por UDP/DTLS a 20 Hz quando o servidor dedicado está disponível, com alternativa por HTTPS. Contas, permissões, comandos e persistência continuam na API. A posição ainda é simulada pelo cliente; a suavização não elimina a latência da conexão. A integração Vivox usa tokens emitidos pelo backend, sem chave no aplicativo. Consulte [rede e voz](Backend/realtime/README.md).

`VerMapaGTA.bat` abre uma prévia independente da cidade no Windows, sem conta ou multiplayer.
`Jogar.bat` abre `TestePersonagem/Builds/WindowsRelease/Horizonte.exe`, com entrada normal e login.
`JogarRedeLocal.bat` abre um laboratório de dois jogadores com contas temporárias e banco em memória.
A cidade atual é a integração San Andreas em `Assets/Scenes/TestePersonagem.unity`,
com 563 setores carregados por proximidade. Android seleciona 146 setores de Los Santos
e usa as cenas em `Assets/Scenes/AndroidLosSantos`, preservando o mapa completo no projeto.
`GameUpdateSetup.Scenes` escolhe o perfil conforme a plataforma. `ReleaseBuild.Windows`
e `ReleaseBuild.Android` geram os pacotes; `tools/prepare_android_los_santos_project.py`
prepara uma cópia Android isolada para reduzir a importação de recursos.
Os reparos de Los Santos ficam em `Assets/GTASanAndreasModFull/GroundRepairs`
e `CollisionRepairs`, com malhas editáveis e catálogos reaplicados ao regenerar
os setores. As ferramentas `LosSantos*` do Editor verificam chão, colisões e
objetos duplicados; os backups, relatórios e imagens locais ficam em
`tools/los-santos-repair`. `tools/sync_los_santos_repairs.py` sincroniza os
arquivos verificados com a cópia Android existente, preservando backup.
As bases e guias usam `surfaceKind: foundation` no catálogo, com colisão
verificada por raios alinhados às faces. Os pisos mantêm o teste de contato
vertical. A recuperação de terrenos distingue as alturas das ruas, plataformas
e canais; a cobertura projetada de um telhado não remove o chão abaixo dele.
Uma patrulha física de Los Santos usa `CharacterController.Move` com as dimensões
do jogador, a 1,65 e 54 m/s. `LosSantosPatrolBatch.RunAfter` percorre a malha de
rotas e registra reinícios após obstáculos; `LosSantosPatrolRecheck.Run` confronta
os achados com as faces visuais e a colisão local. Relatórios, percursos e imagens
na altura do jogador ficam em `tools/los-santos-patrol`. Os testes distinguem
muros visíveis, movimento no ar em descidas e frestas de sondagem de quedas reais.
Para testar reparos no Editor, pare o Play antes de recarregar a cena
`Assets/Scenes/TestePersonagem.unity`; a cena `Entrada` carrega essa cidade.
Executáveis e APKs já gerados exigem uma nova build para incluir os reparos.
Áreas rurais e San Fierro ainda têm falhas visuais; NPCs e missões precisam de revisão
de posição. O tutorial inicial está desativado nos testes do mapa novo.

`VerBairro.bat` é o atalho legado de revisão do bairro anterior, com personagem de teste e
salvamento apenas em memória. Essa prévia não acessa a API da VPS e não é um modo
offline para contas reais. `Jogar.bat` continua abrindo a entrada normal com login.
O comando **Horizonte > Montar bairro com praca central** permanece como ferramenta
histórica e substitui o cenário; não faz parte da geração da cidade oficial.
O backend mantém o mundo e os saves existentes; a ampliação não exige migração SQL.

As versões são definidas em `TestePersonagem/Assets/Resources/GameVersion.json`. Veja [como publicar atualizações Android](releases/README.md). APKs anteriores à 0.5.0 precisam de uma instalação manual para receber o atualizador.

## Backend e testes

Consulte [a documentação da API](Backend/api/README.md) e [a documentação do banco](Backend/README.md). As migrações estão numeradas de 001 a 011. A API ativa fica na VPS Brasil; a ponte do endereço antigo mantém a compatibilidade dos aplicativos instalados. Veja [o estado da migração](Backend/migration/KVM1_STATUS.md). Credenciais são configuradas exclusivamente no ambiente do servidor e não devem ser adicionadas ao projeto Unity ou ao Git.

Para executar os testes locais da API, com Node.js instalado:

```sh
cd Backend/api
npm ci
npm test
```

Os testes locais usam PostgreSQL em memória. Verificadores de integração e rotinas de implantação exigem um ambiente próprio configurado; não devem ser executados contra uma VPS sem conferir seus parâmetros.

O certificado público e o pin usados pelo cliente não são credenciais privadas. Configurações de ambiente estão em `TestePersonagem/Assets/Resources/ApiConfig.json`. Nunca publique a chave privada TLS, senhas, tokens, dumps do banco ou arquivos de contas temporárias.

## Recursos e créditos

Os créditos originais do personagem foram preservados nos diretórios de recursos. Assets de terceiros continuam sujeitos às respectivas licenças; sua presença neste repositório não concede licença adicional de redistribuição.
