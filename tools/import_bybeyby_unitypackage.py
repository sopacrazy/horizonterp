"""Import the user-provided ByBeyby Unity package without changing its GUID references.

The prefab is also copied to Resources so runtime code can select it by gender.
The original package layout stays intact for its material setup menu and credits.
"""
from pathlib import Path, PurePosixPath
import tarfile
import uuid

root = Path(__file__).resolve().parents[1]
source = root / "personagem/ByBeyby-Unity.unitypackage"
assets = root / "TestePersonagem/Assets"
resource_prefab = assets / "Resources/Characters/ByBeyby.prefab"
with tarfile.open(source, "r:gz") as archive:
    members = {entry.name: entry for entry in archive.getmembers()}
    imported = 0
    for entry in archive.getmembers():
        if not entry.name.endswith("/pathname"):
            continue
        key = entry.name.split("/", 1)[0]
        logical = archive.extractfile(entry).read().decode("utf-8").strip().replace("\\", "/")
        path = PurePosixPath(logical)
        if path.is_absolute() or ".." in path.parts or path.parts[:2] != ("Assets", "ByBeyby"):
            raise ValueError(f"Unsafe package path: {logical}")
        target = root / "TestePersonagem" / Path(*path.parts)
        for suffix, output in (("asset", target), ("asset.meta", Path(str(target) + ".meta"))):
            member = members.get(f"{key}/{suffix}")
            if member is None:
                continue
            if output.exists():
                raise FileExistsError(output)
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(archive.extractfile(member).read())
            imported += 1
    prefab = assets / "ByBeyby/Prefabs/ByBeyby.prefab"
    if resource_prefab.exists() or Path(str(resource_prefab) + ".meta").exists():
        raise FileExistsError(resource_prefab)
    resource_prefab.parent.mkdir(parents=True, exist_ok=True)
    resource_prefab.write_bytes(prefab.read_bytes())
    meta = (Path(str(prefab) + ".meta")).read_text(encoding="utf-8")
    old_guid = next(line.split(":", 1)[1].strip() for line in meta.splitlines() if line.startswith("guid:"))
    Path(str(resource_prefab) + ".meta").write_text(meta.replace(old_guid, uuid.uuid4().hex, 1), encoding="utf-8")
print(f"Imported {imported} package files and one runtime prefab: {resource_prefab}")
