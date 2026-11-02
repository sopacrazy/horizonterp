"""Copy current Los Santos game changes into the warmed Android build project."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess
import time

root = Path(__file__).resolve().parents[1]
source = root / "TestePersonagem"
target = root / "TestePersonagemAndroidLS"
assert target.is_dir() and (target / "ProjectSettings").is_dir()


def git_paths(*arguments):
    result = subprocess.run(["git", *arguments], cwd=root, check=True,
                            capture_output=True).stdout
    return [Path(value.decode("utf-8")) for value in result.split(b"\0") if value]


paths = set(git_paths("diff", "--name-only", "-z", "--", "TestePersonagem"))
paths.update(git_paths("diff", "--cached", "--name-only", "-z", "--", "TestePersonagem"))
for folder in ("Scripts", "Resources", "Editor", "Startup", "Branding",
               "Prefabs", "Animations", "Plugins"):
    # Runtime sources must match even after a commit or staging operation.
    # Git's dirty-file lists alone can silently leave an old Android copy.
    current = source / "Assets" / folder
    if current.is_dir():
        paths.update(file.relative_to(root) for file in current.rglob("*") if file.is_file()
                     and not any(part in {"credentials", "backups", "node_modules", ".git"} for part in file.parts))
        folder_meta = Path(str(current) + ".meta")
        if folder_meta.is_file():
            paths.add(folder_meta.relative_to(root))

# The Resources prefab references its avatar, mesh, materials and textures here.
# Include the entire source package even when it is untracked or unchanged in Git.
character_package = source / "Assets/ByBeyby"
assert character_package.is_dir(), "Female character source package is missing"
paths.update(file.relative_to(root) for file in character_package.rglob("*") if file.is_file())
character_meta = Path(str(character_package) + ".meta")
assert character_meta.is_file(), "Female character package folder meta is missing"
paths.add(character_meta.relative_to(root))

for folder in ("GroundRepairs", "CollisionRepairs"):
    base = source / "Assets/GTASanAndreasModFull" / folder
    if base.is_dir():
        paths.update(file.relative_to(root) for file in base.rglob("*") if file.is_file())

for name in ("Entrada.unity", "TestePersonagem.unity", "SanAndreasIntegration.enabled"):
    file = source / "Assets/Scenes" / name
    paths.add(file.relative_to(root))
    if Path(str(file) + ".meta").exists():
        paths.add(Path(str(file) + ".meta").relative_to(root))

sectors = source / "Assets/Scenes/GTASanAndreasModFullSectors"
for file in sectors.glob("*.unity"):
    match = re.fullmatch(r"GTASASector_([mp])(\d+)_([mp])(\d+)", file.stem)
    if not match:
        continue
    x = int(match[2]) * (-1 if match[1] == "m" else 1)
    y = int(match[4]) * (-1 if match[3] == "m" else 1)
    if -2 <= x <= 12 and -13 <= y <= -2:
        paths.add(file.relative_to(root))
        meta = Path(str(file) + ".meta")
        if meta.exists():
            paths.add(meta.relative_to(root))

for folder in ("Packages", "ProjectSettings"):
    base = source / folder
    paths.update(file.relative_to(root) for file in base.rglob("*") if file.is_file())

backup = root / "tools" / "android-073-sync-backup" / time.strftime("%Y%m%d-%H%M%S")
changed = []
for relative in sorted(paths):
    file = root / relative
    if not file.is_file():
        continue
    subpath = relative.relative_to("TestePersonagem")
    destination = target / subpath
    if destination.is_file() and file.stat().st_size == destination.stat().st_size:
        if hashlib.sha256(file.read_bytes()).digest() == hashlib.sha256(destination.read_bytes()).digest():
            continue
    if destination.exists():
        saved = backup / subpath
        saved.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(destination, saved)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(file, destination)
    changed.append(subpath.as_posix())

report = {"changed": len(changed), "files": changed, "backup": str(backup),
          "source": str(source), "target": str(target)}
output = root / "tools/android-073-sync.json"
output.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(f"Synchronized {len(changed)} files. Report: {output}")
