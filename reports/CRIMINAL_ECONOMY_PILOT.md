# Economia persistente — Horizonte Brasil RP

Atualizado em 03/11/2026.

## Estado atual

A economia está conectada à API real da VPS `187.41.106.219`. A interface do projeto Unity principal usa o token da conta e o certificado já existente. Foram removidos os estoques e as recompensas simulados no cliente. As atividades, os depósitos, a produção e o histórico são confirmados no PostgreSQL.

As migrações aditivas 15–18 foram aplicadas. Somente a API HBRP foi reiniciada. Os outros módulos publicados, serviços, portas, credenciais e certificado foram preservados.

## Seis organizações e produção temporizada

Disponível no projeto principal para testar em Entrada > Play > F7 > BANCADA. Nenhuma build foi gerada nesta etapa.

| Organização | Produto exclusivo | Saída por lote | Tempo por lote | Materiais fictícios |
|---|---|---:|---:|---|
| Facção Roxa | Parafal (RP) | 1 | 60 s | 6 sucatas + 4 cristais |
| Facção Verde | AK-47 (RP) | 1 | 60 s | 6 sucatas + 4 cristais |
| Facção Vermelha | M4 (RP) | 1 | 60 s | 6 sucatas + 4 cristais |
| Máfia Branca | Munição de Parafal (RP) | 30 | 30 s | 3 sucatas + 2 cristais |
| Máfia Laranja | Munição de AK-47 (RP) | 30 | 30 s | 3 sucatas + 2 cristais |
| Máfia Preta | Munição de M4 (RP) | 30 | 30 s | 3 sucatas + 2 cristais |

Preços, materiais e tempos são iniciais de teste. São receitas abstratas de gameplay. A caixa logística anterior foi preservada nas duas organizações que já a produziam, agora com 30 segundos por lote.

- Uma produção pendente por organização. A duração multiplica pelo número de lotes.
- Materiais deduzidos ao iniciar. Produto permanece indisponível até o prazo e a confirmação de recolhimento na mesma bancada.
- O trabalho persiste no PostgreSQL e continua durante logout/reinício do serviço.
- Responsáveis autorizados da mesma organização podem recolher ou cancelar. Trocar de organização não transfere o lote para a nova organização.
- Cancelamento devolve o snapshot dos materiais consumidos; se faltar espaço, nada é alterado e o trabalho continua pendente até liberar espaço.
- A capacidade de saída é reservada; depósitos e devoluções de loja não podem ocupar o espaço do lote.
- Receitas de outras organizações, coleta antecipada, duplicação de confirmação e recolhimento de outro grupo são bloqueados no servidor.
- Novas organizações possuem depósito, bancada e loja com NPCs/marcadores provisórios. Nenhuma conta foi vinculada automaticamente e nenhum estoque foi criado.
- O cronômetro do cliente usa a hora recebida do servidor para compensar relógio local incorreto.

Os produtos podem ser fabricados, guardados, vendidos e comprados. **Ainda falta ligar estes códigos de item ao sistema de equipar armas e consumir munição nos disparos.** As animações/modelos atuais de combate continuam separados do catálogo econômico.

### Como testar

1. Admin nível 1 ou 3 usa F7 > MEMBROS e vincula o personagem à organização e cargo desejados.
2. Depositar os materiais, ir à bancada e escolher INICIAR PRODUÇÃO.
3. Conferir cronômetro; sair e entrar novamente para confirmar recuperação.
4. Após o prazo, usar RECOLHER PRODUÇÃO na bancada. O resultado aparece no depósito.
5. Abastecer a loja, comprar com outra conta e conferir inventário/caixa/histórico.
6. Testar cancelamento antes do término: os materiais devem voltar ao depósito, sem entregar o produto.

### Validação e publicação

- 14 cenários automatizados aprovados; 15 resultados contando o teste agregado. Inclui catálogo das seis organizações, exclusividade, prazo, persistência, reserva de capacidade, cancelamento e repetição.
- Scripts Unity compilaram sem erros; Play visual e Android ainda precisam ser avaliados.
- Migração 18 aplicada, serviço hbrp-api ativo e health ok.
- Verificação no PostgreSQL real com papel hbrp_app passou e reverteu todos os dados temporários. Zero contas temporárias restantes.
- 24 contas e 3 personagens preservados; credenciais e certificado iguais antes/depois.
- Backup confirmado: `/srv/hbrp/backups/economy-20261103T233503Z/database.dump` (555.259 bytes).
- Evidência: `tools/criminal-economy-validation/production-release/deployment.json`.

## Etapa de comércio e recuperação de atividades

Disponível no código do projeto principal para testar pelo Play do Unity. Não foi gerada nova build Windows nem APK nesta etapa.

