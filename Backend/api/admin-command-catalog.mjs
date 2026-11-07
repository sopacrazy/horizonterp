import {playerHelp} from './player-command-catalog.mjs';
// The server is the authority for command names and required administrator levels.
// Commands without a game system are listed so admins receive a truthful response.
const entries=[
 ['ans',1,'/ans','Responder à denúncia pendente.','Denúncias'],
 ['mystats',1,'/mystats','Ver estatísticas de administração.'],
 ['coords',1,'/coords','Ver suas coordenadas.'],
 ['goto',1,'/goto [ID]','Teleportar até um jogador.'],
 ['bot1',1,'/bot1','Criar um bot temporário à sua frente para testes.'],
 ['mute',1,'/mute [ID] [minutos] [motivo]','Silenciar um jogador.'],
 ['muterep',1,'/muterep [ID] [minutos] [motivo]','Bloquear denúncias de um jogador.','Denúncias'],
 ['unmute',1,'/unmute [ID]','Remover o silêncio de um jogador.'],
 ['unmuterep',1,'/unmuterep [ID]','Liberar denúncias de um jogador.','Denúncias'],
 ['kick',1,'/kick [ID] [motivo]','Expulsar um jogador da sessão.'],
 ['re',1,'/re [ID]','Observar um jogador.'],
 ['reoff',1,'/reoff','Parar de observar.'],
 ['inf',1,'/inf [ID]','Alternar imortalidade.','Dano e combate'],
 ['jail',1,'/jail [ID] [minutos] [motivo]','Aplicar pena administrativa.'],
 ['unjail',1,'/unjail [ID]','Encerrar uma pena antes do prazo.'],
 ['a',1,'/a [mensagem]','Enviar mensagem apenas aos admins.'],
 ['alist',1,'/alist','Listar administradores online.'],
 ['ahelp',1,'/ahelp [nível]','Mostrar comandos disponíveis.'],
 ['helpadmin',1,'/helpadmin','Abrir o guia completo de comandos do jogo.'],
 ['trocarsexo',1,'/trocarsexo [feminino|masculino]','Alterar o sexo do próprio personagem.'],
 ['speed',1,'/speed [1|2|4|6|8|10|12]','Ajustar sua velocidade de exploração.'],
 ['ban',2,'/ban [ID/nome] [dias] [motivo]','Banir um jogador por dias.'],
 ['hpall',2,'/hpall [raio]','Curar jogadores próximos.','Cura multiplayer'],
 ['spveh',2,'/spveh [raio]','Respawn de veículos próximos.','Veículos'],
 ['spawn',2,'/spawn [ID]','Respawnar um jogador.'],
 ['apanel',2,'/apanel','Abrir a lista administrativa de jogadores.'],
 ['getcar',2,'/getcar [ID veículo]','Trazer veículo até você.','Veículos'],
 ['carid',2,'/carid','Consultar o ID de um veículo.','Veículos'],
 ['unban',3,'/unban [nome]','Desbanir um jogador.'],
 ['offammo',3,'/offammo [ID]','Remover armas de um jogador.','Armas'],
 ['warn',3,'/warn [ID/nome] [motivo]','Advertir um jogador.','Advertências'],
 ['unwarn',3,'/unwarn [ID/nome]','Remover uma advertência.','Advertências'],
 ['dismiss',3,'/dismiss [ID]','Demitir jogador de facção.','Facções'],
 ['skin',3,'/skin [ID] [skin]','Trocar a skin de um jogador.','Skins'],
 ['templeader',3,'/templeader [ID]','Nomear líder temporário.','Facções'],
 ['bandev',3,'/bandev [ID/apelido] [dias] [motivo]','Banir um dispositivo.','Identificação de dispositivos'],
 ['unbandev',3,'/unbandev [apelido]','Desbanir um dispositivo.','Identificação de dispositivos'],
 ['makeleader',3,'/makeleader [ID]','Nomear líder de facção.','Facções'],
 ['money',3,'/money [quantidade]','Conceder dinheiro.','Concessões financeiras'],
 ['veh',3,'/veh [ID]','Criar veículo.','Veículos'],
 ['makeadmin',3,'/makeadmin [ID] [nível]','Alterar nível de administrador.'],
 ['gwfreeze',3,'/gwfreeze','Abrir congelamento de guerras.','Guerras de gangues'],
 ['gzcolor',3,'/gzcolor','Repintar zona de gangue.','Zonas de gangues'],
 ['gzreset',3,'/gzreset','Redefinir zona de gangue.','Zonas de gangues'],
 ['settp',3,'/settp','Definir teletransporte público.','Marcadores de teletransporte'],
 ['myskin',3,'/myskin [skin]','Trocar a própria skin.','Skins'],
 ['astats',3,'/astats [ID/apelido]','Consultar estatísticas administrativas.'],
 ['gethere',3,'/gethere [ID]','Trazer um jogador até você.'],
 ['aad',3,'/aad [mensagem]','Anunciar mensagem a todos.'],
 ['inviz',3,'/inviz','Alternar invisibilidade administrativa.'],
 ['removeinviz',3,'/removeinviz [ID]','Desativar invisibilidade de admin.'],
 ['returninviz',3,'/returninviz [ID]','Ativar invisibilidade de admin.'],
 ['t',3,'/t [0-23]','Alterar a hora da sala.'],
 ['adminpromov',4,'/adminpromov [ID/nick] [1|2]','Promover um jogador a administrador nível 1 ou 2.']
];

