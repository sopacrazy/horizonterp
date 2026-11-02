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
 ['jail',1,'/jail [ID/nome] [minutos] [motivo]','Prender um jogador.','Prisões'],
 ['unjail',1,'/unjail [ID/nome]','Soltar um jogador.','Prisões'],
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
// Level 4 is a specialized appointment role, not an inherited super-admin.
export function adminCommandAllowed(level,name){
 const entry=adminCommand(name);
 if(!entry||!Number.isInteger(level)||level<1)return false;
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
 ['/cuff [ID]','Algemar um jogador próximo.'],
 ['/uncuff [ID]','Retirar as algemas.'],
 ['/follow [ID]','Conduzir o detido algemado.'],
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

export function gameCommandHelp(){
 const admin=ADMIN_COMMANDS.map(entry=>({usage:entry.usage,description:entry.description,
  group:'Administração',level:entry.level,permission:entry.name==='adminpromov'?'Exclusivo do admin nível 4':`Admin nível ${entry.level}`,
  available:!entry.missing,note:entry.missing?`Aguarda ${entry.missing}`:''}));
 admin.push({usage:'/myastats',description:'Atalho para /mystats: consultar suas estatísticas de administrador.',
  group:'Administração',level:1,permission:'Admin nível 1',available:true,note:''});
 const police=policeEntries.map(([usage,description])=>({usage,description,
  group:'Polícia',level:usage.startsWith('/gov ')?2:1,
  permission:usage.startsWith('/gov ')?'Polícia em serviço · Admin nível 2':'Polícia em serviço',
  available:true,note:''}));
 return [...playerHelp(),...admin,...police,{usage:'/190 [mensagem]',description:'Pedir atendimento policial.',
  group:'Atendimento',level:0,permission:'Todos os jogadores',available:true,note:''}];
}