- **Cancelar atividade:** libera a coleta sem entregar materiais; pode ser feito longe do ponto.
- **Expiração:** coleta abandonada expira 15 minutos depois do horário de conclusão e deixa de bloquear uma nova atividade.
- **Lojas:** dois NPCs provisórios, marcados com **L** verde, atendem qualquer jogador. F7 > LOJAS abre compra e gestão.
- **Abastecimento:** responsáveis com permissão de retirada movem produtos do depósito para o estoque da loja. Não cria itens gratuitos.
- **Preço:** configurado em centavos; mínimo R$ 1 e máximo R$ 1.000.000 por unidade. Compra com preço desatualizado é rejeitada antes de cobrar.
- **Compra:** desconta dinheiro da carteira, reduz estoque e entrega o produto ao inventário na mesma transação. Valida espaço e saldo.
- **Caixa:** dinheiro das vendas fica na loja. Líder e sublíder podem recolher para a carteira pessoal, com registro no histórico.
- **Devolução:** produtos não vendidos podem retornar ao depósito, respeitando capacidade e permissões.
- **Auditoria:** compras, abastecimentos, preços, devoluções, retiradas de caixa e cancelamentos ficam registrados; reenvios não duplicam operações.

O catálogo inicial continua usando a caixa logística fictícia para validar o ciclo. Ainda não é o catálogo de armas e munições do roteiro.

### Como testar o ciclo inteiro

1. Usar F7 > MEMBROS com admin nível 1 ou 3 para vincular um personagem à Roxa ou Branca como líder. Não alteramos vínculos automaticamente.
2. Coletar 6 sucatas e minerar 4 cristais, depositar tudo no depósito da organização e fabricar 1 caixa na bancada.
3. Ir à loja da organização e abrir LOJAS. Abastecer 1 caixa e definir um preço compatível com o dinheiro do comprador.
4. Outra conta vai até a loja e compra. Conferir inventário, carteira, estoque e histórico de ambas as contas.
5. Líder recolhe o caixa. Sair e entrar novamente para verificar persistência.

As lojas provisórias ficam em (2477,3; 12,37; 1676,5), Roxa, e (2501,3; 12,37; 1676,5), Branca.

### Validação desta etapa

- 14 testes automatizados aprovados (13 cenários dentro da suíte principal, incluindo o resultado agregado).
- Todos os scripts C# envolvidos compilaram sem erros; avisos legados permanecem em outros scripts.
- Migração 17 aplicada na VPS; hbrp-api ativo e health ok.
- Testes no PostgreSQL real com papel hbrp_app: loja, dinheiro, estoque, caixa, permissões, repetição, cancelamento e expiração aprovados dentro de transação revertida.
- 24 contas e 3 personagens preservados, identidades/credenciais e certificado iguais antes e depois; zero contas temporárias restantes.
- Backup verificado: `/srv/hbrp/backups/economy-20261103T225512Z/database.dump` (537.007 bytes).
- Evidência: `tools/criminal-economy-validation/commerce-release/deployment.json`.
- Inspeção posterior confirmou migrações 1–17, health ok e zero erros recentes do serviço.
- Não foi realizado teste visual no Play ou Android nem teste de contenção com conexões independentes nesta etapa.

## Como testar pelo Unity

1. Abrir `C:/SistemaAdr/gta/TestePersonagem`, cena `Assets/Scenes/Entrada.unity`, e iniciar o Play.
2. Entrar na cidade com uma conta real. Os pontos de coleta e mineração ficam perto do spawn de Los Santos e aparecem no minimapa/mapa.
3. Aproximar-se do NPC e pressionar **E** ou tocar no botão de interação. Também é possível abrir **ECONOMIA** / **F7**.
4. Iniciar uma atividade. O servidor grava o início e libera a conclusão depois de 20 segundos na coleta ou 30 segundos na mineração. Clicar em **CONCLUIR ATIVIDADE** no ponto correto.
5. Para testar depósito e produção, um administrador nível 1 ou 3 abre **F7 > MEMBROS**, informa o UID, escolhe uma organização e um cargo e salva o vínculo. Nenhum personagem existente foi vinculado automaticamente.
6. Ir ao NPC do depósito, guardar os materiais e ir à bancada para produzir. A produção usa o estoque do depósito da organização.
7. Consultar **HISTÓRICO**, sair e entrar novamente para conferir a persistência.

Administradores nível 4 continuam restritos ao cargo de promoção e não acessam a gestão econômica. Cargos de membro/novato podem depositar; gerente/sublíder/líder podem retirar e produzir, dentro dos limites do cargo.

## Ciclo inicial

| Atividade | Tempo mínimo | Resultado |
|---|---:|---|
| Coleta urbana | 20 s | 3 sucatas |
| Mineração | 30 s | 2 cristais |
| Produção | No ponto da bancada | 6 sucatas + 4 cristais → 1 caixa logística |

O intervalo adicional entre coletas da mesma atividade é definido pelo servidor: 20 segundos na coleta urbana e 30 segundos na mineração. Os nomes internos dos itens continuam `pilot_scrap`, `pilot_crystal` e `pilot_logistics_crate`; são itens reais do inventário persistente deste catálogo inicial.

## NPCs e marcadores provisórios

