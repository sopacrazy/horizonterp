# Movimento UDP e Vivox — integração local

O projeto usa o Unity Transport incluído no Editor 6000.6 (6.6.0) e Vivox 16.12.1. A API continua responsável por conta, personagem, comandos, chat e persistência. Movimento usa um canal separado de 20 Hz com UDP/DTLS e snapshots não confiáveis sequenciados; autenticação e renovação usam uma pipeline confiável. O cliente retorna ao HTTPS quando o UDP não está configurado ou se desconecta.

## Testar neste computador

1. Execute `python tools/prepare_local_realtime.py` na raiz do repositório. Isso gera certificados temporários para localhost, sem instalar certificados no Windows.
2. No Unity, `RealtimeIntegrationVerification.Run` valida UDP/DTLS, `RealtimeIntegrationVerification.BuildServer` gera o relay local e `ReleaseBuild.Windows` gera o cliente Windows. Os executáveis ficam em `TestePersonagem/Builds/WindowsRelease` e `TestePersonagem/Builds/RealtimeServer`.
3. Execute `JogarRedeLocal.bat`. Duas janelas entram com personagens temporários, Lucas e Julia. Feche ambas para encerrar o laboratório. O terminal deve permanecer aberto durante o teste.
4. Para a verificação automática completa: `python tools/run_local_realtime_lab.py --verify`.

O laboratório vincula API HTTPS a `127.0.0.1:18443` e UDP a `127.0.0.1:7777`. O PostgreSQL de testes roda em memória via PGlite; dados desaparecem ao fechar o processo. O modo local não lê nem sobrescreve o acesso salvo da conta real. A configuração alternativa só aceita HTTPS em loopback, porta 18443, mediante argumento explícito de linha de comando. O APK não habilita esse modo.

Relatórios: `tools/realtime-integration/udp-validation.json`, `api-tests.log` e `game-validation.json`. Logs dos dois clientes e servidores ficam em `tools/realtime-integration/lab`. Credenciais de teste ficam em pastas ignoradas pelo Git; os arquivos de login são removidos quando o laboratório encerra.

O `Jogar.bat` normal usa a API configurada em Resources e não inicia o laboratório. Uma API ainda sem `/v1/realtime/ticket` mantém o cliente em HTTPS. Nenhuma atualização da VPS é feita por esses scripts.

### Validação local em 31/10/2026

- Unity compilou cliente Windows e servidor local com sucesso.
- Dois processos reais do jogo autenticaram na API HTTPS local, conectaram ao relay UDP/DTLS e trocaram movimento, pulo e chat. Cada cliente recebeu 467/468 snapshots UDP durante a verificação; nenhum erro foi registrado. Os 50/51 ms medidos são do laboratório deste computador, não da internet móvel.
- O teste isolado do transporte passou também com 120 ms de atraso simulado por sentido, jitter de 60 ms e perda de 10% nos datagramas. Cliente com ticket adulterado foi recusado.
- Os 59 testes da API passaram. A entrada com sessão simulada, preservação durante indisponibilidade, rejeição de sessão expirada, seta azul e giro suave do minimapa passaram no cliente compilado.
- O laboratório usa banco temporário e remove os arquivos de login ao encerrar. Uma falha de isolamento na primeira execução chegou ao cofre real deste PC e limpou a sessão local; foi corrigida com bloqueio explícito no `SessionVault` antes da verificação aprovada. Pode ser necessário autenticar a conta real uma vez. A VPS não foi alterada.

Não houve teste em Android físico nem áudio real pelo Vivox. O `Jogar` foi atualizado apenas localmente; GitHub, APK e configuração da VPS continuam sem esta integração.

## Preparação futura do servidor UDP

O servidor recebe `--hbrp-udp-server-config CAMINHO/server.json`, contendo `bindAddress`, `port`, `certificateFile` e `privateKeyFile`. A chave HMAC é lida exclusivamente da variável de ambiente `HBRP_REALTIME_SECRET` (mínimo de 32 caracteres aleatórios), compartilhada apenas com a API. Nunca coloque essa chave ou a chave TLS em Resources, no cliente, no repositório ou na linha de comando.

Na API, configurar:

