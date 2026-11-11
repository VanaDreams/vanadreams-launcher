"""Checks that every stat named in vg/roles.lua and jobs/*.lua is a server mod name.

    python addons/vanagear/tests/stat_names.py
"""
import os
import re
from pathlib import Path

ADDON = Path(__file__).resolve().parent.parent
SERVER_ROOT = Path(os.environ.get('VANAGEAR_SERVER_ROOT', ADDON.parent.parent.parent / 'vanadreams'))
SERVER_ENUM = SERVER_ROOT / 'data' / 'enums' / 'mod.yaml'

names = {n.upper() for n in re.findall(r'^\s+([a-z0-9_]+):\s*\d+', SERVER_ENUM.read_text(encoding='utf-8'), re.M)}
names |= {'PDT', 'MDT'}
# roles.lua keys its job lists by job, which are not stats
names |= {'WAR', 'MNK', 'WHM', 'BLM', 'RDM', 'THF', 'PLD', 'DRK', 'BST', 'BRD', 'RNG', 'SAM', 'NIN', 'DRG', 'SMN',
          'BLU', 'COR', 'PUP', 'DNC', 'SCH', 'GEO', 'RUN'}
bad = 0
for f in [ADDON / 'vg' / 'roles.lua', ADDON / 'vg' / 'wsguide.lua', *sorted((ADDON / 'jobs').glob('*.lua'))]:
    text = re.sub(r'--\[\[.*?\]\]--', '', f.read_text(encoding='utf-8'), flags=re.S)
    for word in sorted(set(re.findall(r'\b([A-Z][A-Z0-9_]{2,})\s*=', text))):
        if word not in names:
            bad += 1
            print(f'{f.name}: {word}')
print('all stat names known' if bad == 0 else f'{bad} unknown')
