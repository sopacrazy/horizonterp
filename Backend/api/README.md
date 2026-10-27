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

## Missão inicial: empréstimo até a Prefeitura

`GET /v1/missions/arrival` recupera o andamento do personagem autenticado. `POST /v1/missions/arrival` aceita somente `{action:"start"}` ou `{action:"finish"}`. A configuração única em `arrival-config.json` define NPCs, percurso, duração do empréstimo e recompensa. A proximidade usa a posição salva pela API; o cliente deve confirmar seu checkpoint antes da interação. A rede continua com movimento simulado pelo cliente, sem física autoritativa.

O andamento usa `character_progress` existente, categoria `mission`, código `arrival_welcome_v1`; não há tabela de propriedade para o carro emprestado. A conclusão concede 100 XP e 15000 centavos por transação, usando `change_balance` e uma chave idempotente. A fase concluída impede repetição, inclusive concorrente, e encerra a autorização de dirigir no multiplayer. As mensagens de presença aceitam o campo visual opcional `driving`; a API exige empréstimo ativo para transmiti-lo como verdadeiro.

Publicar `arrival-mission.mjs` e `arrival-config.json` junto aos módulos atuais. A migração 007 da versão 0.5.8 foi aplicada como pré-requisito aditivo; a missão utiliza tabelas já existentes e não requer migração 008. Manter TLS, credenciais e demais serviços. `node --test test/arrival.test.mjs` cobre empréstimo, proximidade, isolamento, autorização do veículo, expiração, recompensa única e rollback. Na VPS, `python3 /srv/hbrp/api/verify_arrival.py` usa uma conta temporária e remove seu andamento e histórico financeiro na limpeza. O cliente mobile só receberá essa interface em um APK futuro solicitado explicitamente.

## Operação dos bots preservados

`test_bots.py --provision` cria ou verifica exatamente cinco contas normais com personagens BotLucas, BotMarina, BotRafael, BotJulia e BotPedro. Usa o cadastro e a criação de personagem oficiais da API; não injeta identidades no multiplayer nem concede admin. Credenciais aleatórias ficam somente em `/srv/hbrp/bots/accounts.json` (root:hbrp-api, 0640), fora do Git. Não executar o provisionamento repetidamente: os limites normais de login/cadastro se aplicam.

Por solicitação do responsável, `hbrp-bots.service` está parado e desabilitado desde a preparação da versão 0.5.7. As cinco contas foram preservadas; não há bots automáticos na cidade. O código, se explicitamente reativado, usa caminhada e pausas independentes, sem corrida ou pulo. A sala continua com dez vagas.

Para parar/iniciar: `systemctl stop hbrp-bots` / `systemctl start hbrp-bots`. Para trazer de volta um bot após revogação de sessão e posterior liberação: `systemctl restart hbrp-bots`. Sessões revogadas ou bloqueadas interrompem o cliente, sem login automático para contornar a moderação. As contas são persistentes e não devem ser apagadas na limpeza dos testes temporários. No cliente 0.5.6, os bots já aparecem como jogadores remotos e no painel, sem APK novo. Os comandos ainda não implementados continuam sem efeito; cargo e bloqueio/desbloqueio funcionam normalmente.

Validação de movimento: `python test/bots_test.py`. O teste cobre dez minutos simulados por bot, limites do trajeto, deslocamento por frame e independência dos destinos/pausas, sem corrida ou pulo.
