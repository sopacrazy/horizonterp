# HBRP API 0.3.0

Serviço exclusivo `hbrp-api`, usuário Linux `hbrp-api`, pasta `/srv/hbrp/api`.
API HTTPS em `https://70.38.101.147:8443`, PostgreSQL continua apenas local na porta 55432.
Foi adicionada somente a regra de firewall da porta 8443. Nginx e sites existentes não foram editados/reiniciados.

## Rotas

| Método | Caminho | Função |
|---|---|---|
| GET | `/health` | Estado da API e conexão ao banco |
| POST | `/v1/auth/register` | Somente e-mail e senha; cria conta e sessão, sem personagem |
| POST | `/v1/auth/login` | E-mail e senha; sessão de até sete dias |
| POST | `/v1/auth/logout` | Revoga a sessão atual |
| POST | `/v1/characters` | Cria o personagem da conta autenticada, uma única vez |
| GET | `/v1/player` | Estado do personagem da conta autenticada |
| PUT | `/v1/player/checkpoint` | Posição, rotação e câmera com versão e UUID de idempotência |
| PUT | `/v1/player/profile` | Nome e sobrenome do personagem, atualizados juntos |

Autenticação: `Authorization: Bearer <token>`. Nenhuma rota aceita accountId/characterId
do cliente para escolher o dono dos dados. Novo login encerra sessões anteriores.

### Contrato de conta e personagem

`POST /v1/auth/register` recebe apenas `{email,password}` e retorna HTTP 201
com `{token,state:null,needsCharacter:true}`. Campos adicionais, incluindo `name`,
são rejeitados. O cadastro não cria personagem nem suas carteiras.

`POST /v1/auth/login` retorna HTTP 200 com `{token,state,needsCharacter}`.
Conta sem personagem ativo recebe `state:null,needsCharacter:true`;
conta com personagem recebe o estado salvo e `needsCharacter:false`.
`GET /v1/player` usa o mesmo envelope, sem token no corpo.

`POST /v1/characters`, com Bearer token, recebe
`{firstName,lastName,gender}`. Cada nome tem de 2 a 24 pontos Unicode após
normalização NFC e remoção de espaços excedentes. São aceitas letras e marcas
diacríticas, espaços, hífens e apóstrofos internos. U+2019 é convertido para
apóstrofo ASCII. Números, `_`, símbolos, campos protegidos e separadores repetidos
são rejeitados. `gender` aceita somente `female` ou `male`.
O nome completo é `firstName + " " + lastName`, até 49 caracteres, único sem
diferenciar maiúsculas e minúsculas.

A criação retorna HTTP 201 com `{state,needsCharacter:false}`. Personagem,
atributos, posição, aparência, inventário e carteiras são criados numa transação.
O token não muda. Uma segunda criação recebe HTTP 409 `CHARACTER_EXISTS`.
Mundo desabilitado recebe HTTP 503 `WORLD_UNAVAILABLE`, sem dados parciais.
Checkpoint e renomeação sem personagem recebem HTTP 409 `CHARACTER_REQUIRED`.
Dados incompletos de um personagem existente recebem `CHARACTER_INCOMPLETE`;
essa situação não é apresentada como criação pendente.

O estado acrescenta `firstName`, `lastName` e `gender`. Registros antigos mantêm
esses campos nulos, preservando nome, modelo, saldos, posição e progresso.
Feminino e masculino usam por enquanto o mesmo `model:"bybeyby"`, o único
modelo 3D importado. O gênero escolhido fica salvo separadamente.

`PUT /v1/player/profile` recebe preferencialmente `{firstName,lastName}`.
O formato anterior `{name}` continua aceito quando contém nome e sobrenome
humanos: o primeiro token vira `firstName` e o restante `lastName`.
Os formatos não podem ser misturados. A renomeação preserva o gênero.

Senha: scrypt N=131072, r=8, p=1, salt aleatório, comparação em tempo constante.
Tokens: 32 bytes aleatórios, apenas SHA-256 persistido no banco. Não são escritos nos logs.
Limites por IP para login/cadastro, limite de corpo de 4 KiB e consultas parametrizadas.
Serviço limitado a 512 MB e uma CPU; pool de seis conexões. Uma conta/um personagem ativo nesta versão.

