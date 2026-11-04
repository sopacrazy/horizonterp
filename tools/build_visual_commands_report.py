from pathlib import Path
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(r'C:\SistemaAdr\gta')
OUT = ROOT / 'reports' / 'Relatorio_Telas_Visual_Comandos_HBRP_2026-10-30.docx'
OUT.parent.mkdir(exist_ok=True)
d = Document()
s = d.sections[0]
s.page_width, s.page_height = Inches(8.5), Inches(11)
s.top_margin = Inches(.7); s.bottom_margin = Inches(.65)
s.left_margin = Inches(.78); s.right_margin = Inches(.78)

normal = d.styles['Normal']; normal.font.name='Aptos'; normal.font.size=Pt(10.5)
normal.font.color.rgb=RGBColor(35,43,53)
normal.paragraph_format.space_after=Pt(6)
for name,size,before,after in [('Title',20,0,9),('Heading 1',13,13,5),('Heading 2',11,9,4)]:
    st=d.styles[name]; st.font.name='Aptos Display' if name=='Title' else 'Aptos'
    st.font.size=Pt(size); st.font.bold=True; st.font.color.rgb=RGBColor(19,33,49)
    st.paragraph_format.space_before=Pt(before); st.paragraph_format.space_after=Pt(after)
ppr=d.styles['Title']._element.get_or_add_pPr()
for b in ppr.findall(qn('w:pBdr')): ppr.remove(b)

def p(t='',style=None): return d.add_paragraph(t,style=style)
def h(t,n=1): return d.add_heading(t,level=n)
def shade(cell,fill):
    tc=cell._tc.get_or_add_tcPr(); sh=OxmlElement('w:shd'); sh.set(qn('w:fill'),fill); tc.append(sh)
def border(cell):
    tc=cell._tc.get_or_add_tcPr(); bs=tc.first_child_found_in('w:tcBorders')
    if bs is None: bs=OxmlElement('w:tcBorders'); tc.append(bs)
    for side in ('top','left','bottom','right'):
        el=OxmlElement('w:'+side); el.set(qn('w:val'),'single'); el.set(qn('w:sz'),'4'); el.set(qn('w:color'),'D9D9D9'); bs.append(el)
def table(headers,rows,widths):
    t=d.add_table(rows=1,cols=len(headers)); t.autofit=False; t.alignment=WD_TABLE_ALIGNMENT.CENTER
    t.style='Table Grid'
    for i,x in enumerate(headers):
        c=t.rows[0].cells[i]; c.text=x; shade(c,'20384E')
        for run in c.paragraphs[0].runs: run.bold=True; run.font.color.rgb=RGBColor(255,255,255)
    for ri,row in enumerate(rows):
        cells=t.add_row().cells
        for j,x in enumerate(row):
            cells[j].text=str(x)
            if ri%2: shade(cells[j],'F3F7FA')
    for row in t.rows:
        for j,c in enumerate(row.cells):
            c.width=Inches(widths[j]); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER; border(c)
            for pp in c.paragraphs:
                pp.paragraph_format.space_before=Pt(2); pp.paragraph_format.space_after=Pt(2)
                for run in pp.runs: run.font.size=Pt(9)
    for c in t.rows[0].cells:
        for pp in c.paragraphs:
            for run in pp.runs: run.font.size=Pt(9)
    t.rows[0]._tr.get_or_add_trPr().append(OxmlElement('w:tblHeader'))

d.add_paragraph('Telas visuais acessos e comandos do Horizonte Brasil RP','Title')
m=p('Inventário funcional para supervisão  |  30 de outubro de 2026')
m.runs[0].font.size=Pt(9); m.runs[0].font.color.rgb=RGBColor(92,104,117)
p('Este relatório registra o que o jogador encontra no cliente Unity local, quem pode acessar cada função e quais comandos o servidor reconhece. É uma fotografia do desenvolvimento, não uma confirmação de que todos os recursos já foram testados no Android. O APK público 0.6.0 contém uma versão anterior a parte dos ajustes policiais e de interface descritos aqui.')