Os NPCs usam o modelo e as animações já existentes, ficam parados e viram para o jogador próximo. Só aparecem depois que o chão do setor foi encontrado. São pontos de atendimento do cenário, sem contas de jogador.

| Ponto | X | Y de referência | Z | Acesso |
|---|---:|---:|---:|---|
| Coleta urbana | 2481,3 | 12,37 | 1668,5 | Jogadores |
| Mineração | 2497,3 | 12,37 | 1668,5 | Jogadores |
| Depósito Roxa | 2489,3 | 12,37 | 1660,5 | Facção Roxa |
| Bancada Roxa | 2485,3 | 12,37 | 1660,5 | Facção Roxa |
| Depósito Branca | 2489,3 | 12,37 | 1676,5 | Máfia Branca |
| Bancada Branca | 2493,3 | 12,37 | 1676,5 | Máfia Branca |

Os pontos têm raio de interação de 4 metros. O código lógico do mundo permanece `centro_teste`, preservando os saves anteriores. O mapa mostra **C** para atividades, **D** para depósito e **B** para bancada. Depósito/bancada aparecem conforme o vínculo real do personagem. Clicar no marcador abre a seção correspondente e não dispara o teleporte administrativo.

## Regras implementadas

- Coleta exige personagem vivo, presença recente na cidade, proximidade, início registrado e tempo mínimo cumprido; não pode ser executada dentro de veículo.
- Depósito, retirada e produção validam a organização, o cargo, a posição e os limites no servidor.
- Operações de itens usam transações, bloqueios e identificadores de repetição para evitar créditos duplicados.
- Uma atividade em andamento pode ser recuperada ao entrar novamente; é necessário voltar ao mesmo ponto para concluí-la.
- Histórico de atividades do próprio personagem e operações da própria organização, limitado aos últimos 50 registros por grupo.
- O cliente descarta respostas de uma sessão antiga e limpa a interface/NPCs ao trocar de conta.
- Respostas incertas são reconciliadas por consulta; a confirmação de uma operação de itens reutiliza o mesmo identificador.

## Publicação e validação

- Backup verificado: `/srv/hbrp/backups/economy-20261103T212726Z/database.dump` (453.372 bytes).
- Serviço `hbrp-api`: ativo; HTTPS `/health`: `status: ok`, versão da API `0.3.0`.
- Inspeção posterior da VPS confirmou as migrações 1–16 e **zero erros recentes** de requisição/banco nos logs do serviço.
- Migrações aplicadas: **15 e 16**.
- Contas antes/depois: **24 / 24**; personagens antes/depois: **3 / 3**.
- Identidades e credenciais comparadas antes/depois: preservadas. Certificado preservado.
- **12/12 testes econômicos específicos aprovados**.
- Teste no PostgreSQL real aprovado: tempo/proximidade, atribuição administrativa, isolamento, transferências, receita, histórico, repetição e rota autenticada.
- As contas e alterações do verificador existiram somente dentro de uma transação revertida. **Zero contas temporárias restantes**.
- Compilação estática de todos os scripts C# envolvidos: aprovada, com avisos legados de outros scripts.

O teste real do banco não é um teste de carga/contenção com várias conexões. A suíte geral do projeto também possui falhas administrativas/HTTP fora deste conjunto, portanto esta entrega não representa aprovação global do projeto. O posicionamento dos NPCs, a interação visual e o layout no aparelho devem ser avaliados no Play e depois no Android.

## Pendências

1. Ajustar os locais definitivos, aparência dos NPCs e animações específicas de coleta/mineração.
2. Avaliar o ciclo com duas contas e testar desconexão durante as ações e concorrência real no PostgreSQL.
3. Aprovar catálogo, limites, capacidades, cargos, duração e balanceamento definitivos.
4. Preparar paginação completa do histórico e ferramentas administrativas mais completas conforme os testes. Cancelamento/expiração já foram implementados.
5. Validar visual e desempenho no Android e gerar uma próxima versão quando solicitada. A etapa de comércio não gerou APK nem atualizou o executável Windows.
6. Seis organizações, produtos exclusivos e produção temporizada implementados. Faltam recompensas por raridade e integração dos produtos econômicos com equipar/usar armas e munições.
7. Em etapas posteriores: deep web com anúncios, mensagens e encomendas, contratos, catálogo de armas/munições do RP, carregamentos, territórios, disputas com exclusividade temporária, reputação e investigação. Lavagem é uma extensão posterior, não parte do ciclo atual.

## Arquivos principais

- Backend: `Backend/api/criminal-economy.mjs`, `Backend/api/server.mjs`, migrações `015`/`016` e `Backend/api/verify-criminal-economy-postgres.mjs`.
- Unity: `AccountCriminalEconomy.cs`, `CriminalEconomyPilotPanel.cs`, `CriminalEconomyWorld.cs`, `GameHudEconomyMarkers.cs`, USS e hooks no HUD.
- Resultado da publicação: `tools/criminal-economy-validation/release/deployment.json`.
- Compilação estática sem reimportar o mapa: `tools/Test-CriminalEconomyUnity.ps1`.