Dinheiro, XP, nível, vida, fome, sede e energia são valores do servidor. O checkpoint
rejeita campos extras e posições fora dos limites configurados do bairro. Isso é persistência
para o protótipo; não substitui simulação autoritativa de movimentação no multiplayer.
Não há recuperação/verificação de e-mail nem Google/Apple nesta entrega.

## Limites da cidade oficial

`world-bounds.mjs` é a fonte única e imutável de limites jogáveis do backend.
O mundo existente continua `centro_teste`; não há novo mundo, migração SQL,
reset de personagens ou alteração das posições salvas. O spawn legado
`(0,-0.1,0)` e o spawn atual do cliente `(0,0.08,0)` ficam dentro da área.
A configuração local preparada para a cidade oficial é:

```json
{"centro_teste":{"minX":-1000,"maxX":1000,"minY":-80,"maxY":100,"minZ":-1000,"maxZ":1000}}
```

Todos os limites são inclusivos, em metros Unity. Coordenadas precisam ser
números finitos. `getWorldBounds(world)` devolve o objeto congelado ou `null`;
`isPositionWithinWorld(world,{x,y,z})` valida a mesma regra usada pelo checkpoint
e pelo sync multiplayer. O mundo vem do personagem autenticado no banco;
enviar `world`, `characterId` ou campos financeiros no pacote continua proibido.
Mundo sem configuração recebe `WORLD_UNAVAILABLE`; posição fora da área é
rejeitada sem modificar o save. Rotação, câmera, velocidade, fase do pulo,
versão de salvamento e UUID de idempotência mantêm as validações anteriores.

O cliente configura `halfExtent=1001`, resultando em X/Z autorizados de ±1000 m,
e Y de -80 a 100 m. Os limites administrativos abrangem o cenário recuperado;
não garantem piso ou circulação em todo ponto dentro desse volume. O contrato
de requisições e respostas não mudou. Não há dependência do backend em `Assets`.

**A ampliação foi implantada e validada por HTTPS na preparação da versão 0.5.7.** O roteiro
atual é `PROMPT_VPS_CIDADE_OFICIAL.txt`; `PROMPT_VPS_BAIRRO.txt` descreve apenas
os limites históricos da praça e não deve orientar esta atualização. Confirmar
os imports de `world-bounds.mjs` em `server.mjs` e `multiplayer.mjs`, preservar o
conjunto compatível de módulos e publicar a configuração de limites atualizada.
O health permanece `0.3.0` e sozinho não confirma a implantação. Nenhuma nova
dependência, permissão administrativa, regra de chat ou capacidade foi adicionada.

`verify_api.py` foi atualizado para testar interior e seis faces da cidade,
e sua execução HTTPS na VPS passou, preservando os registros anteriores. Ele usa e limpa somente suas
próprias fixtures temporárias. Os testes locais pertinentes estão em
`test/world-bounds.test.mjs`, `test/multiplayer.test.mjs` e
`test/characters.test.mjs`; eles verificam bordas inclusivas, rejeição 0,001 m
além de cada face, validação finita, preservação de identidade, saves e finanças.

## Certificado e configuração

Certificado próprio do HBRP, com SAN para o IP. O Unity verifica a impressão SHA-256
exata e a validade do certificado. Não há callback que aceite qualquer certificado.
O certificado público e sua impressão não são segredos. A chave privada fica somente
na VPS, em `/srv/hbrp/api/tls/server.key`, acessível ao serviço HBRP.
`Assets/Resources/ApiConfig.json` contém apenas URL e impressão pública.
Antes de expirar/rotacionar esse certificado de teste, atualizar a configuração do cliente.
Para publicação, planejar domínio/certificado público e processo de renovação apropriado.

Credenciais do banco: `/srv/hbrp/credentials/api.env`, root 0600, carregadas pelo systemd.
Nunca copiar esse arquivo nem a chave privada para o Unity ou repositório.

## Unity