h('Fluxo visual e telas disponíveis')
table(['Tela ou área','O que aparece','Acesso'],[
('Abertura e atualização','Arte de carregamento, barra de progresso, versão do jogo e verificação de atualização.','Todos'),
('Conta','Cadastro por e-mail, login, senha visível/oculta e recuperação da sessão salva.','Antes de entrar'),
('Personagem','Criação com nome, sobrenome e sexo; prévia visual; lobby do personagem salvo com botão para entrar ou trocar conta.','Conta autenticada'),
('Cidade e HUD','Personagem em terceira pessoa, minimapa, dinheiro, nível/XP, barras vitais, chat, ações, estado da conexão e atalhos.','Jogador em sessão'),
('Central do jogador','Menu de acesso às telas secundárias; fecha para voltar ao jogo.','Jogador em sessão'),
('Administração','Lista paginada de jogadores, busca por primeiro nome ou ID, ações de moderação e teste de velocidade.','Administrador'),
('Polícia','Estado de serviço, rádio por canal, lista de procurados em modal de papel branco e ações pelo chat.','Policial em serviço'),
('Missões e treino','Catálogo de missões, diálogos, objetivos, sala de tiro e contador de acertos/disparos.','Jogador em sessão')
],[1.35,4.65,.85])

h('Central do jogador')
p('O menu principal organiza dez acessos. Algumas telas já são funcionais; outras mostram dados de teste ou atalhos, conforme a evolução dos sistemas.')
table(['Acesso','Conteúdo atual'],[
('Personagem','Primeiro nome, ID, organização, salvamento e estatísticas de deslocamento.'),
('Inventário','Documentos de teste do tutorial, RG/CNH quando emitidos; estado vazio quando não há itens.'),
('Mapa e GPS','Localização e navegação; atalho de retorno ao ponto inicial de teste.'),
('Ações','Soco, centralizar câmera, reposicionar personagem e ajuda dos controles.'),
('Minha sessão','Distância percorrida, tempo de exploração e velocidade máxima.'),
('Missões','Lista e estado das missões do catálogo.'),
('Carteira','Saldo e elementos de economia de teste; compra/doação ainda não representa fluxo final.'),
('Celular','Atalhos para outras telas; ainda não é um telefone completo.'),
('Configurações','Sensibilidade e distância da câmera.'),
('Como jogar','Comandos de teclado e mouse.'),
],[1.6,5.25])

d.add_page_break()
h('Controles e comunicação')
table(['Entrada','Ação atual'],[
('Windows','WASD movimenta; Shift corre; Espaço pula; mouse controla a câmera; Tab abre menu; M abre mapa; Esc fecha janela. Há teclas de ação, câmera e interface na ajuda do jogo.'),
('Android','Área livre à esquerda controla movimento; arrasto à direita controla câmera; botões de corrida, pulo, soco, menus, chat e microfone.'),
('Chat','Toque ou clique abre a caixa de texto sob a conversa. Comandos iniciados por barra são enviados ao servidor.'),
('Voz','Microfone e botão de segurar para falar; rádio policial usa canal e permissão de serviço. Requer teste entre aparelhos reais.'),
],[1.35,5.5])

h('Acessos e permissões')
table(['Perfil','Pode acessar'],[
('Jogador','Conta, personagem, cidade, HUD, menus gerais, chat e pedido policial /190.'),
('Polícia em serviço','Comandos policiais, lista de procurados, viatura, detenção, multas, mensagens da organização e rádio; o servidor verifica o vínculo com a PM e o estado de serviço.'),
('Admin nível 1','Comandos básicos de moderação, observação, chat da equipe, estatísticas, guia /helpadmin e velocidade de teste.'),
('Admin nível 2','Inclui banimento, respawn, painel administrativo e comunicado /gov quando em serviço policial.'),
('Admin nível 3','Inclui desbanimento, mudança de nível administrativo, teleporte de terceiros, anúncios e demais comandos avançados implementados.'),
],[1.45,5.4])
p('O painel foi planejado para permitir alterações apenas em jogadores e administradores de nível inferior, sem alteração da própria conta. A autorização final deve permanecer no servidor.')

