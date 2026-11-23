"""Build the launcher's file hash index from verified published game and viewer ZIPs."""

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import zipfile


def build_index(manifest_path, archives, output):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    with output.open("wb") as stream, gzip.GzipFile(filename="", mode="wb", fileobj=stream, mtime=0) as packed:
        with io.TextIOWrapper(packed, encoding="utf-8", newline="\n") as index:
            for chunk in manifest["chunks"]:
                dest = chunk.get("dest", "FINAL FANTASY XI")
                if dest not in {"FINAL FANTASY XI", "PlayOnlineViewer"}:
                    continue
                archive = archives / chunk["name"]
                with archive.open("rb") as source:
                    digest = hashlib.file_digest(source, "sha256").hexdigest()
                    if archive.stat().st_size != chunk["size"] or digest != chunk["sha256"].lower():
                        raise ValueError(f"{archive} does not match the published manifest")
                    with zipfile.ZipFile(source) as package:
                        index.write(f"{chunk['name']}\t{digest}\t{dest}\n")
                        for entry in package.infolist():
                            if entry.is_dir():
                                continue
                            name = entry.filename.replace("\\", "/")
                            if any(c in name for c in "\t\r\n"):
                                raise ValueError(f"Unsupported file name in {archive}: {name!r}")
                            with package.open(entry) as data:
                                digest = hashlib.file_digest(data, "sha256").hexdigest()
                            index.write(f"{name}\t{entry.file_size}\t{digest}\n")
                        index.write("\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("archives", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build_index(args.manifest, args.archives, args.output)