Entrada de pelo menos dez segundos -> cadastro/login -> criação ou lobby do personagem salvo -> cidade.
Apenas o último e-mail é lembrado no aparelho. Senha e token não são persistidos localmente.
Login é solicitado ao reabrir o jogo. Dados vêm do servidor, incluindo UID do HUD.
Autosave a cada 15 segundos, ao perder foco/pausar e no fechamento normal do PC.
Menu > Salvar e sair aguarda confirmação antes de retornar ao login.
Se o sistema operacional encerrar o app à força, vale o último checkpoint confirmado;
não é possível garantir envio de rede depois que o processo foi encerrado.
Falhas de rede preservam o pedido de salvamento para repetição idempotente.

## Operação

- `systemctl status hbrp-api` e `journalctl -u hbrp-api` para diagnóstico.
- Reiniciar somente `hbrp-api` após atualizar seu código.
- Migrações da API: `database/003_api.sql` e `database/004_character_creation.sql`.
- `verify_api.py`: integração HTTPS, autenticação, persistência, isolamento e rejeição de alterações protegidas.
- `tools/account-review`: testes do Unity, incluindo fechamento e nova execução.
- A porta 55432 continua sem exposição pública.

## Testes locais

Na pasta `Backend/api`, `npm ci` e `npm test` executam os testes de nomes e o
fluxo HTTP real contra PostgreSQL 18.6 em memória (PGlite, dependência somente de
desenvolvimento), sem acesso à VPS e sem credenciais. As migrações 001–004 são
aplicadas, incluindo uma fixture criada antes de 004. O papel da API é
`hbrp_app`, com suas permissões restritas. Há verificação de cadastro pendente,
gêneros, Unicode, isolamento, duplicidade, rollback tardio, saves e legado.
O PGlite possui uma conexão: a concorrência HTTP local prova a exclusividade,
mas o bloqueio entre conexões PostgreSQL 16 deve ser conferido pelo
`verify_api.py` no host HBRP antes de concluir o deploy.

## Atualização da instalação existente

Não reexecute `database/bootstrap_vps.py` e não recrie banco, papéis ou credenciais.
Para esta atualização basta enviar `api/server.mjs`, `api/verify_api.py` e
`database/004_character_creation.sql` aos caminhos correspondentes em `/srv/hbrp`.
`package.json` e `package-lock.json` podem acompanhar para registrar a versão;
`npm ci --omit=dev --ignore-scripts` instala apenas a dependência de produção `pg`.
Não enviar `node_modules`, backups, credenciais nem a chave TLS privada.

Antes, executar o backup HBRP existente e registrar as versões das migrações e
uma fotografia dos IDs/contagens dos dados existentes. Aplicar a migração 004
somente se a versão 4 não constar em `hbrp.schema_migrations`, com
`psql -X -p 55432 -d hbrp -v ON_ERROR_STOP=1` como administrador PostgreSQL local.
Ela é aditiva e não contém atualizações ou remoções dos saves existentes.
Reiniciar somente `hbrp-api`. `/health` deve retornar `version:"0.3.0"`.
Executar `verify_api.py` com administração local do banco; ele testa duas
criações em paralelo, persistência e isolamento, e limpa somente suas próprias
contas `@example.invalid` em `finally`. O resultado vai para
`/srv/hbrp/api/validation.json`. Comparar os dados reais preservados e confirmar
que não restaram as contas de teste. Nenhum APK é necessário.

O `deploy.py` também reconhece 003/004 e reinicia o serviço após o backup, mas
é um instalador mais abrangente. Para uma instalação existente, preferir a
sequência mínima acima, sem tocar TLS, firewall ou serviços de outros projetos.

## Teste online para duas pessoas

O módulo `multiplayer.mjs` acrescenta `POST /v1/multiplayer/sync` e
`POST /v1/multiplayer/leave` ao mesmo HTTPS 8443 e certificado. Não exige
migração, biblioteca adicional, outra porta ou outro serviço. Publicar o módulo
junto com `server.mjs`; reiniciar somente `hbrp-api`.

Cada pessoa usa uma conta e um personagem diferentes. A sala `centro_teste`
aceita dois personagens autenticados. O cliente envia posição, direção,
velocidade e fase do pulo até dez vezes por segundo, sem requisições simultâneas.
O servidor fornece a identidade e o modelo a partir do banco e transmite apenas
o estado visual. Vagas expiram após dez segundos sem atualização; logout e
saída explícita removem a presença. O HUD mostra `ONLINE 1/2` ou `ONLINE 2/2`.

