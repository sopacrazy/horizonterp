// The server is the authority for command names and required administrator levels.
// Commands without a game system are listed so admins receive a truthful response.
const entries=[
 ['ans',1,'/ans','Responder à denúncia pendente.','Denúncias'],
 ['mystats',1,'/mystats','Ver estatísticas de administração.'],
 ['coords',1,'/coords','Ver suas coordenadas.'],
 ['goto',1,'/goto [ID]','Teleportar até um jogador.'],
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
 ['t',3,'/t [0-23]','Alterar a hora da sala.']
];

export const ADMIN_COMMANDS=Object.freeze(entries.map(([name,level,usage,description,missing])=>
 Object.freeze({name,level,usage,description,missing:missing||null})));
const byName=new Map(ADMIN_COMMANDS.map(entry=>[entry.name,entry]));
byName.set('myastats',byName.get('mystats'));
export const adminCommand=name=>byName.get(String(name||'').toLowerCase())||null;
export const adminHelp=level=>ADMIN_COMMANDS.filter(entry=>entry.level<=level).map(entry=>
 `N${entry.level} ${entry.usage} — ${entry.description}${entry.missing?' (aguarda '+entry.missing+')':''}`);
