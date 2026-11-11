"""Repair the exact legacy normal expression in an existing native export.

Backups are kept outside Content. Equal-length replacement preserves package offsets.
"""
import json
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
old = b'normal=normalize((1-abs(b))*(detail+sign(b))-sign(b))'
new = b'normal=normalize((1-abs(b))*(detail-sign(b))+sign(b))'
assert len(old) == len(new)
count = 0
for report in (root / 'Content').rglob('*.formula.json'):
    metadata = json.loads(report.read_text(encoding='utf-8-sig'))
    if metadata.get('Shader') != 'characterlegacy.shpk':
        continue
    asset = report.with_name(report.name.removesuffix('.formula.json') + '.uasset')
    data = asset.read_bytes()
    hits = data.count(old)
    if not hits:
        continue
    for path in [asset, report]:
        backup = root / 'normal-swapc-v1-backup' / path.relative_to(root)
        backup.parent.mkdir(parents=True, exist_ok=True)
        if backup.exists():
            raise FileExistsError(backup)
        shutil.copy2(path, backup)
    asset.write_bytes(data.replace(old, new))
    metadata['Reference'] = 'FFXIV SM5 SHPK 2026-11-12; normal-swapc-v2; tools/ShpkProbe'
    report.write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding='utf-8')
    assert len(asset.read_bytes()) == len(data) and old not in asset.read_bytes()
    print(f'{asset.name}: repaired {hits} expressions')
    count += 1
print(f'Repaired {count} materials; backups: {root / "normal-swapc-v1-backup"}')
