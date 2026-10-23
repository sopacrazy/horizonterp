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
- Cadastro e login, separação entre conta e personagem e salvamento de progresso.
- Sala experimental para dois jogadores, sincronização de movimento e chat.
- Tipos de conta e nível administrativo. Os comandos administrativos são apenas placeholders: suas ações ainda não são executadas.
- Chat com eco local imediato e destaque verde para comandos administrativos.

A rede ainda é um protótipo de sincronização por HTTPS; a suavização não elimina a latência da conexão.

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