O estado visual é temporário e a simulação de movimento continua no cliente.
Esse teste não implementa combate, colisão entre jogadores, voz nem física
autoritativa. Dinheiro, inventário e checkpoints continuam nas rotas existentes;
o snapshot multiplayer não aceita esses campos.

O chat da sala usa `POST /v1/multiplayer/chat` com `clientId`, `messageId`
(UUID de reenvio) e `text`. Requer a mesma sessão e presença na sala, aceita
até 180 caracteres em uma linha e limita envios por jogador. Nome e UID vêm
da conta autenticada. O sync aceita o cursor opcional `afterChat` e devolve
`chatEpoch` e mensagens ordenadas, sem quebrar clientes anteriores. O histórico
é temporário: até 60 mensagens, por no máximo 15 minutos; reiniciar o serviço
apaga esse histórico. Não há migração nem gravação de mensagens no banco.

Validação local: `node --test test/characters.test.mjs test/multiplayer.test.mjs`.
Na VPS, `python3 /srv/hbrp/api/verify_multiplayer.py` executa os testes com
contas descartáveis e remove somente essas contas em `finally`.
`--prepare-clients` reserva fixtures para a verificação com duas instâncias
Unity; após ela, executar `--cleanup` e remover os arquivos locais das fixtures.
Nunca registrar tokens ou credenciais desses testes nos relatórios.

## Administração pelo chat

Os comandos são digitados no chat e exigem a identidade administrativa fornecida pelo servidor. Use `/ahelp` para listar os comandos liberados ao seu nível. O catálogo completo está em `admin-command-catalog.mjs`. O painel `/apanel` (nível 2) mostra apenas a lista pesquisável de jogadores; não executa ações.

Já funcionam: `/coords`, `/mystats`, `/astats`, `/alist`, `/a` (privado entre administradores), `/aad`, `/goto`, `/gethere`, `/spawn`, `/re`, `/reoff`, `/mute`, `/unmute`, `/kick`, `/ban`, `/unban`, `/makeadmin`, `/inviz`, `/removeinviz`, `/returninviz`, `/t`, `/apanel` e `/speed 1|2|4|6|8|10|12`. Estatísticas e mutes da sala ficam em memória e reiniciam com o serviço. A hora definida por `/t` vale para a sala atual. O painel e os atalhos de comando antigos foram removidos do HUD.

Comandos que dependem de sistemas ainda ausentes, como veículos, facções, armas, prisões, denúncias e guerras, são aceitos apenas para informar qual sistema falta. Eles não executam efeitos simulados. A API valida níveis e hierarquia. Banimentos com prazo exigem a migração `007_admin_chat.sql`, que acrescenta `characters.blocked_until`. Aplique a migração antes de publicar o backend atualizado. As migrações 005 e 006 continuam pré-requisitos. `npm test` valida o backend com banco local em memória.

## Jornada inicial e catálogo de missões

`mission-catalog.json` registra as dez missões obrigatórias de tutorial e as sete missões normais fornecidas pelo projeto. As três primeiras estão jogáveis; as demais ficam cadastradas como `implemented:false` até que seus sistemas reais existam, portanto não podem conceder recompensa provisória. As listas diária e semanal estão preparadas, mas vazias até existir conteúdo aprovado.

`GET /v1/missions/arrival` recupera o andamento do personagem autenticado. `POST /v1/missions/arrival` aceita as ações `start`, `finish`, `confirm_rg`, `issue_rg`, `start_driving_school` e `finish_driving_school`. A configuração em `arrival-config.json` define NPCs, percursos, duração do empréstimo e recompensas das Missões 01–03. A proximidade usa a posição salva pela API; o cliente deve confirmar seu checkpoint antes de cada interação.

O andamento usa `character_progress` existente, categoria `mission`, código `arrival_welcome_v1`; não há tabela de propriedade para o carro emprestado. Missões 01 e 02 concedem 100 XP e 15000 centavos cada; a Missão 03 concede 100 XP. Cada recompensa é transacional e idempotente. O RG persiste no estado da jornada. A conclusão da Missão 03 encerra o empréstimo. Contas que concluíram a versão antiga seguem diretamente para a emissão do RG sem repetir a recompensa anterior.

