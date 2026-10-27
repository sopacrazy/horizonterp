# Primeira missão: chegada à Prefeitura

Ao entrar na cidade, o jogador se aproxima do morador e usa E (ou o botão de interação no celular) para conversar. O diálogo usa o texto de boas-vindas fornecido pelo responsável. Somente **INICIAR MISSÃO**, confirmado pela API, libera o carro do NPC.

O carro é um empréstimo de até 30 minutos. E entra no veículo, W/S aceleram e dão ré, A/D viram, e F sai. No celular, o controle de movimento dirige e o botão de interação permite entrar/sair. A câmera passa a acompanhar o carro. O GPS azul segue a rua até o posto de atendimento da Prefeitura. É necessário sair do carro e falar com o atendente para concluir.

A conclusão credita 100 XP e R$ 150 virtuais no servidor, apenas uma vez por personagem, e devolve o veículo. O carro não cria registro de propriedade em player_vehicles. A autorização de dirigir no multiplayer termina com o empréstimo. O andamento permanece em character_progress, categoria mission, código arrival_welcome_v1. O RG/CNH e a doação fictícia de HZ Coins do tutorial anterior foram removidos deste fluxo; o próximo atendimento será uma missão futura.

Configuração única: Backend/api/arrival-config.json, copiada para Resources/Arrival/Config.json por ArrivalMissionSetup.Prepare antes dos builds. A cena e a área percorrida foram conferidas com raycasts sobre o mapa atual. Os pontos são o morador (5,0.28,4), carro (10,0.30,0) e atendimento municipal (60,0.92,4).

## Dublagem

O áudio está pendente. Quando recebido, importar com o nome **Assets/Resources/Arrival/WelcomeVoice.mp3** (ou outro formato de áudio suportado, preservando o nome WelcomeVoice). Ele toca quando o diálogo do morador abre e para ao fechar. Os áudios antigos não são usados por esta missão.

## Validação e entrega

- Testes de API usam HTTP e PostgreSQL em memória, cobrindo autorização, proximidade, isolamento, empréstimo, expiração, renovação, conclusão concorrente, recompensa única e rollback financeiro em falha de auditoria.
- verify_arrival.py valida HTTPS na VPS com uma conta temporária e limpa seus próprios registros, inclusive o histórico financeiro da recompensa.
- --verify-arrival executa o ciclo no Windows com transporte simulado, sem usar a conta real nem a VPS.
- O novo Windows foi compilado sem aumentar a versão de distribuição. Nenhum APK novo deve ser gerado/publicado sem pedido explícito.
