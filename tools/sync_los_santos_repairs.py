"""Copy verified Los Santos repairs to the existing isolated Android project."""
from __future__ import annotations

import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'TestePersonagem'
TARGET = ROOT / 'TestePersonagemAndroidLS'
OUTPUT = ROOT / 'tools/los-santos-repair'


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    if not (TARGET / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise SystemExit('Existing isolated Android project was not found.')
    paths: set[str] = set()
    for line in (OUTPUT / 'android-sync-paths.txt').read_text(encoding='utf-8-sig').splitlines():
        relative = PurePosixPath(line.strip())
        if not relative.parts or relative.parts[0] != 'Assets' or '..' in relative.parts:
            raise ValueError(f'Invalid asset path: {line!r}')
        if not (SOURCE / str(relative)).is_file():
            raise FileNotFoundError(SOURCE / str(relative))
        paths.add(relative.as_posix())
        if (SOURCE / (relative.as_posix() + '.meta')).is_file():
            paths.add(relative.as_posix() + '.meta')
        for parent in relative.parents:
            if parent.as_posix() in {'.', 'Assets'}:
                break
            meta = parent.as_posix() + '.meta'
            if (SOURCE / meta).is_file():
                paths.add(meta)

    stamp = datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
    backup = OUTPUT / f'android-sync-backup-{stamp}'
    changed = []
    for relative in sorted(paths):
        source, destination = SOURCE / relative, TARGET / relative
        source_hash = digest(source)
        if destination.is_file() and digest(destination) == source_hash:
            continue
        if destination.is_file():
            saved = backup / relative
            saved.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(destination, saved)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        if digest(destination) != source_hash:
            raise OSError(f'Copied asset hash mismatch: {relative}')
        changed.append(relative)
    report = {'source': str(SOURCE), 'target': str(TARGET), 'backup': str(backup),
              'verifiedFiles': len(paths), 'changedFiles': len(changed),
              'files': changed, 'sourceMapPreserved': True}
    (OUTPUT / 'android-repair-sync.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'files'}))


if __name__ == '__main__':
    main()
