"""Build VanaDunna with the installed XI Tools database action into staging only.
Input must be the effective current client weapon DATs, including existing patches.
Never writes to the source install or publishes. Output ZIP uses existing text overlay.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import zipfile

ITEM = 22999
DONOR = 21460
FILES = ('ROM/118/108.DAT', 'ROM/0/6.DAT')
DEST = '{ashita}/polplugins/DATs/vanadreams-text'
PACK = 'vanadreams-vanadunna-01.zip'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def build(source, output, xi_tools):
    source, output = Path(source).resolve(), Path(output).resolve()
    if output == source or source in output.parents or output in source.parents:
        raise ValueError('Use a separate staging output, not the input install or its parent.')
    os.environ['FFXI_DIR'] = str(source)
    sys.path.insert(0, str(Path(xi_tools).resolve()))
    from xi.database import xi_build as db
    from xi.ui.items.xi_parser import _decrypt, detect_stride
    from xi.ui.items.xi_layout import read_field
    action = json.loads(Path(__file__).with_name('vanadunna.json').read_text(encoding='utf-8-sig'))
    stage = output / 'overlay'
    if stage.exists():
        raise ValueError('Output overlay already exists; use a new output directory.')
    originals = {}
    report = {'item': ITEM, 'donor': DONOR, 'login_points': 500, 'files': {}, 'checks': []}
    for rel in FILES:
        raw = (source / rel).read_bytes()
        stride = detect_stride(raw)
        dec = _decrypt(raw)
        index = ITEM - 16384
        record = dec[index * stride:(index + 1) * stride]
        fmt = 'retail' if stride == 0x1400 else 'legacy'
        if not db.is_empty_item(record, 'weapon', fmt):
            raise ValueError(f'{rel}: item {ITEM} is already occupied; refusing overwrite')
        donor = dec[(DONOR - 16384) * stride:(DONOR - 16383) * stride]
        if read_field(donor, 'weapon', fmt, 'skill') != 45:
            raise ValueError(f'{rel}: donor is not a handbell')
        originals[rel] = raw
        target = stage / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
        report['files'][rel] = {'input_sha256': digest(raw), 'stride': stride, 'blank_target': True}
    result = db.build(action, root=stage, target='vanadunna-staging', manifest={}, sql_path=None, project='vanadunna')
    if result['warnings']:
        raise ValueError(result['warnings'])
    assert set(result['files']) == set(FILES)
    for rel, raw in originals.items():
        changed = (stage / rel).read_bytes()
        stride = detect_stride(raw)
        old, new = _decrypt(raw), _decrypt(changed)
        a, b = (ITEM - 16384) * stride, (ITEM - 16383) * stride
        assert len(old) == len(new)
        assert old[:a] == new[:a] and old[b:] == new[b:], 'Unrelated records changed'
        fmt = 'retail' if stride == 0x1400 else 'legacy'
        rec = new[a:b]
        assert db.item_name(rec, 'weapon', fmt) == 'VanaDunna'
        for field, expected in [('jobs', 0x7ffffe), ('level', 1), ('slots', 4), ('skill', 45)]:
            assert read_field(rec, 'weapon', fmt, field) == expected, field
        donor = old[(DONOR - 16384) * stride:(DONOR - 16383) * stride]
        assert db._icon_region(rec) == db._icon_region(donor), 'Donor icon changed'
        assert (source / rel).read_bytes() == raw, 'Source changed'
        report['files'][rel]['output_sha256'] = digest(changed)
    report['checks'] = ['EN and JP target blank before creation', 'all 22 jobs level 1', 'ranged slot handbell skill 45', 'Matre Bell icon preserved', 'every other record byte-identical including Dunna', 'input files unchanged']
    with zipfile.ZipFile(output / PACK, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel in FILES:
            info = zipfile.ZipInfo(rel, date_time=(2026, 11, 16, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, (stage / rel).read_bytes())
    blob = (output / PACK).read_bytes()
    chunk = {'name': PACK, 'size': len(blob), 'sha256': digest(blob), 'files': 2, 'unpacked': sum(len(v) for v in originals.values()), 'dest': DEST}
    (output / (PACK + '.json')).write_text(json.dumps(chunk, indent=2) + '\n')
    (output / 'verification.json').write_text(json.dumps(report, indent=2) + '\n')
    return report, chunk

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('source')
    p.add_argument('output')
    p.add_argument('--xi-tools', default=os.environ.get('XI_TOOLS_SRC', r'D:\Workshop\Claude\Output\xi-vision\xi-tools-src\src'))
    a = p.parse_args()
    report, chunk = build(a.source, a.output, a.xi_tools)
    print(json.dumps({'verification': report, 'chunk': chunk}, indent=2))
