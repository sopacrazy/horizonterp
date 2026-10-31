## Horizonte Brasil 0.7.1

- Novo movimento multiplayer com Unity Transport por UDP criptografado, atualizações a 20 Hz e interpolação dos outros personagens. O jogo mantém a conexão HTTPS como alternativa quando UDP estiver indisponível.
- Integração do Vivox para voz por proximidade e canais de rádio, com tokens emitidos pela API do Horizonte. A chave do serviço permanece somente no servidor.
- Minimap com giro suave e seta azul do jogador, incluindo o mapa ampliado.
- Melhorias no recebimento e reprodução contínua do áudio do sistema anterior e na sincronização das animações dos jogadores remotos.
- Versão 0.7.1 exibida no jogo e no carregamento.

Os dois jogadores devem instalar esta versão para testar juntos o novo movimento e a nova voz. Autorize o microfone quando o Android solicitar. Use o botão do microfone para voz de proximidade; o rádio continua sujeito ao serviço e ao canal policial.

Instale o APK por cima da versão existente. O jogo verifica a atualização ao iniciar; a instalação requer confirmação do Android. No Windows, extraia o ZIP completo e abra Horizonte/Horizonte.exe.

O Android continua contendo somente Los Santos. O mapa completo permanece preservado no projeto e no pacote Windows. Não houve teste desta versão em Android físico antes da publicação; o desempenho e o áudio por Wi-Fi/4G devem ser avaliados nos aparelhos de teste.

Validações: 59 testes locais da API aprovados; dois clientes Unity autenticados por UDP/DTLS na VPS e conectados ao mesmo canal real do Vivox; entrada Windows com sessão lembrada aprovada; contas temporárias removidas e 14 contas/14 personagens preservados. APK ARM64 de 311.307.212 bytes (296,9 MiB), com a mesma assinatura da versão anterior e alinhamento de 16 KiB verificado.