export const ADMIN_COMMANDS=Object.freeze(entries.map(([name,level,usage,description,missing])=>
 Object.freeze({name,level,usage,description,missing:missing||null})));
const byName=new Map(ADMIN_COMMANDS.map(entry=>[entry.name,entry]));
byName.set('myastats',byName.get('mystats'));
export const adminCommand=name=>byName.get(String(name||'').toLowerCase())||null;
// Level 1 is the unrestricted administrator. Level 4 remains a specialized
// appointment role when used by itself.
export function adminCommandAllowed(level,name){
 const entry=adminCommand(name);
 if(!entry||!Number.isInteger(level)||level<1)return false;
 if(level===1)return true;
 if(level===4||entry.name==='adminpromov')return level===4&&entry.name==='adminpromov';
 return level>=entry.level;
}
export const adminHelp=level=>ADMIN_COMMANDS.filter(entry=>adminCommandAllowed(level,entry.name)).map(entry=>
 `N${entry.level} ${entry.usage} — ${entry.description}${entry.missing?' (aguarda '+entry.missing+')':''}`);

const policeEntries=[
 ['/ide [ID]','Mostrar sua identificação policial ao jogador.'],
 ['/wanted','Consultar a lista de procurados.'],
 ['/su [ID] [1-6 estrelas] [motivo]','Adicionar procurado e registrar motivo.'],
 ['/clear [ID]','Retirar o status de procurado.'],
 ['/cuff [ID|BOT1]','Algemar um jogador próximo ou seu bot de teste.'],
 ['/uncuff [ID]','Retirar as algemas.'],
 ['/follow [ID|BOT1]','Conduzir o detido algemado ou seu bot de teste.'],
 ['/stay','Parar a condução.'],
 ['/carpolicia','Posicionar a viatura policial.'],
 ['/put [ID]','Colocar o detido algemado na viatura.'],
 ['/eject [ID]','Retirar o detido da viatura.'],
 ['/ticket [ID] [R$] [motivo]','Registrar uma multa pendente.'],
 ['/proc [ID]','Aceitar a chamada 190 mais recente do jogador.'],
 ['/m [texto]','Falar pelo megafone perto da viatura.'],
 ['/f [mensagem]','Enviar mensagem à organização.'],
 ['/d [mensagem]','Enviar mensagem às organizações.'],
 ['/gov [mensagem]','Publicar comunicado para todos (admin nível 2).']
];
const policeOperations=[
 ['/servico [on|off]','Entrar ou sair de serviço na PM.','PM ou administração'],
 ['/pmrecrutar [ID]','Recrutar jogador para a PM.','Comando ou administração'],
 ['/pmpatente [ID] [recruta|oficial|comando]','Alterar a patente. Alterações do Comando exigem administração.','Comando ou administração'],
 ['/pmdemitir [ID]','Remover um membro da PM.','Comando ou administração'],
 ['/delegacia','Marcar a delegacia; Oficial ou Comando em serviço entra ou sai da custódia quando está no local.'],
 ['/cela [minutos] [motivo]','Colocar na cela o preso algemado que acompanha o policial dentro da custódia.','Oficial ou Comando em serviço'],
 ['/prender [ID] [minutos] [motivo]','Registrar a ocorrência e prender um detido na delegacia.','Oficial ou Comando em serviço'],
 ['/soltar [ID|BOT1]','Encerrar antecipadamente a pena de um preso ou liberar seu bot de teste na delegacia.','Oficial ou Comando em serviço'],
 ['/ocorrencias [ID]','Consultar as últimas penas registradas de um jogador.','Oficial ou Comando em serviço'],
 ['/chamados','Consultar fila do 190 e chamados da patrulha.'],
 ['/despachar190 [ID solicitante] [ID policial]','Atribuir um chamado a outro policial em serviço.','Comando em serviço'],
 ['/gpsocorrencia','Marcar no mapa a posição registrada no chamado aceito.'],
 ['/encerrar190','Encerrar o chamado aceito pela patrulha.'],
 ['/cancelar190','Cancelar seu chamado mais recente.','Todos os jogadores'],
 ['/minhasmultas','Consultar suas multas e códigos para pagamento.','Todos os jogadores'],
 ['/pagarmulta [código]','Pagar uma multa com o saldo em dinheiro.','Todos os jogadores'],
 ['/cancelarmulta [código]','Cancelar uma multa pendente.','Oficial responsável ou Comando em serviço'],
 ['/revistar [ID]','Consultar inventário do jogador sob custódia.'],
 ['/documentos [ID]','Consultar RG e CNH de um jogador próximo.'],
 ['/placa [placa]','Consultar proprietário de veículo registrado ou viatura.'],
 ['/apreender [ID] [item] [quantidade]','Remover itens de um jogador sob custódia e registrar a apreensão.','Oficial ou Comando em serviço'],
 ['/arsenal [pistola|rifle|guardar]','Retirar ou guardar equipamento na delegacia. Rifle exige Oficial ou Comando.'],
 ['/giroflex','Alternar as luzes de emergência da viatura que você dirige.'],
 ['/sirene','Alternar a sirene da viatura que você dirige.']
];