Publicar `arrival-mission.mjs` e `arrival-config.json` junto aos módulos atuais. A migração 007 da versão 0.5.8 foi aplicada como pré-requisito aditivo; a missão utiliza tabelas já existentes e não requer migração 008. Manter TLS, credenciais e demais serviços. `node --test test/arrival.test.mjs` cobre empréstimo, proximidade, isolamento, autorização do veículo, expiração, recompensa única e rollback. Na VPS, `python3 /srv/hbrp/api/verify_arrival.py` usa uma conta temporária e remove seu andamento e histórico financeiro na limpeza. O cliente mobile só receberá essa interface em um APK futuro solicitado explicitamente.

## Operação dos bots preservados

`test_bots.py --provision` cria ou verifica exatamente cinco contas normais com personagens BotLucas, BotMarina, BotRafael, BotJulia e BotPedro. Usa o cadastro e a criação de personagem oficiais da API; não injeta identidades no multiplayer nem concede admin. Credenciais aleatórias ficam somente em `/srv/hbrp/bots/accounts.json` (root:hbrp-api, 0640), fora do Git. Não executar o provisionamento repetidamente: os limites normais de login/cadastro se aplicam.

Por solicitação do responsável, `hbrp-bots.service` está parado e desabilitado desde a preparação da versão 0.5.7. As cinco contas foram preservadas; não há bots automáticos na cidade. O código, se explicitamente reativado, usa caminhada e pausas independentes, sem corrida ou pulo. A sala continua com dez vagas.

Para parar/iniciar: `systemctl stop hbrp-bots` / `systemctl start hbrp-bots`. Para trazer de volta um bot após revogação de sessão e posterior liberação: `systemctl restart hbrp-bots`. Sessões revogadas ou bloqueadas interrompem o cliente, sem login automático para contornar a moderação. As contas são persistentes e não devem ser apagadas na limpeza dos testes temporários. No cliente 0.5.6, os bots já aparecem como jogadores remotos e no painel, sem APK novo. Os comandos ainda não implementados continuam sem efeito; cargo e bloqueio/desbloqueio funcionam normalmente.

Validação de movimento: `python test/bots_test.py`. O teste cobre dez minutos simulados por bot, limites do trajeto, deslocamento por frame e independência dos destinos/pausas, sem corrida ou pulo.


## Comandos gerais de jogador em 31 de outubro de 2026

O catálogo `player-command-catalog.mjs` registra 21 comandos. `/ajuda` abre o guia público, também incluído em `/helpadmin`. `/s` entrega o grito aos jogadores presentes em até 50 metros no momento do envio; `/w ID texto` é privado ao alvo e remetente. Ambos respeitam mute e limite de mensagens. `/gps`, `/phone`, `/stats`, `/quests` abrem telas existentes. `/headmove` alterna localmente a rotação animada da cabeça. `/anim` abre o menu reservado para futuros gestos.

`/report` persiste solicitações com intervalo mínimo de 60 segundos por personagem; `/myreports` consulta somente os últimos 20 registros do personagem autenticado. Aplicar `011_player_reports.sql` uma única vez, após backup, com a identidade administrativa das migrações anteriores. O atendimento e a resposta dos relatórios ainda serão implementados; `/ans` continua indicado como pendente.

`/me`, `/do`, `/try`, `/mask`, `/house`, `/biz`, `/daily`, `/j`, `/v` e `/healme` retornam mensagem de preparação, sem simular ações, benefício VIP ou cura grátis. Kit médico exige inventário e consumo transacional. Testes locais: `node --test test/player-commands.test.mjs test/multiplayer.test.mjs test/police-commands.test.mjs`. Validação HTTPS com contas temporárias e limpeza: `python3 /srv/hbrp/api/verify_player_commands.py`. Alterações de interface exigem o cliente Unity atualizado; o APK 0.6.0 não inclui os novos atalhos.


## Teleporte administrativo pelo mapa

No cliente Unity local, toque/clique sem arrastar no mapa ampliado solicita `POST /v1/admin/map-teleport` com `{x,z}`. A API confere sessão e nível administrativo atual (mínimo 1), valida coordenadas e registra `admin.map_teleport` no histórico de auditoria. Não há migração adicional. Arrastar continua navegando pelo mapa; jogadores comuns não iniciam teleporte.

