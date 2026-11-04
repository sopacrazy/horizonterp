from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.style import WD_STYLE_TYPE
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from pathlib import Path

OUT = Path(r'C:\SistemaAdr\gta\reports\Relatorio_Horizonte_Brasil_RP_2026-10-30.docx')
OUT.parent.mkdir(parents=True, exist_ok=True)
d = Document()
s = d.sections[0]
s.page_width, s.page_height = Inches(8.5), Inches(11)
s.top_margin = Inches(.68); s.bottom_margin = Inches(.65)
s.left_margin = Inches(.78); s.right_margin = Inches(.78)

styles = d.styles
normal = styles['Normal']; normal.font.name = 'Aptos'; normal.font.size = Pt(10.5)
normal.font.color.rgb = RGBColor(35, 43, 53)
normal.paragraph_format.space_after = Pt(6)
normal.paragraph_format.line_spacing = 1.12
for name, size, before, after in [('Title', 20, 0, 9), ('Heading 1', 13, 13, 5), ('Heading 2', 11, 9, 4)]:
    st = styles[name]
    st.font.name = 'Aptos Display' if name == 'Title' else 'Aptos'
    st.font.size = Pt(size); st.font.bold = True; st.font.color.rgb = RGBColor(19, 33, 49)
    st.paragraph_format.space_before = Pt(before); st.paragraph_format.space_after = Pt(after)
title_ppr = styles['Title']._element.get_or_add_pPr()
for border in title_ppr.findall(qn('w:pBdr')):
    title_ppr.remove(border)
if 'Meta' not in styles:
    styles.add_style('Meta', WD_STYLE_TYPE.PARAGRAPH)
styles['Meta'].font.name = 'Aptos'; styles['Meta'].font.size = Pt(9)
styles['Meta'].font.color.rgb = RGBColor(92, 104, 117)
styles['Meta'].paragraph_format.space_after = Pt(10)

def p(text='', style=None):
    return d.add_paragraph(text, style=style)
def h(text, level=1):
    d.add_heading(text, level=level)
def bullet(text):
    return d.add_paragraph(text, style='List Bullet')
def cellshade(cell, fill):
    tcPr=cell._tc.get_or_add_tcPr(); shd=OxmlElement('w:shd'); shd.set(qn('w:fill'),fill); tcPr.append(shd)
def table(headers, rows, widths=None):
    t=d.add_table(rows=1, cols=len(headers)); t.alignment=WD_TABLE_ALIGNMENT.CENTER
    t.style='Light Shading Accent 1'
    for j,txt in enumerate(headers):
        c=t.rows[0].cells[j]; c.text=txt; cellshade(c,'E8EDF1')
        for run in c.paragraphs[0].runs: run.bold=True
    for row in rows:
        cells=t.add_row().cells
        for j,txt in enumerate(row): cells[j].text=txt
    if widths:
        t.autofit=False
        for row in t.rows:
            for j,w in enumerate(widths): row.cells[j].width=Inches(w)
    for row in t.rows:
        for c in row.cells:
            c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            for pp in c.paragraphs:
                pp.paragraph_format.space_after=Pt(2); pp.paragraph_format.space_before=Pt(2)
                for run in pp.runs: run.font.size=Pt(9)
    d.add_paragraph().paragraph_format.space_after=Pt(0)

d.add_paragraph('Relatório de desenvolvimento do Horizonte Brasil RP', 'Title')
d.add_paragraph('30 de outubro de 2026  |  Para a supervisão do projeto  |  Situação verificada no projeto local e nos servidores', 'Meta')
p('O Horizonte Brasil RP já tem um protótipo jogável em Unity para Windows e Android, com conta, personagem, mapa, interface, multiplayer experimental e persistência. A versão Android publicada é a 0.6.0, limitada a Los Santos. A nova VPS está preparada e testada, mas a troca dos jogadores ainda não foi concluída porque o APK instalado usa o endereço e o certificado da VPS antiga.')

h('Situação atual')
table(['Área', 'Estado verificado'], [
    ('Cliente Windows', 'Projeto Unity local em desenvolvimento e testes pelo Editor ou pelo atalho Jogar.'),
    ('Android', 'APK 0.6.0 publicado para teste, 287.978.487 bytes (274,6 MiB), com atualização via GitHub Releases.'),
    ('Servidores', 'API antiga ativa para APKs instalados; API nova 0.3.0 ativa em VPS separada, ainda sem corte definitivo.'),
    ('Banco', 'Snapshot migrado com 10 migrações, 14 contas e 14 personagens ativos; testes temporários removidos.'),
], [1.55, 5.3])

