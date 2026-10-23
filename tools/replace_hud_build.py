from pathlib import Path
p=Path('TestePersonagem/Assets/Scripts/GameHud.cs');s=p.read_text(encoding='utf-8-sig');start=s.index('    void Build()');end=s.index('    public void OpenPanel',start)
s=s[:start]+Path('tools/new_hud_build_body.txt').read_text(encoding='utf-8-sig')+s[end:]
p.write_text(s,encoding='utf-8')