O cliente aguarda os setores, procura piso acima da água e espaço livre para a cápsula, e chega com queda de 2,5 metros usando a animação Jump. Falha de carregamento/piso retorna ao ponto anterior. O checkpoint fica suspenso no teleporte e a posição provisória de carregamento não é transmitida. O recurso não pode ser iniciado em veículo, algemado, observando ou em ação de combate. A altura e colisão são resolvidas no cliente deste protótipo.

Validação: `node --test test/admin-map-teleport.test.mjs`; `python3 /srv/hbrp/api/verify_admin_map.py` verifica HTTPS, autorização e auditoria com conta temporária. Em 31/10/2026, 25 testes locais passaram, C# compilou sem erros e 11 verificações HTTPS passaram na KVM1. O efeito visual ainda requer avaliação em Play Mode. Nenhum APK foi produzido.


Ajuste local do teleporte: clicar agora marca o destino e abre confirmação. Apenas o botão Ir para destino solicita autorização; cancelar ou fechar invalida a seleção. Um setor é antecipado durante a confirmação, mantendo o setor ocupado e o teto móvel configurado. A chegada aguarda o setor do ponto e valida colisão, permitindo carregar vizinhos em seguida. A mensagem Carregando destino foi removida. A redução de espera ainda precisa ser medida em execução real.

## Combate corpo a corpo em teste local

`POST /v1/multiplayer/attack` exige Bearer token e exatamente `{clientId,attackId}`, ambos UUIDs. O ator vem da sessão e precisa estar na sala com o mesmo cliente. O servidor escolhe somente um alvo vivo à frente (cone de 120 graus), a até 2,2 metros no plano e 1,5 metro de diferença vertical, usando as posições autenticadas mais recentes (até 1,5 segundo). O combo completo dura 2,2 segundos e causa 20 pontos de dano uma única vez; repetir o mesmo `attackId`, inclusive em concorrência, devolve o resultado original sem novo dano. Algema, salto, veículo, reação a dano e morte impedem atacar. Ocupantes de veículo não recebem socos através do veículo.

A vida é reduzida atomicamente em `hbrp.character_stats` antes de publicar a reação. Nenhuma migração é necessária. `health`, alvo, dano e horário fornecidos pelo cliente são rejeitados. Um alvo reage por 1,6 segundo, com movimento bloqueado nesse intervalo; ao chegar a zero permanece morto e imóvel. A vida persiste ao reconectar e no bot oficial de teste. Não foi adicionado renascimento automático; `/healme`, `/inf` e a cura administrativa continuam pendentes dos sistemas correspondentes.

O sync acrescenta `combatAvailable:true`, `selfCombat`, `peers[].combat` e `peers[].combatClient`. A resposta do ataque traz `serverTime`, `selfCombat`, `combatPeers:[{uid,combat}]`, `targetUid` e `damage`. Cada estado de combate contém `epoch`, `revision`, `attackSequence`, `attackId`, `attackAt`, `hitSequence`, `hitAttackId`, `hitAt`, `deathSequence`, `deathAt`, `health` e `dead`. Horários são milissegundos Unix do servidor; a revisão crescente e o epoch permitem ignorar respostas atrasadas e não repetir animações ao consultar novamente. O cliente prevê a animação do próprio soco imediatamente e reconcilia os eventos confirmados, sem enviar vida ou dano. `hitAttackId` identifica o ataque que realmente causou a reação; uma previsão visual local pode ser confirmada sem reiniciar a animação, sem antecipar vida ou morte. Um erro ou golpe sem alvo não cria `hitAttackId`. `peers[].combatClient` informa se o outro cliente negociou combate ou é o bot oficial, evitando antecipar reação em um APK antigo.

Compatibilidade: após receber `combatAvailable:true`, o cliente atualizado envia `combatClient:true` nos próximos frames de sync. Esse campo é opcional e aceita somente booleanos. Clientes antigos continuam entrando e se movimentando; não podem iniciar ataques nem são selecionados como alvos enquanto não anunciarem suporte às animações e ao estado de combate. O bot oficial de teste pode receber dano. Essa negociação evita congelar ou matar jogadores com APK antigo sem a interface correspondente.

O movimento continua sendo simulado pelo cliente neste protótipo; distância e orientação são conferidas sobre as posições da sala. Não há geometria do mapa no backend para validar paredes entre os dois atores. Para combate competitivo será necessário validar colisão e deslocamento no servidor dedicado.

