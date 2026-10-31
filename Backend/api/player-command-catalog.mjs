const entries=[
 ['s','/s [mensagem]','Gritar para jogadores em até 50 metros.'],
 ['w','/w [ID] [mensagem]','Sussurrar em privado para um jogador online.'],
 ['me','/me [ação]','Descrever uma ação em primeira pessoa.','Interpretação de ações'],
 ['do','/do [ação]','Descrever uma situação em terceira pessoa.','Interpretação de situações'],
 ['try','/try [ação]','Tentar uma ação com sucesso ou falha.','Catálogo de ações e regras de tentativa'],
 ['j','/j [mensagem]','Conversar no chat do trabalho.','Vínculo com trabalho e canal profissional'],
 ['stats','/stats','Consultar dados e estatísticas do personagem.'],
 ['myreports','/myreports','Consultar seus últimos 20 relatórios à administração.'],
 ['report','/report [mensagem]','Enviar um relatório à administração.'],
 ['gps','/gps','Abrir o mapa e navegador GPS.'],
 ['phone','/phone','Abrir o celular.'],
 ['mask','/mask','Colocar ou retirar máscara.','Máscaras'],
 ['healme','/healme','Utilizar um kit de primeiros socorros.','Inventário de kits e consumo validado pelo servidor'],
 ['house','/house','Abrir o menu da residência.','Residências'],
 ['biz','/biz','Abrir o menu do negócio.','Negócios'],
 ['daily','/daily','Consultar tarefas diárias.','Tarefas diárias'],
 ['quests','/quests','Consultar missões e progresso.'],
 ['v','/v [mensagem]','Conversar no chat VIP, quando possuir benefício ativo.','Benefícios e canal VIP'],
 ['headmove','/headmove','Parar ou retomar os movimentos da cabeça.'],
 ['anim','/anim','Abrir o menu de animações.'],
 ['ajuda','/ajuda','Abrir este guia de comandos do jogador.']
];
export const PLAYER_COMMANDS=Object.freeze(entries.map(([name,usage,description,missing])=>Object.freeze({name,usage,description,missing:missing||null})));
export const playerCommand=name=>PLAYER_COMMANDS.find(entry=>entry.name===name);
export const playerHelp=()=>PLAYER_COMMANDS.map(entry=>({usage:entry.usage,description:entry.description,group:'Jogador',level:0,
 permission:'Todos os jogadores',available:!entry.missing,note:entry.missing?'Aguarda '+entry.missing:entry.name==='anim'?'Catálogo de gestos em preparação':''}));