h('Comandos de polícia registrados')
table(['Grupo','Comandos','Uso'],[
('Identificação e procura','/ide, /wanted, /su, /clear','Identificação, consulta, inclusão e remoção de procurados.'),
('Custódia','/cuff, /uncuff, /follow, /stay','Algemar, soltar e conduzir o detido.'),
('Viatura','/carpolicia, /put, /eject','Disponibilizar viatura e colocar/retirar detido.'),
('Atendimento e multa','/ticket, /proc; /190 para todos','Registrar multa, aceitar chamado e pedir atendimento.'),
('Comunicação','/m, /f, /d, /gov','Megafone, organização, organizações e anúncio governamental; /gov exige admin nível 2.'),
],[1.35,2.5,3.0])
p('Esses comandos constam no catálogo e têm tratamento no servidor. O comportamento visual e a sincronização entre dois jogadores ainda exigem testes completos, especialmente custódia, viatura, rádio e chegada de chamados.')

d.add_page_break()
h('Comandos administrativos disponíveis no catálogo')
table(['Nível','Comandos com tratamento atual'],[
('1','/mystats (alias /myastats), /coords, /goto, /mute, /unmute, /kick, /re, /reoff, /a, /alist, /ahelp, /helpadmin, /speed'),
('2','/ban, /spawn, /apanel'),
('3','/unban, /makeadmin, /astats, /gethere, /aad, /inviz, /removeinviz, /returninviz, /t'),
],[.85,6.0])
p('O comando /helpadmin abre um modal com busca e filtros de Administração, Polícia e Atendimento. O guia também mostra a permissão e indica funções que aguardam outros sistemas.')
table(['Nível','Comandos listados mas dependentes de sistemas ausentes'],[
('1','/ans, /muterep, /unmuterep (denúncias); /inf (dano e combate); /jail, /unjail (prisões).'),
('2','/hpall (cura multiplayer); /spveh, /getcar, /carid (veículos).'),
('3','/offammo (armas); /warn, /unwarn (advertências); /dismiss, /templeader, /makeleader (facções); /skin, /myskin (skins); /bandev, /unbandev (dispositivos); /money (concessões); /veh (veículos); /gwfreeze, /gzcolor, /gzreset (gangues); /settp (teletransporte público).'),
],[.85,6.0])

h('Estado visual e próximos testes')
p('A interface atual usa HUD transparente sobre a cidade, ícones laterais, modais escuros e indicadores próprios para admin/polícia. A lista de procurados usa aparência de documento em papel. Nome e ID sobre os personagens têm contorno para leitura sobre o cenário; a identificação de polícia fica em linha separada.')
p('O catálogo de missões contém 17 entradas, das quais três estão marcadas como implementadas. O tutorial inicial está desativado nos testes atuais. As posições de NPCs e objetivos precisam de revisão no mapa Los Santos. Login por Google/Facebook, telefone completo, garagem e economia final ainda não estão disponíveis.')
p('Para homologar este inventário, faltam percorrer cada tela em resoluções de computador e celular, validar botões de toque, testar voz e comandos com dois jogadores reais e confirmar permissões administrativas no servidor. Nenhum desses testes está declarado como concluído neste relatório.')

f=s.footer.paragraphs[0]; f.text='Horizonte Brasil RP  |  Inventário funcional local  |  30/10/2026'; f.alignment=WD_ALIGN_PARAGRAPH.CENTER
for run in f.runs: run.font.size=Pt(8); run.font.color.rgb=RGBColor(110,119,128)
d.save(OUT)
print(OUT)