Testes: `node --test test/melee-combat.test.mjs test/melee-combat-api.test.mjs test/multiplayer.test.mjs test/police-vehicle.test.mjs`. O teste HTTP usa PostgreSQL em memória e contas descartáveis; não acessa a VPS. Esta alteração de protocolo requer publicar a API antes do teste online com clientes atualizados. Nenhum serviço, certificado, credencial ou APK é alterado por esse procedimento local.

## Cargo administrativo nivel 4

O nivel 4 e um cargo especializado: seu unico comando administrativo e `/adminpromov [ID/nick] [1|2]`. Nao herda painel, teleporte, velocidade, invisibilidade ou atalho policial. Comandos normais de jogador continuam disponiveis. Os niveis 1, 2 e 3 preservam suas permissoes anteriores.

O alvo pode ser pesquisado pelo ID ou pelo nome/primeiro nome exato, sem diferenciar maiusculas e com Unicode NFC. Nome repetido exige ID. O comando permite promover jogador normal para 1 ou 2 e nivel 1 para 2; nao rebaixa, nao altera a propria conta e nao altera niveis 3 ou superiores. Transacao revalida o cargo apos obter os bloqueios e registra `admin.adminpromov` na auditoria. Reenvio da mesma mensagem conserva a idempotencia do chat.

O endpoint administrativo comum continua limitado a conceder ate o nivel 3. A atribuicao inicial do cargo especializado 4 e uma operacao administrativa separada, nunca feita por cadastro ou dados enviados pelo cliente. Nenhuma migracao nova e necessaria: `accounts.admin_level` ja admite esse valor. Nesta implementacao nenhuma conta existente recebeu o cargo 4.

O sync devolve `selfAccountRole` e `selfAdminLevel` para atualizar a interface do personagem autenticado sem novo login. Clientes antigos ignoram esses campos; a nova interface Unity oculta as ferramentas indevidas do cargo 4, e a API verifica a restricao independentemente da interface.

Validacao: `node --test test/admin-rank4.test.mjs test/admin-promote.test.mjs test/player-admin.test.mjs test/admin-map-teleport.test.mjs`. Os testes HTTP usam banco descartavel. Verificacao Unity sem Play/build: `AdminRankPermissionVerification.Run`. A verificacao HTTPS da VPS usa somente contas temporarias e as remove ao final, comparando a contagem e o hash dos cargos existentes.

## Acesso fechado da equipe beta por UID

A API de produção exige um UID e uma senha emitidos pela administração. `POST /v1/auth/login` recebe exatamente `{uid:"123",password:"..."}`. Não aceita e-mail nem cria conta automaticamente. `POST /v1/auth/register` retorna `403 REGISTRATION_CLOSED`. `GET /v1/auth/config` informa `{authMode:"beta",registrationOpen:false}`. As instruções antigas de cadastro público e provisionamento de bots acima ficam como histórico e não habilitam acesso nesta fase.

A migração aditiva `012_beta_access.sql` cria concessões explícitas em `beta_access`, a versão da credencial em cada sessão e os metadados de idempotência. Não autoriza contas existentes, não apaga dados e não contém UID privilegiado fixo. Uma sessão só é aceita se sua versão corresponder à concessão ativa da conta. Sessões anteriores sem concessão retornam `401 BETA_ACCESS_REQUIRED`; redefinição ou revogação invalida as sessões anteriores. O login revalida senha, versão e revogação sob os bloqueios da conta e da concessão, impedindo usar uma senha anterior durante uma troca concorrente.

O cadastro de personagem continua separado: uma conta nova recebe `state:null`, `needsCharacter:true` e `account:{uid,accountRole,adminLevel,canManageBeta}` depois do login. O estado de personagem também expõe `canManageBeta`; o painel usa esse indicador, sempre conferido novamente na API. O personagem é criado pelos endpoints existentes. As novas contas beta não precisam de e-mail, identidade fictícia ou dados de personagem antecipados. Senhas geradas têm 24 caracteres aleatórios e são armazenadas somente como hash scrypt; redefinir senha não altera personagem, dinheiro ou progresso.

