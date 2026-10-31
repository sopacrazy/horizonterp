# Migração HBRP para KVM 1

Em 2026-10-30, somente o HBRP foi copiado para `187.41.106.219`.

- PostgreSQL 18, cluster `hbrp`, banco `hbrp`, porta local 55432.
- API `hbrp-api` v0.3.0, HTTPS na porta 8443, certificado novo com SAN para o IP novo.
- Snapshot da VPS antiga: 10 migrações, 14 contas e 14 personagens ativos.
- `verify_api.py` passou. Contas de teste removidas; contagens finais: 14 e 14.
- Backup diário exclusivo do HBRP às 03:00 UTC, retenção de 14 dias.
- A VPS antiga e seus outros projetos continuam intactos e ativos.

## Troca concluída em 31 de outubro de 2026

O APK 0.6.0 ainda usa `https://70.38.101.147:8443` e fixa o certificado antigo.
Esse endpoint agora executa `legacy-api-bridge.mjs`, encaminhando somente o
HBRP para a KVM1 e validando o certificado dela. O certificado antigo,
suas credenciais e os demais serviços da VPS antiga foram preservados.
O projeto local e a versão 0.7.0 usam diretamente a KVM1.

Antes da troca, ambas as APIs foram paradas brevemente e os dados comparados.
Contas, senhas, identidade, saldos, inventários e progresso de missão eram
iguais. Estatísticas e posições foram conciliadas por data de atualização:
o save mais novo do ID 18 na KVM1 e do ID 27 no servidor antigo foram mantidos.
Sessões, revogações, chaves de salvamento e auditoria foram conciliadas sem
substituir registros mais novos. O banco ativo permanece com 14 contas e
14 personagens, e contém a migração 011 de relatórios.

Backups anteriores à troca: `/srv/hbrp/backups/hbrp-20261031T142159Z.dump`
na VPS antiga e `/srv/hbrp/backups/hbrp-20261031T142203Z.dump` na KVM1.
O servidor anterior do endpoint antigo está em
`/srv/hbrp/api-backups/release070-cutover/server.mjs`.
Snapshots privados de conciliação ficam somente na pasta de backups.

O mapa e os demais arquivos do Unity não precisam ser copiados para a VPS. O
servidor hospeda API, banco e lógica de sala/voz; o mapa fica no cliente.
