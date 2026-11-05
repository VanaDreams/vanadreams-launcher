"""Builds vanagear's data files from the Vanadreams server source.

    python tools/gearstats/build.py <path to the vanadreams checkout>

Reads the server's own tables (sql/item_equipment.sql, item_weapon.sql, item_mods.sql,
item_mods_pet.sql, item_latents.sql, augments.sql, weapon_skills.sql), the mod enum
(scripts/enum/mod.codegen.lua) and the weaponskill scripts (scripts/actions/weaponskills),
and writes Lua tables into addons/vanagear/data/. The level 99 gear stats are already part of
the server's item_mods.sql, so nothing else needs applying.

Nothing here runs the server's code: the SQL is read as INSERT lines and the weaponskill
scripts as text.
"""
import ast
import operator
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / 'addons' / 'vanagear' / 'data'

ROW = re.compile(r"^\s*(?:INSERT|REPLACE) INTO `(\w+)` VALUES\s*\((.*)\);\s*(?:--.*)?$", re.I)


def values(text):
    """The values of one INSERT row, as Python values. Strings stay strings, 0x... stays bytes."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c in ' \t,':
            i += 1
            continue
        if c == "'":
            j, buf = i + 1, []
            while j < n:
                if text[j] == '\\' and j + 1 < n:
                    buf.append(text[j + 1]); j += 2; continue
                if text[j] == "'":
                    if j + 1 < n and text[j + 1] == "'":
                        buf.append("'"); j += 2; continue
                    break
                buf.append(text[j]); j += 1
            out.append(''.join(buf)); i = j + 1
            continue
        j = i
        while j < n and text[j] != ',':
            j += 1
        tok = text[i:j].strip()
        if tok.lower().startswith('0x'):
            out.append(bytes.fromhex(tok[2:]))
        elif tok.upper() == 'NULL':
            out.append(None)
        else:
            try:
                out.append(int(tok))
            except ValueError:
                out.append(float(tok))
        i = j
    return out


def rows(path, table):
    for line in path.read_text(encoding='utf-8', errors='replace').splitlines():
        m = ROW.match(line)
        if m and m.group(1) == table:
            yield values(m.group(2))


def mod_names(server):
    names = {}
    text = (server / 'scripts' / 'enum' / 'mod.codegen.lua').read_text(encoding='utf-8')
    for m in re.finditer(r'^\s*([A-Z0-9_]+)\s*=\s*(\d+),', text, re.M):
        names.setdefault(int(m.group(2)), m.group(1))
    return names


# ---- weaponskills ---------------------------------------------------------------------------

_OPS = {ast.Add: operator.add, ast.Sub: operator.sub, ast.Mult: operator.mul, ast.Div: operator.truediv,
        ast.USub: operator.neg}


def _arith(expr):
    """A number from a Lua expression after merit and job-point calls are read as 0."""
    expr = re.sub(r'player:get\w+\([^()]*(\([^()]*\))?[^()]*\)', '0', expr)
    expr = expr.split('--')[0].strip()
    try:
        tree = ast.parse(expr, mode='eval')
    except SyntaxError:
        return None

    def ev(node):
        if isinstance(node, ast.Expression):
            return ev(node.body)
        if isinstance(node, ast.Constant) and isinstance(node.value, (int, float)):
            return node.value
        if isinstance(node, ast.BinOp) and type(node.op) in _OPS:
            return _OPS[type(node.op)](ev(node.left), ev(node.right))
        if isinstance(node, ast.UnaryOp) and type(node.op) in _OPS:
            return _OPS[type(node.op)](ev(node.operand))
        raise ValueError
    try:
        return ev(tree)
    except (ValueError, ZeroDivisionError):
        return None


# several assignments can share a line: params.str_wsc = 0.3 params.mnd_wsc = 0.5
WSC = re.compile(r'params\.(str|dex|vit|agi|int|mnd|chr)_wsc\s*=\s*(.+?)(?=\s+params\.|$)')


def weaponskill(script, adoulin=True):
    """Stat modifiers, hit count and kind of one weaponskill, with the Adoulin block applied."""
    wsc, hits, kind, in_adoulin = {}, 1, 'physical', False
    for raw in script.splitlines():
        line = raw.strip()
        if 'USE_ADOULIN_WEAPON_SKILL_CHANGES' in line and line.startswith('if'):
            in_adoulin = True
            continue
        if in_adoulin and line == 'end':
            in_adoulin = False
            continue
        if in_adoulin and not adoulin:
            continue
        for m in WSC.finditer(line):
            v = _arith(m.group(2))
            if v is not None:
                wsc[m.group(1).upper()] = round(v, 3)
        m = re.search(r'params\.numHits\s*=\s*(\d+)', line)
        if m:
            hits = int(m.group(1))
        if 'doMagicWeaponskill' in line:
            kind = 'magical'
        elif 'doRangedWeaponskill' in line and kind != 'magical':
            kind = 'ranged'
    return {k: v for k, v in wsc.items() if v}, hits, kind


# ---- job abilities --------------------------------------------------------------------------

# Only the user's own mods count: target:getMod reads the monster or party member.
GETMOD = re.compile(r'(?:player|caster|actor|user|charmer|beastmaster|master|pc)\s*:\s*get(?:Mod|Stat|MaxGearMod)\(\s*xi\.mod\.([A-Z0-9_]+)')
UTIL_CALL = re.compile(r'xi\.job_utils\.(\w+)\.(\w+)\s*\(')
EFFECT = re.compile(r'addStatusEffect\(\s*xi\.effect\.([A-Z0-9_]+)')
DEF = re.compile(r'^(?:xi\.job_utils\.(\w+)\.(\w+)\s*=\s*function|local\s+function\s+(\w+)|local\s+(\w+)\s*=\s*function)', re.M)


def job_utils(server):
    """'job.fn' -> the text of that job_utils function, with the file's local helper functions
    it calls appended, so a mod read inside a helper (corsair's roll math) still counts."""
    out = {}
    for f in (server / 'scripts' / 'globals' / 'job_utils').glob('*.lua'):
        text = f.read_text(encoding='utf-8', errors='replace')
        defs = []
        for m in DEF.finditer(text):
            name = f'{m.group(1)}.{m.group(2)}' if m.group(1) else (m.group(3) or m.group(4))
            defs.append((m.start(), name, bool(m.group(1))))
        bodies, locals_ = {}, {}
        for i, (pos, name, public) in enumerate(defs):
            end = defs[i + 1][0] if i + 1 < len(defs) else len(text)
            (bodies if public else locals_)[name] = text[pos:end]
        for name, body in bodies.items():
            seen, todo, extra = set(), [body], []
            while todo:
                t = todo.pop()
                for loc, lbody in locals_.items():
                    if loc not in seen and re.search(rf'\b{re.escape(loc)}\s*\(', t):
                        seen.add(loc)
                        extra.append(lbody)
                        todo.append(lbody)
            out[name] = body + '\n'.join(extra)
    return out


def effect_gain(server, effect):
    """The onEffectGain part of an effect script: mods it reads when the effect lands."""
    f = server / 'scripts' / 'effects' / f'{effect.lower()}.lua'
    if not f.exists():
        return ''
    text = f.read_text(encoding='utf-8', errors='replace')
    m = re.search(r'effectObject\.onEffectGain.*?(?=effectObject\.on|\Z)', text, re.S)
    return m.group(0) if m else ''


def ability_mods(server, script, utils):
    """Every mod the server reads while the ability goes off: in its script, in the job_utils
    functions it calls (two levels), and in onEffectGain of the effects it gives."""
    seen, texts, todo = set(), [script], [script]
    for _ in range(2):
        nxt = []
        for t in todo:
            for job, fn in UTIL_CALL.findall(t):
                key = f'{job}.{fn}'
                if key in utils and key not in seen:
                    seen.add(key)
                    nxt.append(utils[key])
        texts += nxt
        todo = nxt
    for t in list(texts):
        for eff in EFFECT.findall(t):
            texts.append(effect_gain(server, eff))
    mods = []
    for t in texts:
        for mod in GETMOD.findall(t):
            if mod not in mods:
                mods.append(mod)
    return mods


# ---- writing Lua --------------------------------------------------------------------------

def lua_str(s):
    return "'" + s.replace('\\', '\\\\').replace("'", "\\'") + "'"


def lua(v):
    if isinstance(v, dict):
        parts = []
        for k, x in v.items():
            key = f'[{k}]' if isinstance(k, int) else (k if re.match(r'^[A-Za-z_]\w*$', k) else f'[{lua_str(k)}]')
            parts.append(f'{key}={lua(x)}')
        return '{' + ','.join(parts) + '}'
    if isinstance(v, (list, tuple)):
        return '{' + ','.join(lua(x) for x in v) + '}'
    if isinstance(v, str):
        return lua_str(v)
    if isinstance(v, bool):
        return 'true' if v else 'false'
    if isinstance(v, float):
        return repr(round(v, 4))
    return str(v)


def write_table(path, header, entries, chunk=400):
    """Writes `return t` where t[key] = value, split into functions so no one chunk holds too
    many constants for LuaJIT."""
    lines = [f'-- {h}' if h else '--' for h in header]
    lines.append('-- Generated by tools/gearstats/build.py. Do not edit; rebuild instead.')
    lines.append('local t = {};')
    items = list(entries.items())
    for i in range(0, len(items), chunk):
        lines.append('(function()')
        for k, v in items[i:i + chunk]:
            key = f'[{k}]' if isinstance(k, int) else f'[{lua_str(k)}]'
            lines.append(f't{key}={lua(v)};')
        lines.append('end)();')
    lines.append('return t;')
    path.write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\n')


def main(server):
    server = Path(server)
    sql = server / 'sql'
    names = mod_names(server)
    OUT.mkdir(parents=True, exist_ok=True)

    items = {}
    for r in rows(sql / 'item_equipment.sql', 'item_equipment'):
        item_id, name, level, ilevel, jobs, mid, shield, script, slot, rslot, rlook, su = r[:12]
        items[item_id] = {'name': name, 'level': level, 'jobs': jobs, 'slot': slot}
        if ilevel:
            items[item_id]['ilvl'] = ilevel
        if su:
            items[item_id]['su'] = su
        if shield:
            items[item_id]['shield'] = shield

    weapons = 0
    for r in rows(sql / 'item_weapon.sql', 'item_weapon'):
        item_id = r[0]
        if item_id in items:
            # itemId, name, skill, subskill, ilvl_skill, ilvl_parry, ilvl_macc, dmgType, hit, delay, dmg, unlock_points
            cols = r
            items[item_id]['skill'] = cols[2]
            items[item_id]['delay'] = cols[-3]
            items[item_id]['dmg'] = cols[-2]
            weapons += 1

    # gear that is used from the slot (Warp Ring, Empress Band): seconds worn before it can be
    # used (useDelay), cast time once used (activation), and reuse in seconds
    usable = 0
    for r in rows(sql / 'item_usable.sql', 'item_usable'):
        item_id, _name, _targets, activation, _anim, _animTime, charges, use_delay, reuse = r[:9]
        if item_id in items:
            items[item_id]['use'] = {'delay': use_delay, 'act': activation, 'reuse': reuse}
            usable += 1

    def add(item_id, key, mod, value):
        if item_id in items:
            items[item_id].setdefault(key, {})[mod] = value

    for item_id, mod, value in rows(sql / 'item_mods.sql', 'item_mods'):
        add(item_id, 'mods', mod, value)
    for item_id, mod, value, pet in rows(sql / 'item_mods_pet.sql', 'item_mods_pet'):
        if item_id in items:
            items[item_id].setdefault('pet', {}).setdefault(pet, {})[mod] = value
    for item_id, mod, value, latent, param in rows(sql / 'item_latents.sql', 'item_latents'):
        if latent == 40 and item_id in items:   # EQUIPPED_IN_SLOT: worth its mods only in that slot
            items[item_id].setdefault('inslot', {}).setdefault(param, {})[mod] = value

    augments = {}
    for aug_id, mult, mod, value, is_pet, pet_type in rows(sql / 'augments.sql', 'augments'):
        if mod == 0:
            continue
        augments.setdefault(aug_id, []).append({'mod': mod, 'v': value, 'm': mult, **({'pet': pet_type} if is_pet else {})})

    ws = {}
    ws_dir = server / 'scripts' / 'actions' / 'weaponskills'
    for r in rows(sql / 'weapon_skills.sql', 'weapon_skills'):
        ws_id, snake, jobs, skill_type, skill_level, element = r[:6]
        main_only = r[14] if len(r) > 14 else 0
        script = ws_dir / f'{snake}.lua'
        wsc, hits, kind = weaponskill(script.read_text(encoding='utf-8')) if script.exists() else ({}, 1, 'physical')
        ws[ws_id] = {'key': snake, 'skill': skill_type, 'level': skill_level, 'element': element,
                     'wsc': wsc, 'hits': hits, 'kind': kind}

    # job abilities: the mods the server reads when each one goes off, so gear that raises them
    # can be worn for it
    utils = job_utils(server)
    abilities = {}
    ab_dir = server / 'scripts' / 'actions' / 'abilities'
    for r in rows(sql / 'abilities.sql', 'abilities'):
        ab_id, snake, job, level = r[:4]
        script = ab_dir / f'{snake}.lua'
        if not script.exists() or job == 0:
            continue
        mods = ability_mods(server, script.read_text(encoding='utf-8', errors='replace'), utils)
        if mods:
            abilities[ab_id] = {'key': snake, 'job': job, 'level': level, 'mods': mods}

    # job traits and job point gifts: what a job already has at a level, so gear needs less to reach a
    # cap (a Red Mage's Fast Cast trait, a Corsair's Snapshot gift). A trait's ranks replace each other,
    # so each trait counts its highest rank at the player's level; traits opened by merits are left out.
    traits = {}
    for traitid, _name, job, level, rank, mod, value, _tag, meritid in rows(sql / 'traits.sql', 'traits'):
        if meritid == 0 and mod != 0:
            traits.setdefault(job, []).append({'trait': traitid, 'level': level, 'rank': rank, 'mod': mod, 'v': value})
    gifts = {}
    for job, jp, mod, value, _desc in rows(sql / 'job_point_gifts.sql', 'job_point_gifts'):
        if mod != 0:
            gifts.setdefault(job, []).append({'jp': jp, 'mod': mod, 'v': value})

    used = set()
    for rows_ in traits.values():
        used.update(t['mod'] for t in rows_)
    for rows_ in gifts.values():
        used.update(g['mod'] for g in rows_)
    for it in items.values():
        used.update(it.get('mods', {}))
        for p in it.get('pet', {}).values():
            used.update(p)
        for s in it.get('inslot', {}).values():
            used.update(s)
    for rows_ in augments.values():
        used.update(a['mod'] for a in rows_)

    src = f'from {server.name} at the commit checked out when this was run'
    write_table(OUT / 'items.lua', ['Equippable items: name, level, jobs mask, slot mask, weapon skill and stats.',
                                    'mods: the server mod id = value; pet: petType = { mod = value };',
                                    'inslot: slot id = { mod = value } for mods that only work in that slot.',
                                    'Source: the Vanadreams server tables, ' + src + '.'], items)
    write_table(OUT / 'augments.lua', ['Augment id = list of { mod, v (base value), m (multiplier), pet (pet type) }.',
                                       'Value of a worn augment, as the server works it out:',
                                       '(v > 0 and v + level or v - level) * max(m, 1).'], augments)
    write_table(OUT / 'weaponskills.lua', ['Weaponskill id = key, skill type, element, stat modifiers (wsc), hits, kind.',
                                           'Adoulin weaponskill values, the server default.'], ws)
    write_table(OUT / 'abilities.lua', ['Job ability id = key, job, level, and the mods the server reads when it goes off',
                                        '(the ability script, the job_utils functions it calls and the effects it gives).'], abilities)
    write_table(OUT / 'traits.lua', ['Job id = list of { trait, level, rank, mod, v } (sql/traits.sql, merit traits left out).',
                                     'Ranks of one trait replace each other: the highest rank at a level is the one held.'], traits)
    write_table(OUT / 'gifts.lua', ['Job id = list of { jp (job points spent), mod, v } (sql/job_point_gifts.sql).',
                                    'Every gift at or under the job points spent counts.'], gifts)
    write_table(OUT / 'mods.lua', ['Server mod id = name (scripts/enum/mod.codegen.lua), for the mods any item, trait or gift uses.'],
                {m: names.get(m, f'MOD_{m}') for m in sorted(used)})
    print(f'items {len(items)} (weapons {weapons}), augments {len(augments)}, weaponskills {len(ws)}, '
          f'abilities with gear {len(abilities)}, mods {len(used)}')
    for f in sorted(OUT.glob('*.lua')):
        print(f'  {f.name} {f.stat().st_size:,} bytes')


LATENTS_KEPT = {
    8: 'SUBJOB', 62: 'MAINJOB', 50: 'JOB_LEVEL_BELOW', 51: 'JOB_LEVEL_ABOVE', 23: 'ZONE', 48: 'IN_DYNAMIS',
    10: 'WEAPON_DRAWN', 11: 'WEAPON_SHEATHED',
}


def extras(server):
    """Set bonuses (scripts/globals/gear_sets.lua) and the conditional stats a set can be built for
    (sql/item_latents.sql, only the conditions known when a set is built: job, level, zone, Dynamis,
    weapon drawn or sheathed). Writes gearsets.lua and latents.lua, adds any mod names they use to
    mods.lua, and leaves the other data files as they are."""
    server = Path(server)
    names = mod_names(server)
    ids_by_name = {v: k for k, v in names.items()}
    item_ids = {}
    for m in re.finditer(r'^\s*([A-Z0-9_]+)\s*=\s*(\d+)', (server / 'scripts' / 'enum' / 'item.lua').read_text(encoding='utf-8', errors='replace'), re.M):
        item_ids[m.group(1)] = int(m.group(2))

    text = (server / 'scripts' / 'globals' / 'gear_sets.lua').read_text(encoding='utf-8', errors='replace')
    body = text[text.index('local gearSets'):text.index('xi.gear_sets.createItemToSetId')]
    sets = {}
    for m in re.finditer(r'\[(\d+)\]\s*=\s*(?:--[^\n]*\n\s*)*\{', body):
        set_id = int(m.group(1))
        i, depth = m.end(), 1
        while depth and i < len(body):
            if body[i] == '{': depth += 1
            elif body[i] == '}': depth -= 1
            elif body.startswith('--', i):
                i = body.index('\n', i)
            i += 1
        block = body[m.end():i - 1]
        items_m = re.search(r'items\s*=\s*\{(.*?)\}', block, re.S)
        if not items_m:
            continue
        items = []
        for line in items_m.group(1).splitlines():
            line = line.split('--')[0]
            for tok in re.findall(r'xi\.item\.([A-Z0-9_]+)|(\d+)', line):
                if tok[0]:
                    if tok[0] in item_ids: items.append(item_ids[tok[0]])
                elif tok[1]:
                    items.append(int(tok[1]))
        mods = []
        mods_m = re.search(r'mods\s*=\s*\{(.*)\}', block, re.S)
        for mm in re.finditer(r'\{\s*xi\.mod\.([A-Z0-9_]+)\s*,([^}]*)\}', mods_m.group(1) if mods_m else ''):
            mod_id = ids_by_name.get(mm.group(1))
            vals = [int(v) for v in re.findall(r'-?\d+', mm.group(2).split('--')[0])]
            if mod_id is not None and vals:
                mods.append({'mod': mod_id, 'v': vals})
        if not items or not mods:
            continue
        entry = {'items': items, 'min': 2, 'mods': mods}
        for key, field in (('minEquipped', 'min'), ('maxEquipped', 'max'), ('requiredItem', 'req')):
            km = re.search(key + r'\s*=\s*(\d+)', block)
            if km:
                entry[field] = int(km.group(1))
        sets[set_id] = entry

    latents = {}
    for item_id, mod, value, latent, param in rows(server / 'sql' / 'item_latents.sql', 'item_latents'):
        if latent in LATENTS_KEPT and latent != 40:
            latents.setdefault(item_id, []).append({'latent': latent, 'param': param, 'mod': mod, 'v': value})

    write_table(OUT / 'gearsets.lua', ['Set id = { items, min (pieces needed), max (pieces counted, if set), req (an item that must be',
                                       'one of them), mods = list of { mod, v = value by tier } } (scripts/globals/gear_sets.lua).',
                                       'Tier = min(pieces worn, max) - min; the value is v[tier + 1].'], sets)
    write_table(OUT / 'latents.lua', ['Item id = list of { latent, param, mod, v } (sql/item_latents.sql), only the conditions a set can',
                                      'be built for: ' + ', '.join(f'{k} {v}' for k, v in sorted(LATENTS_KEPT.items())) + '.'], latents)

    # the element a magical weaponskill's damage takes (params.ele in its script), for the obi; the
    # element column in weapon_skills.sql is the skillchain/gorget one, not this
    ws_elements = {}
    for path in sorted((server / 'scripts' / 'actions' / 'weaponskills').glob('*.lua')):
        m = re.search(r'params\.ele\s*=\s*xi\.element\.([A-Z]+)', path.read_text(encoding='utf-8', errors='replace'))
        if m and m.group(1) != 'NONE':
            ws_elements[re.sub(r'[^a-z0-9]', '', path.stem.lower())] = m.group(1).capitalize()
    write_table(OUT / 'wselements.lua', ['Weaponskill (its name in lower case, letters and digits only) = the element its damage takes',
                                         '(params.ele in scripts/actions/weaponskills), for the obi.'], ws_elements)

    # the stat each enfeebling spell reads against the target's, and whether Saboteur strengthens it
    # (scripts/globals/spells/enfeebling_spell.lua, STAT_USED and SABOTEUR)
    enfeebles = {}
    text = (server / 'scripts' / 'globals' / 'spells' / 'enfeebling_spell.lua').read_text(encoding='utf-8', errors='replace')
    for m in re.finditer(r'\[xi\.magic\.spell\.([A-Z0-9_]+)\s*\]\s*=\s*\{\s*xi\.effect\.\w+\s*,\s*-?\d+\s*,\s*xi\.mod\.([A-Z]+)\s*,'
                         r'\s*-?\d+\s*,\s*-?\d+\s*,\s*-?\d+\s*,\s*-?\d+\s*,\s*(true|false)', text):
        enfeebles[re.sub(r'[^a-z0-9]', '', m.group(1).lower())] = {'stat': m.group(2), 'saboteur': m.group(3) == 'true'}
    write_table(OUT / 'enfeebles.lua', ['Enfeebling spell (its name in lower case, letters and digits only) = { stat it reads against the',
                                        "target's, saboteur: Saboteur strengthens it } (scripts/globals/spells/enfeebling_spell.lua)."], enfeebles)

    # the mod names these use, added to mods.lua without touching the rest of it
    used = {m['mod'] for s in sets.values() for m in s['mods']} | {l['mod'] for ls in latents.values() for l in ls}
    mods_path = OUT / 'mods.lua'
    mods_text = mods_path.read_text(encoding='utf-8')
    have = set(int(x) for x in re.findall(r'^t\[(\d+)\]=', mods_text, re.M))
    add = [f"t[{m}]={lua_str(names.get(m, f'MOD_{m}'))};" for m in sorted(used - have)]
    if add:
        at = mods_text.rindex('end)()')
        mods_path.write_text(mods_text[:at] + '\n'.join(add) + '\n' + mods_text[at:], encoding='utf-8', newline='\n')
    print(f'gear sets {len(sets)}, items with kept latents {len(latents)}, mod names added {len(add)}')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--extras':
        extras(sys.argv[2])
    elif len(sys.argv) == 2:
        main(sys.argv[1])
    else:
        sys.exit(__doc__)