h('Jogo e experiência do jogador')
bullet('Personagem 3D com estados de parado, caminhada, corrida e pulo; câmera em terceira pessoa e controles para teclado e toque.')
bullet('Entrada com cadastro e login, criação separada de personagem, seleção de sexo, lobby e recuperação do acesso salvo.')
bullet('HUD com minimapa, status, menus, chat, identificação de jogador, controles móveis e indicação da versão em execução.')
bullet('Protótipos de direção com veículo emprestado, portal para interior e sala de treino de tiro; esses sistemas ainda exigem revisão visual e de jogabilidade.')
bullet('Missões 01 a 03 estão marcadas como implementadas no catálogo de 17 missões. O tutorial inicial foi desabilitado para testes e os NPCs, rotas e objetivos precisam ser reposicionados no mapa novo. As demais missões estão catalogadas, sem implementação completa.')

h('Mapa e pacote Android')
p('O mapa completo permanece no projeto Unity para expansão futura. O pacote Android 0.6.0 inclui somente 146 setores de Los Santos, carregados por proximidade, com meta de até nove setores ativos e 30 FPS em aparelhos com 4 GB de RAM. As texturas Android usam ASTC 8×8 com limite de 512 pixels; os colisores foram preservados. A meta de desempenho ainda precisa de medição em aparelho físico e em partidas com dois jogadores.')
p('O atualizador consulta um manifesto no GitHub, baixa o APK e confere integridade, pacote, versão e assinatura. A instalação depende da confirmação do Android; não é silenciosa. As regiões rurais e San Fierro permanecem fora do APK atual e têm falhas visuais conhecidas no mapa completo.')

h('Contas, administração e comunicação')
bullet('Backend Node.js com PostgreSQL para contas, personagens, posição, atributos, saldos, inventário, sessões e progresso. O cliente não define saldos ou privilégios por conta própria.')
bullet('Sala multiplayer de teste com até dez vagas, posição, animação remota e chat. Há suavização de movimento, mas a qualidade com dois jogadores ainda precisa de validação na nova conexão.')
bullet('Papéis de usuário e administrador, níveis de acesso, painel de jogadores e comandos administrativos. Parte dos comandos do catálogo ainda é apenas informativa ou está em implementação.')
bullet('Organização policial e comandos de identificação, procurados, algemas, condução, viatura, multas, chamados e comunicação aparecem no código atual. A integração completa de cada ação deve ser testada; esses recursos locais não estão todos confirmados no APK publicado.')
bullet('Voz por microfone e rádio policial por canal estão em desenvolvimento no cliente e na API. Ainda falta teste real com dois aparelhos, permissões de microfone e qualidade de áudio.')

h('Infraestrutura e migração')
p('A VPS antiga permanece ativa para os APKs instalados. Na nova VPS foi criado um ambiente isolado do HBRP: PostgreSQL 18 na porta local 55432, API HTTPS 0.3.0 na porta 8443, certificado próprio, credenciais internas novas e backup diário com retenção de 14 dias. Outros projetos da VPS antiga não foram alterados. O projeto Unity neste computador já aponta para a VPS nova; o Android publicado ainda aponta para a antiga.')
p('O teste oficial da API nova passou para cadastro, login, criação concorrente de personagem, isolamento entre contas, salvamento e recuperação de dados. Após a limpeza das contas temporárias, permaneceram 14 contas e 14 personagens. Uma falha no retorno de cameraYaw foi corrigida durante essa validação. O endpoint de saúde respondeu com HTTP 200.')
table(['Medição deste computador', 'VPS nova', 'VPS antiga'], [
    ('Ping médio, dez pacotes', '55 ms', '226 ms'),
    ('Perda de pacotes', '0%', '0%'),
], [3.1, 1.85, 1.85])
p('A diferença de ping é favorável à nova VPS, mas não comprova por si só fluidez de jogo. Cinco conexões HTTPS completas à API nova levaram de 0,32 a 0,41 segundo, incluindo a abertura de cada conexão.')

h('Pendências e próximos testes')
for item in [
    'Concluir o corte para a VPS nova: obter backup final da antiga, reconciliar alterações após o snapshot, validar saldos e progresso e então publicar um APK com o novo endereço e pin. Manter a antiga ativa até os clientes migrarem.',
    'Testar Windows e Android com duas contas reais na nova VPS: deslocamento, animação remota, chat, voz, rádio, reconexão e latência percebida.',
    'Revisar colisões, desempenho em Android de 4 GB, NPCs e rotas das três primeiras missões no mapa de Los Santos.',
    'Validar cada comando administrativo e policial e completar os que ainda são placeholders. Concluir as missões restantes conforme os recursos do mapa forem preparados.',
    'Conferir licenças e atribuições dos modelos, texturas, músicas e demais recursos de terceiros antes de distribuição mais ampla.',
]: bullet(item)

footer=s.footer.paragraphs[0]
footer.alignment=WD_ALIGN_PARAGRAPH.RIGHT
footer.add_run('Horizonte Brasil RP  •  Relatório interno  •  30/10/2026').font.size=Pt(8)
d.save(OUT)
print(OUT)
