# Atualizações Android

## Autorização para versões posteriores à 0.5.5

Por solicitação do responsável pelo projeto, novas versões do APK após a 0.5.5 só devem ser geradas ou publicadas mediante pedido explícito. Pedidos de ajustes no código, no HUD ou de testes no Windows não autorizam gerar ou publicar um novo APK.

O cliente a partir da versão 0.5.0 lê `android.json` durante o loading. O arquivo é servido por HTTPS em `raw.githubusercontent.com`; o APK é hospedado no GitHub Releases deste repositório. Nenhuma senha ou token GitHub fica no aplicativo.

O download é automático quando `versionCode` aumenta. Antes de instalar, o cliente valida origem, tamanho, SHA-256, pacote, versão e assinatura igual à instalação atual. O Android pede confirmação e, se necessário, permissão para instalar atualizações por este aplicativo. Instalação silenciosa não está implementada. O jogador pode cancelar e continuar na versão atual.

## Publicar a próxima versão

1. Atualize **version** e **versionCode** em `TestePersonagem/Assets/Resources/GameVersion.json`. Ambos devem identificar uma nova distribuição, inclusive em relação a APKs de teste locais. O código precisa ser maior que o anterior. `GameVersionBuildSetup` aplica esse arquivo automaticamente em todas as compilações, inclusive pela interface do Editor. Loading e HUD mostram `Application.version`, isto é, a versão realmente instalada; consultar uma atualização não troca esse número antes da instalação.
2. Compile Windows, execute verificações e gere o APK usando `AndroidSetup.Build`. Preserve a chave de assinatura usada nos APKs anteriores. A configuração atual usa a chave de desenvolvimento local: ela precisa ser mantida para atualizar os aparelhos de teste.
3. Execute `python tools/verify_android_release.py --version X.Y.Z --version-code N --log nome-do-build.log --chat --updater --stairs --walk --network --hud2 --admin-chat`. Os programas Android ficam fora do Git; configure-os localmente conforme o ambiente. Para preparar o atlas refinado de `personagem/hud novo.png` antes de compilar, use `Hud2Setup.BuildAndroid`.
4. Envie o código validado ao GitHub antes de criar a release.
5. Prepare as notas da versão e, com o GitHub CLI autenticado, execute `python tools/publish_android_update.py --apk TestePersonagem/Builds/Android/HorizonteBrasil-X.Y.Z.apk --notes-file caminho/das-notas.txt`.
6. O publicador cria a release com o APK e somente depois escreve o manifesto. Confira e faça commit/push de `releases/android.json` para oferecer a atualização.

Nunca publique o manifesto antes de o APK estar disponível, nem substitua o arquivo de uma versão já publicada. Em caso de erro, publique uma versão com código maior. Para interromper a oferta, altere `available` para `false` e envie o manifesto atualizado.

Recompilar para testes não aumenta o número sozinho. A próxima publicação define o novo número no arquivo único de configuração; o build e os textos dentro do jogo acompanham esse valor automaticamente.

APKs 0.4.1 e anteriores não têm o atualizador e precisam receber a 0.5.0 manualmente uma vez. A instalação deve usar o mesmo pacote e a mesma assinatura, preservando os dados existentes. Não desinstale o jogo para atualizar.

Falhas de rede ou de integridade não impedem a entrada no jogo atual; a verificação será repetida ao abrir o aplicativo. Windows mostra a versão, mas não instala APKs.
