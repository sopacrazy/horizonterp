# Banco de dados HBRP

Instalado na VPS autorizada em 23/10/2026, separado dos aplicativos existentes.

## Conexão e isolamento

- PostgreSQL 16, cluster `hbrp`, serviço `postgresql@16-hbrp`.
- Host interno `127.0.0.1`, porta `55432`, banco `hbrp`, schema `hbrp`.
- `hbrp_owner`: proprietário sem login, usado em migrações pela administração local.
- `hbrp_app`: usuário da futura API, sem superusuário, criação de bancos ou administração de roles.
- Credenciais somente na VPS: `/srv/hbrp/credentials/database.env` e `app.pgpass`, modo 0600; diretório 0700.
- Nenhuma senha deve ser incluída no Unity, APK, repositório ou documentação.
- Acesso remoto de manutenção via túnel SSH. A porta PostgreSQL não está publicada na internet.
- Diretório de dados: `/var/lib/postgresql/16/hbrp`; configuração: `/etc/postgresql/16/hbrp`.
- Limites iniciais do serviço: 768 MB, quota de uma CPU e 30 conexões; usuário da aplicação limitado a 12 conexões. Rever conforme testes reais.

## Estrutura inicial: 26 tabelas

Contas/UID permanente, identidades de login, hashes de senha, sessões, personagens,
atributos, posição, aparência, mundos, catálogo de itens, inventários, carteiras,
operações financeiras, extrato, catálogos e propriedade de veículos/imóveis,
organizações/membros, progressão de empregos/missões/tutorial, benefícios, punições,
auditoria e versão da migração.

Essa estrutura prepara o armazenamento. Não implementa login, compras, profissões,
multiplayer ou as outras mecânicas do escopo. Não foram criadas contas reais.
Os números do UID são únicos e permanentes, mas não são necessariamente consecutivos.

## Integração futura

Unity -> API autenticada / servidor do jogo -> PostgreSQL.

A API deve verificar titularidade e permissões em toda operação. A credencial do banco
é exclusiva do servidor. Os dados não devem ser gravados diretamente pelo aplicativo.
E-mail/Google/Apple são opções de esquema, não integrações já configuradas.
Senhas devem ser transformadas em hash pela API, nunca armazenadas em texto puro.
Tokens de sessão persistidos são hashes de 32 bytes, não tokens utilizáveis.

- Vida/fome/sede/energia: 0 a 100. XP, nível e tempo jogado não negativos.
- Posição salva por personagem com mundo, XYZ e rotação Y.
- Usar `version` para atualizações condicionais e impedir sobrescrita de um estado mais novo.
- Banco não deve receber posição a cada frame; salvar por eventos/checkpoints e intervalos definidos pelo servidor.
- `cash` e `bank`: valores inteiros em centavos. `hz`: moedas inteiras, vinculadas à conta.
- `cash` e `bank` são vinculados ao personagem.
- Alterações financeiras passam pelas funções `hbrp.change_balance` e `hbrp.transfer_balance`.
- Cada operação exige UUID de idempotência; tentativas repetidas com o mesmo conteúdo não duplicam dinheiro.
- A API autoriza as recompensas, compras e transferências; possuir a função no banco não valida regras de gameplay.
- Definições de itens, veículos, imóveis, organizações e benefícios são administradas por migração/rotina administrativa futura, não pela credencial do cliente.
- Nomes de personagens e e-mails são únicos sem diferenciar maiúsculas/minúsculas.
- Dados principais têm exclusão restrita; exclusão de personagem deve ser lógica (`deleted_at`) por administração.

## Backup e restauração

- Script na VPS: `/srv/hbrp/database/backup.sh`.
- Agendamento próprio: `/etc/cron.d/hbrp-database`, diariamente às 03:15 no horário da VPS.
- Arquivos em `/srv/hbrp/backups`, formato custom do pg_dump, retenção de 14 dias.
- Backup inicial restaurado em banco temporário exclusivo HBRP, conferido e removido depois.
- Cópia inicial baixada para `Backend/backups` neste computador (não incluída no controle de versões).
- Cópia externa automática diária ainda precisa de destino e configuração; backup local sozinho não cobre perda da VPS.
- O arquivo de credenciais não faz parte do pg_dump. Recuperação em outra máquina exige provisionar os papéis e criar novas credenciais protegidas antes da restauração.

## Arquivos e verificações

- `database/001_initial.sql`: migração inicial, aplicada como `hbrp_owner`.
- `database/002_transfer_locking.sql`: ajuste dos bloqueios para transferências concorrentes, já aplicado.
- `database/bootstrap_vps.py`: instalação inicial já executada; não reexecutar sobre dados existentes.
- `database/verify.sql`: testes transacionais com rollback.
- `database/verify_concurrency.py`: testes concorrentes com fixtures próprias e limpeza.
- `database/verify_restore.py`: restauração do backup em banco temporário HBRP e verificação.
- `database/validation/`: relatórios baixados da VPS.

Os serviços preexistentes nginx, PM2, Clipai Shorts e TitoJa foram comparados antes/depois
da instalação. Não foram alterados seus arquivos, portas ou configurações.

## Estado da validação

Migrações 001 e 002 aplicadas. Login da aplicação e testes de integridade/permissões
passaram. O teste concorrente passou com oito solicitações para a mesma chave, oito
créditos independentes e oito transferências paralelas, sem duplicação de dinheiro.
O backup final foi restaurado em banco temporário; as 26 tabelas e todos os testes
transacionais foram conferidos novamente. Fixtures de teste removidas; zero contas reais.
Relatórios e cópia inicial do backup foram baixados para este computador.
Os serviços preexistentes mantêm os mesmos processos da verificação inicial.
O banco está pronto para a próxima etapa: API autenticada e integração com o Unity.