export function gameCommandHelp(){
 const admin=ADMIN_COMMANDS.map(entry=>({usage:entry.usage,description:entry.description,
  group:'Administração',level:entry.level,permission:entry.name==='adminpromov'?'Admin nível 1 ou cargo nível 4':`Admin nível 1 ou nível ${entry.level}+`,
  available:!entry.missing,note:entry.missing?`Aguarda ${entry.missing}`:''}));
 admin.push({usage:'/myastats',description:'Atalho para /mystats: consultar suas estatísticas de administrador.',
  group:'Administração',level:1,permission:'Admin nível 1',available:true,note:''});
 const police=policeEntries.map(([usage,description])=>({usage,description,
  group:'Polícia',level:usage.startsWith('/gov ')?2:1,
  permission:usage.startsWith('/gov ')?'Polícia em serviço · Admin nível 2':'Polícia em serviço',
  available:true,note:''}));
 const operations=policeOperations.map(([usage,description,permission='Polícia em serviço'])=>({
  usage,description,permission,group:permission==='Todos os jogadores'?'Atendimento':'Polícia',level:0,available:true,note:''
 }));
 return [...playerHelp(),...admin,...police,...operations,{usage:'/190 [mensagem]',description:'Pedir atendimento policial.',
  group:'Atendimento',level:0,permission:'Todos os jogadores',available:true,note:''}];
}