Somente administradores com a capacidade explícita `beta_access.can_manage`, nível positivo e cargo diferente de 4 podem emitir acessos. O provisionamento autoriza essa capacidade na conta gestora indicada e preserva seu nível atual; nenhum administrador a recebe automaticamente pelo cargo. A API bloqueia alterações na própria conta e em cargos iguais ou superiores. As rotas usam Bearer token e a autorização atual do banco:

| Rota | Dados | Resultado |
| --- | --- | --- |
| `GET /v1/admin/beta-access?q=&offset=0` | Pesquisa opcional de rótulo ou UID | `accesses`, `total`, `offset`, `pageSize:25` |
| `POST /v1/admin/beta-access` | `{requestId,label}` | Nova conta normal e `{access,password,credentialAvailable:true}` |
| `POST /v1/admin/beta-access/:uid/reset` | `{requestId}` | Nova senha exibida uma única vez; encerra as sessões antigas |
| `POST /v1/admin/beta-access/:uid/revoke` | `{requestId,reason?}` | `{uid,revoked:true}`; preserva os dados da conta |
| `POST /v1/admin/beta-access/:uid/approve` | `{requestId,label?}` | Autoriza explicitamente uma conta existente com nova senha beta |

Cada item da lista contém `uid,label,active,createdAt,lastLoginAt,hasCharacter,accountRole,adminLevel,isSelf,canManage`. A listagem não revela e-mail, hash, senha ou token. O `requestId` é um UUID criado pelo painel por operação. Uma resposta perdida de emissão ou redefinição pode ser repetida com o mesmo UUID: retorna `409 CREDENTIAL_ALREADY_ISSUED` com `uid/uids`, sem criar outra conta e sem revelar novamente a senha. Nesse caso, consulte o UID e redefina sua senha com um novo pedido. Mesmo UUID com dados diferentes retorna `409 BETA_REQUEST_CONFLICT`. Senhas não são guardadas no registro de idempotência, auditoria ou logs. As respostas HTTP usam `Cache-Control: no-store`.

### Preparação inicial, antes de ativar a API atualizada

Faça o backup habitual e aplique a migração 012 com a identidade administrativa existente e `ON_ERROR_STOP=1`. Mantenha a API anterior até terminar o provisionamento. Execute o utilitário administrativo com `DATABASE_URL` já carregada do ambiente privado do serviço; não passe a URL na linha de comando e não imprima esse ambiente:

```text
node provision_beta_access.mjs --manager-uid <UID do responsável> --count 5 --batch-id <UUID fixo desta operação> --output /diretorio/privado/acessos-beta.json
```

O diretório deve existir e ser privado. O utilitário cria um arquivo exclusivo com permissão `0600`, sem sobrescrever arquivos. Sua saída padrão contém somente contagem, UIDs e caminho. As cinco senhas estão apenas no arquivo privado para entrega individual à equipe, nunca no Git ou no APK. Guarde o UUID do lote: repetir a operação conserva os UIDs existentes e recusa gerar contas duplicadas.

Na mesma transação, o utilitário autoriza somente a conta gestora indicada, copiando o hash da senha atual e associando suas sessões não revogadas à concessão inicial. Esse aproveitamento de sessões acontece apenas quando a conta ainda não tinha concessão; novas execuções não recuperam sessões invalidadas depois por redefinição ou revogação. A conta gestora precisa ser um administrador de nível positivo já existente e diferente do cargo 4; sua função e nível não são alterados. Nenhuma outra conta antiga recebe autorização implícita. As cinco contas novas começam como jogador normal, nível administrativo zero e sem personagem. Só depois desse provisionamento conclua a troca do código e reinicie exclusivamente `hbrp-api`.

Validação local: `node --test test/beta-access.test.mjs`. A suíte usa PostgreSQL descartável e cobre acesso somente por UID, bloqueio do cadastro público, tokens antigos sem concessão, preservação explícita da gestão, cinco contas sem personagem, criação posterior de personagem, nível 4 sem acesso ao painel, idempotência, sigilo das senhas, redefinição, revogação, concorrência de credenciais e rollback quando a auditoria falha. O modo legado existe somente como opção explícita do construtor nos testes históricos; a inicialização de produção não lê modo legado de variável de ambiente nem de dados enviados pelo cliente.
