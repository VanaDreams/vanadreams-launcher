"""Build and verify a deterministic VanaGear candidate from repository files.

Does not install, upload, tag, publish, or invoke any release workflow.
The file set matches the launcher's repo-folder source: every committed
file under addons/vanagear, including its documentation, tests, font and license.
Ignored caches and local runtime files are excluded by Git's file listing.
"""
import argparse
import hashlib
import io
import json
import re
import subprocess
import tarfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args])


def digest(data):
    return hashlib.sha256(data).hexdigest()


def package(out):
    relevant = ('addons/vanagear', 'catalog.json', 'launcher/VanadreamsLauncher/assets/catalog.json',
                'tools/package_vanagear.py')
    assert not git('status', '--porcelain', '--', *relevant).strip(), 'Commit the candidate before packaging'
    commit = git('rev-parse', 'HEAD').decode().strip()
    # Package Git's canonical bytes, as GitHub serves them, not autocrlf's
    # Windows working-copy bytes. No cache or uncommitted file can slip in.
    tree = git('-c', 'core.autocrlf=false', '-c', 'core.eol=lf',
               'archive', '--format=tar', commit, 'addons/vanagear')
    payload = {}
    with tarfile.open(fileobj=io.BytesIO(tree), mode='r:') as tar:
        for member in tar.getmembers():
            if member.isdir():
                continue
            assert member.isfile(), f'Not a regular source file: {member.name}'
            relative = Path(member.name).relative_to('addons/vanagear')
            assert not any(part in ('__pycache__', '.pytest_cache', '.git') for part in relative.parts), member.name
            assert relative.parts[0] != 'config', f'Player configuration must not be packaged: {member.name}'
            archive_name = 'vanagear/' + relative.as_posix()
            assert archive_name.casefold() not in {n.casefold() for n in payload}, 'Case-colliding archive entries'
            payload[archive_name] = tar.extractfile(member).read()
    payload = dict(sorted(payload.items()))
    # Archive conversion can be affected by local Git configuration/attributes.
    # Prove every member equals its committed blob instead of assuming it does.
    blobs = {}
    for record in git('ls-tree', '-r', '-z', commit, '--', 'addons/vanagear').split(b'\0'):
        if not record:
            continue
        metadata, name = record.decode('utf-8').split('\t', 1)
        mode, kind, oid = metadata.split()
        assert kind == 'blob' and mode in ('100644', '100755'), f'Unexpected Git entry: {name}'
        blobs[name.removeprefix('addons/')] = oid
    assert set(blobs) == set(payload), 'Archive differs from committed file inventory'
    for name, data in payload.items():
        oid = hashlib.sha1(f'blob {len(data)}\0'.encode('ascii') + data).hexdigest()
        assert oid == blobs[name], f'Archive bytes differ from committed blob: {name}'
    version = re.search(r"addon\.version\s*=\s*'([^']+)'", payload['vanagear/vanagear.lua'].decode('utf-8'))[1]
    entries = []
    for name in ('catalog.json', 'launcher/VanadreamsLauncher/assets/catalog.json'):
        catalog = json.loads(git('show', f'{commit}:{name}').decode('utf-8-sig'))
        entry = next(item for item in catalog['items'] if item['id'] == 'vanagear')
        assert entry['version'] == version, f'Version mismatch in {name}'
        assert entry['source'] == {'type': 'repo-folder', 'repo': 'VanaDreams/vanadreams-launcher',
                                   'path': 'addons/vanagear', 'branch': 'main'}, 'Unexpected publication route'
        entries.append(entry)
    assert entries[0] == entries[1], 'VanaGear catalog entries differ'

    assert payload, 'Empty addon'
    required = ('vanagear.lua', 'vg/i18n.lua', 'vg/locale_font.lua', 'vg/locales/en.lua',
                'vg/locales/ja.lua', 'vg/locales/ja_ui.lua', 'vg/locales/ja_help.lua',
                'vg/locales/ja_messages.lua', 'fonts/MPLUS1p-Regular.ttf', 'fonts/OFL.txt',
                'LICENSE-LuAshitacast', 'LOCALIZATION.md', 'README.md')
    for name in required:
        assert 'vanagear/' + name in payload, f'Missing required file: {name}'

    out.mkdir(parents=True, exist_ok=True)
    archive = out / f'vanagear-{version}-candidate.zip'
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in payload.items():
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            z.writestr(info, data)

    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None, 'ZIP integrity check failed'
        assert set(z.namelist()) == set(payload), 'Archive file set mismatch'
        for name, original in payload.items():
            assert z.read(name) == original, f'Archive content mismatch: {name}'
    assert git('rev-parse', 'HEAD').decode().strip() == commit, 'Commit changed while packaging'
    assert not git('status', '--porcelain', '--', *relevant).strip(), 'Source changed while packaging'

    manifest = {
        'addon': 'vanagear', 'version': version, 'status': 'local release candidate; not published',
        'branch': git('branch', '--show-current').decode().strip(),
        'source_commit': commit, 'source_dirty': False,
        'archive': archive.name, 'sha256': digest(archive.read_bytes()),
        'files': len(payload), 'unpacked_bytes': sum(map(len, payload.values())),
        'file_sha256': {name: digest(data) for name, data in payload.items()},
    }
    manifest_path = out / f'vanagear-{version}-candidate.manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: manifest[key] for key in ('version', 'archive', 'sha256', 'files', 'unpacked_bytes')}, indent=2))
    return archive


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    package(parser.parse_args().out.resolve())
