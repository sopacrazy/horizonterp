# Horizonte Brasil RP

Protótipo de jogo em terceira pessoa para Windows e Android, desenvolvido em Unity.

## Estrutura

- `TestePersonagem/`: projeto Unity, cenas, scripts, animações e recursos visuais.
- `Backend/api/`: API Node.js de contas, personagens, persistência, sala de teste e chat.
- `Backend/database/`: migrações PostgreSQL, backup e verificações.
- `personagem/`: arquivos originais do personagem, animações e artes.
- `personagem_unity/`: arquivos de conversão do personagem e créditos.
- `tools/`: scripts de conversão e verificação; programas e SDKs instalados não são versionados.

## Abrir no Unity

1. Instale o Unity Editor **6000.6.7f1** pelo Unity Hub.
2. No Hub, adicione a pasta `TestePersonagem` como projeto existente.
3. Aguarde a importação dos recursos e pacotes.
4. Abra as cenas indicadas em `TestePersonagem/Assets/Editor/GameUpdateSetup.cs`.

Para Android, instale também Android Build Support, SDK, NDK e OpenJDK compatíveis com o editor. A configuração de build está em `Assets/Editor/AndroidSetup.cs`; confira os caminhos locais das ferramentas antes de compilar em outro computador.

No Windows, `AbrirProjeto.bat` abre o projeto na instalação padrão do editor. `Jogar.bat` executa uma build local já gerada; builds não estão incluídas neste repositório.

## Funcionalidades do protótipo

- Personagem com animações de parado, caminhada, corrida e pulo; câmera em terceira pessoa.
- HUD, controles por toque, introdução e lobby de criação de personagem.
- Cidade oficial na cena `TestePersonagem`, com cenário recuperado, materiais reconstruídos e colisões revisadas. O bairro anterior fica em backup local durante a integração.
- Cadastro e login, separação entre conta e personagem e salvamento de progresso.
- Sala experimental com dez vagas, sincronização de movimento e chat. Bots automáticos desativados.
- Tipos de conta e nível administrativo. Os comandos administrativos são apenas placeholders: suas ações ainda não são executadas.
- Modo de velocidade para administradores: no painel ADMIN, toque em **Velocidade** e escolha x2, x4, x6, x8, x10 ou x12. A opção x1 volta à velocidade normal; o modo rápido é desativado ao perder o acesso de administrador.
- Chat com eco local imediato e destaque verde para comandos administrativos.
- A partir da 0.5.0: versão no HUD/loading e atualização Android pelo GitHub Releases, com confirmação de instalação pelo Android.

A rede ainda é um protótipo de sincronização por HTTPS; a suavização não elimina a latência da conexão.

`VerMapaGTA.bat` abre uma prévia independente da cidade no Windows, sem conta ou multiplayer.
`Jogar.bat` abre a build oficial com a entrada normal e login. A cidade oficial é
configurada por `GtaOfficialCitySetup.CreateAndBuildWindows`; `GTAMapaTeste` permanece
uma cena de revisão separada. Os materiais novos ficam em `Assets/GTARecoveredMap/Restored`.
O pacote de origem tinha referências de materiais ausentes; a reconstrução não reproduz
integralmente sua aparência. Relatórios e imagens locais ficam em `tools/gta-map-review`.
Os limites ampliados da API foram implantados e validados por HTTPS para a versão 0.5.7,
preservando contas, posições e saldos. A cidade carrega 56 setores por proximidade;
`GameUpdateSetup.Scenes` inclui esses setores na compilação Android e Windows.

`VerBairro.bat` é o atalho legado de revisão do bairro anterior, com personagem de teste e
salvamento apenas em memória. Essa prévia não acessa a API da VPS e não é um modo
offline para contas reais. `Jogar.bat` continua abrindo a entrada normal com login.
O comando **Horizonte > Montar bairro com praca central** permanece como ferramenta
histórica e substitui o cenário; não faz parte da geração da cidade oficial.
O backend mantém o mundo e os saves existentes; a ampliação não exige migração SQL.

As versões são definidas em `TestePersonagem/Assets/Resources/GameVersion.json`. Veja [como publicar atualizações Android](releases/README.md). APKs anteriores à 0.5.0 precisam de uma instalação manual para receber o atualizador.

## Backend e testes

Consulte [a documentação da API](Backend/api/README.md) e [a documentação do banco](Backend/README.md). As migrações estão numeradas de 001 a 005. Credenciais são configuradas exclusivamente no ambiente do servidor e não devem ser adicionadas ao projeto Unity ou ao Git.

Para executar os testes locais da API, com Node.js instalado:

```sh
cd Backend/api
npm ci
node --test test/characters.test.mjs test/multiplayer.test.mjs
```

Os testes locais usam PostgreSQL em memória. Verificadores de integração e rotinas de implantação exigem um ambiente próprio configurado; não devem ser executados contra uma VPS sem conferir seus parâmetros.

O certificado público e o pin usados pelo cliente não são credenciais privadas. Configurações de ambiente estão em `TestePersonagem/Assets/Resources/ApiConfig.json`. Nunca publique a chave privada TLS, senhas, tokens, dumps do banco ou arquivos de contas temporárias.

## Recursos e créditos

Os créditos originais do personagem foram preservados nos diretórios de recursos. Assets de terceiros continuam sujeitos às respectivas licenças; sua presença neste repositório não concede licença adicional de redistribuição.
