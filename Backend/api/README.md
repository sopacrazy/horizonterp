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
rejeita campos extras e posições fora da pequena área de teste. Isso é persistência
para o protótipo; não substitui simulação autoritativa de movimentação no multiplayer.
Não há recuperação/verificação de e-mail nem Google/Apple nesta entrega.

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

## Administração (preparação)
A migração 005 adiciona `accounts.account_role` (`user`/`admin`) e `admin_level` (0 para usuário; positivo para administrador). Novas contas e contas antigas iniciam como usuário normal. Promoções são operações administrativas no banco; a identidade da API não tem permissão de INSERT/UPDATE nessas colunas.

O estado do personagem e o objeto `account` de login/player informam `accountRole` e `adminLevel`. Os 15 comandos solicitados são reconhecidos sem diferenciar maiúsculas/minúsculas. Todos permanecem sem implementação: não teleportam, punem, concedem imortalidade nem enviam mensagens administrativas. A resposta é privada em `command`, com `status: not_implemented`; comandos não são inseridos no histórico público. Jogadores normais recebem `ADMIN_ONLY`. Ainda não há hierarquia por comando.
