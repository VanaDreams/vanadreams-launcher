"""Build the Chapter I client updater artifact from a PRIVATE donor ZIP and exact retail baselines.

Never publish the donor ZIP or the originals. Review distribution rights for locally new DATs.
"""
import argparse
import hashlib
import json
import re
import struct
import zipfile
from collections import defaultdict
from pathlib import Path

MAGIC = b"VDP1"
FILENAME = "vanadreams-chapter-i-01.zip"
DEST = "{ashita}/polplugins/DATs/vanadreams-chapter-i"
DAT = re.compile(r"(?:[FV]TABLE\.DAT|ROM(?:2|4)?/\d+/\d+\.DAT)")
DONOR_ROOT = "FINAL FANTASY XI/"
MIRROR_ROOT = "XIPivot-overlay-VanaDreams/VanaDreams/"
HASH = re.compile(r"[a-f0-9]{64}")
HAIR = {
    "FTABLE.DAT": (2, "e45c2ca79ea63de2cbfd3cb90474442e015159ce68a4e52dcb7e830079c623ee", {5192, 5193, *range(57235, 57241)}),
    "VTABLE.DAT": (1, "cb5a6cc150420ec70a098e40d8d0d1f9c6f3ae2cbd7b484d16f130b04f36efe7", {5192, 5193, 57240}),
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encode(base, target):
    """Copy matching ranges from the verified base; literals hold only changed bytes."""
    index = defaultdict(list)
    n = 24
    for p in range(0, len(base) - n + 1, 8):
        key = base[p:p+n]
        if len(index[key]) < 8:
            index[key].append(p)
    result = bytearray(MAGIC)
    pos, pending = 0, 0
    while pos + n <= len(target):
        key = target[pos:pos+n]
        options = index.get(key, ())
        if base[pos:pos+n] == key:
            options = (pos, *options)
        if not options:
            pos += 1
            continue
        best, length = 0, 0
        for p in options:
            match = n
            while (pos + match + 64 <= len(target) and p + match + 64 <= len(base)
                   and target[pos+match:pos+match+64] == base[p+match:p+match+64]):
                match += 64
            while pos + match < len(target) and p + match < len(base) and target[pos+match] == base[p+match]:
                match += 1
            if match > length:
                best, length = p, match
            if pos + length == len(target):
                break
        if pending < pos:
            result += b"\x01" + struct.pack("<I", pos - pending) + target[pending:pos]
        result += b"\x00" + struct.pack("<II", best, length)
        pos += length
        pending = pos
    if pending < len(target):
        result += b"\x01" + struct.pack("<I", len(target) - pending) + target[pending:]
    return result + b"\xff"


def compose_tables(original, hair, chapter, width, expected):
    if len(original) != len(hair) or len(original) != len(chapter) or len(original) % width:
        raise ValueError("Table lengths differ")
    touched = set()
    composed = bytearray(hair)
    for off in range(0, len(original), width):
        end = off + width
        base, other, new = original[off:end], hair[off:end], chapter[off:end]
        if new == base:
            continue
        touched.add(off // width)
        if other != base and other != new:
            raise ValueError(f"Hair and Chapter I both edit table record {off // width}")
        composed[off:end] = new
    if touched != expected:
        raise ValueError(f"Unexpected Chapter I table changes {sorted(touched)}")
    return bytes(composed)


PUBLIC = {
    "FTABLE.DAT": ("vanadreams-client-patch-01.zip", "cccb9cbf6aecb456f447ea74634b41fbfc9eed3a7cdab9e5da89b9824449bc53"),
    "ROM2/13/13.DAT": ("vanadreams-client-patch-01.zip", "4b7f339d6adf4c8cea9a7c8222af771368d624341df8ee98c20b526850be1c3c"),
    "ROM2/13/58.DAT": ("ROM2-02.zip", "6a267f87a59bfce5afda3c8caa505eb00388618fb198dde4264b0c83919f1e0e"),
    "ROM2/13/103.DAT": ("vanadreams-client-patch-02.zip", "b85ad1c70c61c008325f336584bcf7b41f21d3ef4f71d063ff774ee73e4ae9d3"),
    "ROM2/17/63.DAT": ("ROM2-02.zip", "11f61ae7d45ede6f7fd011611a8e76dec58ebb4ee2a4a4c46db4e3bb81c21db1"),
}
EVENT_HPS = {(0x010821A9 + i, 0) for i in range(5)}
DIALOG_PRIOR = {
    "ROM2/13/58.DAT": {7002, 11719},
    "ROM2/17/63.DAT": {6520, 7351, 7352, 7353, 7377, 7378, 7379, 7380, 7381, 7382, 7384, 11719},
}


def by_entity(rows, offset):
    counts, result = defaultdict(int), {}
    for row in rows:
        key = struct.unpack_from("<I", row, offset)[0]
        result[key, counts[key]] = row
        counts[key] += 1
    return result


def by_events(data):
    count, = struct.unpack_from("<I", data)
    sizes = struct.unpack_from(f"<{count}I", data, 4)
    pos = 4 + 4 * count
    rows = []
    for size in sizes:
        rows.append(data[pos:pos+size])
        pos += size
    if pos != len(data):
        raise ValueError("Invalid event table")
    return by_entity(rows, 0)


def by_names(data):
    if len(data) % 32:
        raise ValueError("Invalid name table")
    return by_entity((data[i:i+32] for i in range(0, len(data), 32)), 28)


def changed(a, b):
    return {k for k in a.keys() | b.keys() if a.get(k) != b.get(k)}


def compose_actor_dat(original, official, calvin):
    base, prior, author = map(by_events, (original, official, calvin))
    old, new = changed(base, prior), changed(base, author)
    if old != EVENT_HPS or old & new or len(new) != 74:
        raise ValueError("Unexpected overlap in event actors")
    for key in old:
        author[key] = prior[key]
    for i, key in enumerate(sorted(old)):
        row = author[key]
        events = struct.unpack_from("<2H", row, 12)
        if events != (65535, 8700 + i):
            raise ValueError("Unexpected previous home point event IDs")
    rows = list(author.values())  # preserve the order and exact bytes of Calvin's actor blocks
    out = struct.pack(f"<{1+len(rows)}I", len(rows), *map(len, rows)) + b"".join(rows)
    if by_events(out) != author:
        raise ValueError("Rebuilt actors fail round-trip")
    return out


def compose_names(original, official, calvin):
    base, prior, author = map(by_names, (original, official, calvin))
    old, new = changed(base, prior), changed(base, author)
    if len(old) != 56 or len(new) != 78 or old & new or old & base.keys():
        raise ValueError("Unexpected overlap in entity names")
    author.update({key: prior[key] for key in old})
    out = b"".join(author[k] for k in sorted(author))
    if by_names(out) != author:
        raise ValueError("Rebuilt names fail round-trip")
    return out


def dialog_entries(data):
    if int.from_bytes(data[:3], "little") + 4 != len(data) or data[3] != 16:
        raise ValueError("Invalid dialog container")
    raw = data[4:].translate(bytes(i ^ 128 for i in range(256)))
    count = int.from_bytes(raw[:4], "little") // 4
    offsets = struct.unpack_from(f"<{count}I", raw) + (len(raw),)
    if list(offsets) != sorted(offsets):
        raise ValueError("Invalid dialog offsets")
    return [raw[a:b] for a,b in zip(offsets, offsets[1:])]


def compose_dialog(original, official, calvin, expected_indices):
    base, prior, author = map(dialog_entries, (original, official, calvin))
    if len(base) != 11720 or len(prior) != len(base) or len(author) != 11765 or author[:len(base)] != base:
        raise ValueError("Unexpected dialog composition")
    if {i for i in range(len(base)) if base[i] != prior[i]} != expected_indices:
        raise ValueError("Unexpected prior dialog indices")
    for i in expected_indices:
        author[i] = prior[i]
    offsets = []
    pos = len(author) * 4
    for row in author:
        offsets.append(pos)
        pos += len(row)
    raw = struct.pack(f"<{len(offsets)}I", *offsets) + b"".join(author)
    result = len(raw).to_bytes(3, "little") + b"\x10" + raw.translate(bytes(i ^ 128 for i in range(256)))
    if dialog_entries(result) != author:
        raise ValueError("Rebuilt dialog fails round-trip")
    return result


def build(archive, game, hair, public, output):
    output.mkdir(parents=True, exist_ok=True)
    records, deltas, seen = [], {}, set()
    with zipfile.ZipFile(archive) as z:
        if len(set(z.namelist())) != len(z.namelist()):
            raise ValueError("Duplicate donor ZIP entry")
        for line in z.read("MANIFEST.txt").decode("utf-8-sig").splitlines():
            fields = line.split(" | ")
            if len(fields) != 5 or not DAT.fullmatch(fields[0]):
                continue
            path, size, digest, basis, _ = fields
            if path.casefold() in seen:
                raise ValueError(f"Duplicate donor path: {path}")
            seen.add(path.casefold())
            author = z.read(DONOR_ROOT + path)
            if len(author) != int(size) or sha(author) != digest or z.read(MIRROR_ROOT + path) != author:
                raise ValueError(f"Donor manifest or mirror mismatch: {path}")
            if HASH.fullmatch(basis):
                original = (game / path).read_bytes()
                if sha(original) != basis:
                    raise ValueError(f"Retail SHA-256 mismatch: {path}")
            elif path in {f"ROM/490/{i}.DAT" for i in range(55, 63)} and basis == "(new file, no retail original)":
                original, basis = b"", None
            else:
                raise ValueError(f"Unexpected base-less DAT: {path}")
            inputs, outputs = {"retail": original}, {"shared": author}
            if path in PUBLIC:
                container, expected = PUBLIC[path]
                current = (public / container / path.lower()).read_bytes()
                if sha(current) != expected:
                    raise ValueError(f"Published basis mismatch: {path}")
                inputs["public"] = current
                if path == "ROM2/13/13.DAT":
                    outputs["shared"] = compose_actor_dat(original, current, author)
                elif path == "ROM2/13/103.DAT":
                    outputs["shared"] = compose_names(original, current, author)
                elif path in DIALOG_PRIOR:
                    outputs["shared"] = compose_dialog(original, current, author, DIALOG_PRIOR[path])
            if path in HAIR:
                width, original_hair_sha, ids = HAIR[path]
                hair_input = (hair / path).read_bytes()
                if sha(hair_input) != original_hair_sha:
                    raise ValueError(f"Shipped hair basis mismatch: {path}")
                inputs["hair"] = hair_input
                outputs = {"retail": author, "hair": compose_tables(original, hair_input, author, width, ids)}
                outputs["public"] = compose_tables(original, inputs["public"], author, width, ids) if path in PUBLIC else outputs["hair"]
            record = dict(path=path, author_sha256=digest,
                          inputs={k: sha(v) if basis else None for k,v in inputs.items()},
                          outputs={k: {"sha256": sha(v), "size":len(v)} for k,v in outputs.items()})
            for i, b in inputs.items():
                for k, a in outputs.items():
                    deltas[f"{len(records)}-{i}-{k}.vdp"] = encode(b, a)
            records.append(record)
    if len(records) != 30 or sum(d["inputs"]["retail"] is None for d in records) != 8:
        raise ValueError("Unexpected Chapter I pack contents")
    zip_path = output / FILENAME
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        def write(name, data):
            info = zipfile.ZipInfo(name, (2026, 11, 20, 22, 31, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)
        write("chapter-i.json", json.dumps({"dat": records}, separators=(",", ":")).encode())
        for name, data in deltas.items():
            write(name, data)
    descriptor = dict(dest=DEST, sha256=sha(zip_path.read_bytes()), manifest_slot="chapter_i")
    (output / (FILENAME + ".json")).write_text(json.dumps(descriptor, indent=2) + "\n", encoding="utf-8")
    print(f"{zip_path.name}: {zip_path.stat().st_size} bytes, SHA-256 {descriptor['sha256']}")
    for d in records:
        if len(d["inputs"]) > 1:
            print(d["path"], {k:v["sha256"] for k,v in d["outputs"].items()})


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("archive", type=Path)
    ap.add_argument("game", type=Path, help="Unmodified FINAL FANTASY XI folder, read only")
    ap.add_argument("hair", type=Path, help="Private folder with hash-verified installed hair FTABLE/VTABLE")
    ap.add_argument("public", type=Path, help="Private exact preimages from named official client chunks")
    ap.add_argument("output", type=Path, help="Private staging output, outside client/chunks")
    a = ap.parse_args()
    build(a.archive, a.game, a.hair, a.public, a.output)