| Variável | Conteúdo |
| --- | --- |
| `HBRP_REALTIME_SECRET` | Mesmo segredo de assinatura do servidor dedicado |
| `HBRP_UDP_HOST` | IP público do servidor UDP |
| `HBRP_UDP_PORT` | Porta UDP, inicialmente 7777 |
| `HBRP_UDP_SERVER_NAME` | Nome/SAN certificado esperado pelo cliente |
| `HBRP_UDP_CA_FILE` | Caminho do certificado público da CA |

`POST /v1/realtime/ticket` exige a sessão HBRP e que a conta já esteja na sala com o mesmo clientId. O ticket fixa personagem, mundo, sessão, clientId e nonce, expira em 15 segundos e é renovado a cada 7 segundos. O relay rejeita assinatura, identidade, nonce reutilizado por outra conexão, sequência, formato e coordenadas inválidas. A posição ainda é simulada pelo cliente: isto não implementa física autoritativa, anticheat completo ou colisões no servidor. Uma revogação maliciosamente ignorada pelo cliente deixa de autorizar novos movimentos em até 15 segundos; o cliente normal desconecta ao perder a sessão.

O relay Linux está instalado na VPS Brasil como `hbrp-realtime`, porta UDP 7777, usando o certificado existente. A API continua em HTTPS 8443 e os dados não exigem migração. O projeto mínimo gerado por `tools/prepare_realtime_server_project.py` compila o servidor sem importar o mapa, com `Backend/realtime/BuildRelay.cs`. O certificado e o pin da API não são trocados para instalar o relay.

## Ativar o Vivox

1. Vincular o projeto em **Edit > Project Settings > Services** à organização e ao projeto corretos no Unity Cloud.
2. No Unity Dashboard, selecionar esse projeto, abrir Vivox e ativar Voice & Text. Conferir a faixa gratuita e o uso no painel de cobrança antes de habilitar serviços adicionais.
3. Obter **Server**, **Domain**, **Issuer**, Project ID e, quando aplicável, Environment ID. A signing key fica somente no ambiente do backend.
4. Configurar na API `VIVOX_SERVER`, `VIVOX_DOMAIN`, `VIVOX_ISSUER`, `VIVOX_PROJECT_ID`, `VIVOX_ENVIRONMENT_ID` e `VIVOX_SIGNING_KEY`. Não habilitar geração de tokens com segredo embutido no cliente.
5. Validar login e áudio em dois aparelhos, troca de canais, retirada de serviço policial, pausa, reconexão e permissões do microfone antes de publicar.

O projeto está vinculado ao Unity Cloud e as credenciais do serviço ficam no ambiente privado da API. Sem a configuração, o jogo preserva a voz anterior. Com Vivox configurado, o microfone usa Vivox e deixa de embutir PCM nas requisições de sincronização. O canal de proximidade tem alcance de 35 m; rádio usa canais de grupo 1 a 3. A transmissão usa somente um canal por vez, com mute imediato durante trocas e ao soltar o botão. O cliente entra no canal posicional antes do canal de rádio. O Test Mode do SDK deve permanecer desligado; nenhuma signing key é distribuída ao cliente.

`GET /v1/services` devolve somente configurações públicas. `POST /v1/voice/token` valida a conta e cria VATs assinados para login, proximidade e o canal policial confirmado pelo servidor. Identidade de terceiros, canais arbitrários, alcances maiores e ações privilegiadas são recusados. As credenciais reais foram configuradas na VPS para a versão 0.7.1 e dois clientes foram autenticados no mesmo canal posicional. O teste não capturou áudio; dispositivos físicos, troca de canais e revogação/moderação de participantes no serviço hospedado ainda precisam de avaliação.

Documentação oficial: [Unity Transport](https://docs.unity.com/en-us/engine/6000.7/manual/packages-list/packages-all/pack-core/com-unity-transport), [Vivox por proximidade](https://docs.unity.com/en-us/vivox-unity/developer-guide/channels/positional-channels), [tokens no servidor](https://docs.unity.com/en-us/vivox-unity/access-token-guide/generate-token-secure-server/generate-token-secure-server-toc), [preços UGS](https://unity.com/products/gaming-services/pricing).
