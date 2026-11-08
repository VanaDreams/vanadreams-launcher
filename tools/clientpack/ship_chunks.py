r"""Upload the extra game-file packs under client/chunks/ to the vanadreams-client R2 bucket and add them to the
live client manifest, so every player gets them on their next Install/Patch (Play runs the check).

    python tools/clientpack/ship_chunks.py [--dry-run]

Runs in the "Ship client packs" GitHub workflow on every push to main that touches client/chunks/, so merging a pack
is what ships it. Layout:

    client/chunks/<client_ver>/<name>.zip           the pack, unpacked over the game folder
    client/chunks/<client_ver>/<name>.zip.json      optional: {"dest": "{ashita}/...", "sha256": "..."}

Unchanged ZIP bytes are not reuploaded. Sidecar metadata is applied even when the ZIP hash is unchanged.
A changed pack replaces its entry in place; pinned dependencies determine the final safe extraction order.
Each zip is uploaded and read back before the manifest changes; the old manifest is kept beside it as
manifest.backup-<UTC time>.json. Needs CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID in the environment.
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone

BUCKET = "vanadreams-client"
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "chunks")
WRANGLER = ["npx", "--yes", "wrangler@4"]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while block := f.read(1 << 20):
            h.update(block)
    return h.hexdigest()


def safe_relative(path):
    return (isinstance(path, str) and bool(path) and ":" not in path
            and all(part not in ("", ".", "..") for part in path.replace("\\", "/").split("/")))


def valid_hash(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{64}", value) is not None


def chunk_entry(path, dest=None, side=None):
    """Validate the sidecar against the actual archive before uploading anything."""
    side = side or {}
    with zipfile.ZipFile(path) as z:
        infos = [i for i in z.infolist() if not i.is_dir()]
        entries = {i.filename.replace("\\", "/").lower(): i for i in infos}
        if len(entries) != len(infos) or any(not safe_relative(i.filename) for i in infos):
            raise ValueError(f"{path}: unsafe or duplicate archive paths")
        checks = side.get("verify", [])
        seen = set()
        for check in checks:
            rel, want = check.get("path"), check.get("sha256")
            if not safe_relative(rel) or not valid_hash(want):
                raise ValueError(f"{path}: invalid verification entry")
            key = rel.replace("\\", "/").lower()
            if key in seen or key not in entries:
                raise ValueError(f"{path}: duplicate or missing verification file {rel}")
            seen.add(key)
            if hashlib.sha256(z.read(entries[key])).hexdigest() != want.lower():
                raise ValueError(f"{path}: verification hash differs for {rel}")
    chunk = {
        "name": os.path.basename(path),
        "size": os.path.getsize(path),
        "sha256": sha256(path),
        "files": len(infos),
        "unpacked": sum(i.file_size for i in infos),
    }
    if dest:
        chunk["dest"] = dest
    for key in ("requires", "verify", "required"):
        if key in side:
            chunk[key] = side[key]
    if side.get("sha256") and side["sha256"].lower() != chunk["sha256"]:
        raise ValueError(f"{path}: archive hash differs from its sidecar")
    if chunk.get("required") and not checks:
        raise ValueError(f"{path}: required packs must verify their installed files")
    return chunk


def order_chunks(chunks):
    """Keep the baseline order, with pinned prerequisites before their dependent packs."""
    have = {}
    for chunk in chunks:
        key = chunk["name"].lower()
        if key in have:
            raise ValueError(f"Duplicate chunk: {chunk['name']}")
        have[key] = chunk
    ordered, visiting, done = [], set(), set()

    def visit(chunk):
        key = chunk["name"].lower()
        if key in done:
            return
        if key in visiting:
            raise ValueError(f"Dependency cycle at {chunk['name']}")
        visiting.add(key)
        for need in chunk.get("requires", []):
            dependency = have.get(need.get("name", "").lower())
            if (dependency is None or not valid_hash(need.get("sha256"))
                    or dependency["sha256"].lower() != need["sha256"].lower()):
                raise ValueError(f"{chunk['name']}: missing or changed prerequisite {need.get('name')}")
            if chunk.get("dest", "").startswith("{ashita}") != dependency.get("dest", "").startswith("{ashita}"):
                raise ValueError(f"{chunk['name']}: prerequisites must share the game or Ashita root")
            visit(dependency)
        visiting.remove(key)
        done.add(key)
        ordered.append(chunk)

    for chunk in chunks:
        visit(chunk)
    return ordered


def merge_chunks(existing, packs):
    """Replace in place, never append a changed baseline after a dependent overlay."""
    replacements = {entry["name"].lower(): entry for _, entry in packs}
    result = [replacements.pop(chunk["name"].lower(), chunk) for chunk in existing]
    result.extend(replacements.values())
    return order_chunks(result)


def r2(*args):
    subprocess.run(WRANGLER + ["r2", "object", *args, "--remote"], check=True)


def r2_get(key, out):
    r2("get", f"{BUCKET}/{key}", "--file", out)


def r2_put(key, path, content_type):
    r2("put", f"{BUCKET}/{key}", "--file", path, "--content-type", content_type)


def summary(line):
    print(line, flush=True)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as f:
            f.write(line + "\n")


def ship_version(ver, folder, tmp, dry_run):
    packs = []
    for name in sorted(os.listdir(folder)):
        if not name.lower().endswith(".zip"):
            continue
        path = os.path.join(folder, name)
        side = {}
        if os.path.exists(path + ".json"):
            with open(path + ".json", encoding="utf-8") as f:
                side = json.load(f)
        entry = chunk_entry(path, side.get("dest"), side)
        if side.get("sha256") and side["sha256"].lower() != entry["sha256"]:
            sys.exit(f"{ver}/{name}: sha256 {entry['sha256']} does not match its .json ({side['sha256']})")
        packs.append((path, entry))
    if not packs:
        return

    live = os.path.join(tmp, f"{ver}-manifest.live.json")
    r2_get(f"{ver}/manifest.json", live)
    with open(live, encoding="utf-8") as f:
        manifest = json.load(f)
    if manifest.get("client_ver") != ver:
        sys.exit(f"{ver}/manifest.json says client_ver {manifest.get('client_ver')}; refusing to change it")
    old_count = len(manifest["chunks"])
    have = {c["name"]: c for c in manifest["chunks"]}

    # Validate the complete dependency graph before any archive or manifest write.
    merged = merge_chunks(manifest["chunks"], packs)
    todo = []
    for path, e in packs:
        if have.get(e["name"], {}).get("sha256") == e["sha256"]:
            summary(f"- `{ver}/{e['name']}` already live, unchanged")
        else:
            todo.append((path, e))
    if not todo and merged == manifest["chunks"]:
        return

    for path, e in todo:
        summary(f"- `{ver}/{e['name']}` {e['size']:,} bytes, {e['files']} files, sha256 `{e['sha256']}`")
        if dry_run:
            continue
        r2_put(f"{ver}/{e['name']}", path, "application/zip")
        back = os.path.join(tmp, "readback.zip")
        r2_get(f"{ver}/{e['name']}", back)
        if sha256(back) != e["sha256"]:
            sys.exit(f"{ver}/{e['name']}: the uploaded copy reads back with a different hash; manifest left unchanged")
        os.remove(back)

    manifest["chunks"] = merged
    manifest["total_size"] = sum(c["size"] for c in manifest["chunks"])
    manifest["total_unpacked"] = sum(c["unpacked"] for c in manifest["chunks"])
    new = os.path.join(tmp, f"{ver}-manifest.json")
    with open(new, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    summary(f"- `{ver}/manifest.json`: {old_count} chunks -> {len(manifest['chunks'])}")
    if dry_run:
        return

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    r2_put(f"{ver}/manifest.backup-{stamp}.json", live, "application/json")
    r2_put(f"{ver}/manifest.json", new, "application/json")
    check = os.path.join(tmp, "manifest.check.json")
    r2_get(f"{ver}/manifest.json", check)
    if sha256(check) != sha256(new):
        sys.exit(f"{ver}/manifest.json reads back different from what was uploaded")
    summary(f"- shipped; old manifest kept as `{ver}/manifest.backup-{stamp}.json`")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="read the live manifest and report, upload nothing")
    a = ap.parse_args()
    root = os.path.normpath(ROOT)
    with tempfile.TemporaryDirectory() as tmp:
        for ver in sorted(os.listdir(root)):
            folder = os.path.join(root, ver)
            if os.path.isdir(folder):
                ship_version(ver, folder, tmp, a.dry_run)


if __name__ == "__main__":
    main()
