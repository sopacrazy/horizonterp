## Horizonte Brasil 0.7.0

- Conexão com a VPS Brasil. O endereço anterior encaminha os aplicativos antigos para o mesmo serviço, preservando o certificado anterior e as sessões. Contas e progresso foram conciliados antes da troca.
- Voz por proximidade e rádio policial por canais, com botão de falar e indicação de microfone ativo. O uso de áudio requer permissão do microfone no Android.
- Serviço policial, identificação, procurados, algemas, condução, viatura, ocupante visível, multas, chamados e canais de comunicação.
- Guia `/helpadmin`, ajustes no painel administrativo e nos nomes sobre os personagens.
- Guia público `/ajuda`, grito `/s` por proximidade, sussurro privado `/w`, relatórios `/report` e `/myreports` persistidos e atalhos de GPS, celular, estatísticas e missões. Sistemas ainda ausentes ficam identificados como pendentes.
- Mapa de Los Santos em resolução maior, minimapa girando com o jogador e mapa ampliado com navegação por arrasto.
- Administradores podem marcar um destino no mapa e confirmar o teleporte. A chegada usa uma queda curta, valida o piso e antecipa um setor para reduzir a espera. Cancelar não teleporta.
- Entrada ajustada ao chão, tutorial inicial desativado durante os testes do novo mapa e melhorias locais de sessão e interface.
- Versão 0.7.0 exibida no carregamento e no HUD.

Android continua limitado a Los Santos, com carregamento por proximidade e perfil inicial para aparelhos com 4 GB de RAM. O pacote Windows preserva o mapa completo local. Áreas rurais e San Fierro ainda têm falhas visuais; NPCs e missões precisam de revisão de posições.

Instale o APK sobre a versão existente para preservar o acesso salvo. O jogo verifica a atualização ao iniciar, e o Android pede confirmação para instalar. Windows: extraia o ZIP completo e abra `Horizonte/Horizonte.exe`. No projeto local, use `Jogar.bat` após a build validada.

Esta distribuição não foi executada em um aparelho Android físico antes da publicação. Voz, rádio, animação da chegada e desempenho final devem ser avaliados nos aparelhos de teste.

Validações desta versão: 54 testes locais da API aprovados; integração da API ativa validada e contas temporárias removidas; entrada Windows com sessão lembrada aprovada; APK ARM64 de 293.933.163 bytes (280,3 MiB), assinatura compatível com a 0.5.9 e alinhamento de 16 KiB verificado.
