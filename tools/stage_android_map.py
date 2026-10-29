"""Stage only dependencies of the reviewed map, preserving unrelated local previews."""
from pathlib import Path
import argparse,subprocess

root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--stage',action='store_true');args=p.parse_args()
dependency_file=root/'tools/android-map-optimization/dependencies.txt'
selected=set()
def include(path):
    path=Path(path)
    if path.is_file():
        selected.add(path.relative_to(root).as_posix())
        meta=Path(str(path)+'.meta')
        if meta.is_file():selected.add(meta.relative_to(root).as_posix())
        for parent in path.parents:
            if parent==root/'TestePersonagem' or parent==root:break
            folder_meta=Path(str(parent)+'.meta')
            if folder_meta.is_file():selected.add(folder_meta.relative_to(root).as_posix())
for line in dependency_file.read_text(encoding='utf-8-sig').splitlines():
    include(root/'TestePersonagem'/line)
    if line.endswith('.obj'):include((root/'TestePersonagem'/line).with_suffix('.mtl'))
for path in (root/'TestePersonagem/Assets/Scenes/GTASanAndreasModFullSectors').glob('*.unity'):include(path)
include(root/'TestePersonagem/Assets/Scenes/TestePersonagem.unity')
for folder in ['Editor','Scripts']:
    for path in (root/f'TestePersonagem/Assets/{folder}').glob('GtaSa*.cs'):include(path)
for path in ['TestePersonagem/Assets/Editor/AndroidMapOptimization.cs',
             'TestePersonagem/Assets/Editor/SanAndreasIntegration.cs',
             'TestePersonagem/Assets/Editor/LosSantosAndroidSetup.cs',
             'TestePersonagem/Assets/Scripts/MapRegionBoundary.cs',
             'TestePersonagem/Assets/Scenes/AndroidLosSantos/TestePersonagem.unity',
             'TestePersonagem/Assets/Scenes/AndroidLosSantos/Entrada.unity',
             'TestePersonagem/Assets/Scenes/LosSantosAndroid.enabled',
             'TestePersonagem/Assets/Scripts/IntegratedMapEntry.cs',
             'TestePersonagem/Assets/Scripts/RememberedCityEntryVerification.cs',
             'TestePersonagem/Assets/Scenes/SanAndreasIntegration.enabled']:
    include(root/path)
manifest=root/'tools/android-map-optimization/git-paths.txt'
manifest.write_text('\n'.join(sorted(selected))+'\n',encoding='utf-8')
total=sum((root/path).stat().st_size for path in selected)
print(f'Map dependency files: {len(selected)}, bytes: {total}')
if args.stage:
    # Tens of thousands of pathspecs in one add cause quadratic matching work.
    # Each reviewed path is an existing source file; update-index consumes exact
    # filenames directly without expanding directory patterns or adding extras.
    subprocess.run(['git','update-index','--add','-z','--stdin'],
                   input=('\0'.join(sorted(selected))+'\0').encode('utf-8'),cwd=root,check=True)
