r"""Upload the extra game-file packs under client/chunks/ to the vanadreams-client R2 bucket and add them to the
live client manifest, so every player gets them on their next Install/Patch (Play runs the check).

    python tools/clientpack/ship_chunks.py [--dry-run]

Runs in the "Ship client packs" GitHub workflow on every push to main that touches client/chunks/, so merging a pack
is what ships it. Layout:

    client/chunks/<client_ver>/<name>.zip           the pack, unpacked over the game folder
    client/chunks/<client_ver>/<name>.zip.json      optional: {"dest": "{ashita}/...", "sha256": "..."}

A pack already in the live manifest with the same name and hash is left alone. A changed pack replaces its entry.
Each zip is uploaded and read back before the manifest changes; the old manifest is kept beside it as
manifest.backup-<UTC time>.json. Needs CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID in the environment.
"""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone

BUCKET = "vanadreams-client"
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "chunks")
# On Windows npx is npx.cmd, which subprocess cannot start by its bare name.
WRANGLER = [shutil.which("npx") or "npx", "--yes", "wrangler@4"]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while block := f.read(1 << 20):
            h.update(block)
    return h.hexdigest()


def chunk_entry(path, dest=None):
    """Same fields pack.py writes for a game chunk."""
    with zipfile.ZipFile(path) as z:
        infos = [i for i in z.infolist() if not i.is_dir()]
    chunk = {
        "name": os.path.basename(path),
        "size": os.path.getsize(path),
        "sha256": sha256(path),
        "files": len(infos),
        "unpacked": sum(i.file_size for i in infos),
    }
    if dest:
        chunk["dest"] = dest
    return chunk


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
            with open(path + ".json", encoding="utf-8-sig") as f:  # Windows PowerShell writes a BOM
                side = json.load(f)
        entry = chunk_entry(path, side.get("dest"))
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

    todo = []
    for path, e in packs:
        if have.get(e["name"], {}).get("sha256") == e["sha256"]:
            summary(f"- `{ver}/{e['name']}` already live, unchanged")
        else:
            todo.append((path, e))
    if not todo:
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

    names = {e["name"] for _, e in todo}
    manifest["chunks"] = [c for c in manifest["chunks"] if c["name"] not in names] + [e for _, e in todo]
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
