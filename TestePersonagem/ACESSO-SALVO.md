O login bem-sucedido guarda somente o token revogável de sessão. A senha não é armazenada. No Windows, o token fica no Gerenciador de Credenciais do usuário e em um arquivo cifrado com DPAPI por usuário, usado como alternativa quando o cofre não está acessível em outra execução. No Android, é cifrado com AES-GCM e chave não exportável do Android Keystore.

Ao reabrir o jogo, GET /v1/player valida a sessão e recupera o estado atual. Com personagem existente, aparece o botão verde JOGAR e o link Trocar conta. A entrada continua dependendo do clique e do carregamento normal. Sem personagem, abre a criação. Falhas de rede preservam o token e permitem tentar novamente, sem entrada offline.

Sair/trocar de conta, sessão expirada/revogada ou personagem bloqueado removem o acesso salvo. A validade continua sendo a do servidor (atualmente sete dias); não há renovação automática nem armazenamento da senha. Um novo login em outro dispositivo pode revogar o anterior conforme a regra atual da API.

A verificação offline --verify-arrival também cobre restauração, falha de rede, sessão expirada, bloqueio, conta sem personagem e remoção do acesso. No Windows, testa gravação/leitura/exclusão no cofre e no arquivo DPAPI com identificador exclusivo. Três execuções separadas com --vault-fixture write/read/clear e o mesmo GUID de teste confirmam persistência entre processos; a leitura apaga apenas a credencial fictícia do cofre para forçar a alternativa DPAPI. Credenciais reais do usuário não são lidas nem alteradas pelos testes.

O código Android pode ser compilado com javac contra android.jar sem gerar APK. A entrega mobile depende de uma nova versão do APK, a ser gerada/publicada somente mediante pedido explícito.
