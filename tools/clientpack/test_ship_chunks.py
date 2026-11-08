import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import ship_chunks as ship


class PackTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.hair = self.pack("hair.zip", b"old")
        self.faces = self.pack("faces.zip", b"new")
        self.faces[1]["requires"] = [{"name": "hair.zip", "sha256": self.hair[1]["sha256"]}]
        self.faces[1]["required"] = True
        self.faces[1]["verify"] = [{"path": "FFXiMain.dll", "sha256": hashlib.sha256(b"new").hexdigest()}]

    def pack(self, name, data):
        path = self.root / name
        with zipfile.ZipFile(path, "w") as z:
            z.writestr("FFXiMain.dll", data)
        return str(path), ship.chunk_entry(path)

    def test_changed_baseline_does_not_move_after_face_pack(self):
        older = dict(self.hair[1], sha256="0" * 64)
        chunks = ship.merge_chunks([older, self.faces[1]], [self.hair])
        self.assertEqual(["hair.zip", "faces.zip"], [c["name"] for c in chunks])

    def test_reversed_input_is_sorted_and_other_chunks_preserved(self):
        other = self.pack("other.zip", b"unrelated")[1]
        result = ship.merge_chunks([other, self.faces[1], self.hair[1]], [])
        self.assertEqual(["other.zip", "hair.zip", "faces.zip"], [c["name"] for c in result])

    def test_missing_or_changed_dependency_fails_before_upload(self):
        for baseline in [[], [dict(self.hair[1], sha256="0" * 64)]]:
            with self.assertRaises(ValueError):
                ship.merge_chunks(baseline, [self.faces])

    def test_cycles_and_duplicates_rejected(self):
        hair = copy.deepcopy(self.hair[1])
        hair["requires"] = [{"name": "faces.zip", "sha256": self.faces[1]["sha256"]}]
        with self.assertRaises(ValueError):
            ship.order_chunks([hair, self.faces[1]])
        with self.assertRaises(ValueError):
            ship.order_chunks([hair, hair])

    def test_sidecar_verifies_real_archive_bytes(self):
        result = ship.chunk_entry(self.faces[0], side=self.faces[1])
        self.assertEqual(self.faces[1], result)
        side = copy.deepcopy(self.faces[1])
        side["verify"][0]["sha256"] = "0" * 64
        with self.assertRaises(ValueError):
            ship.chunk_entry(self.faces[0], side=side)

    def test_unsafe_missing_and_duplicate_checks_rejected(self):
        for rel in ["../FFXiMain.dll", "C:/file", "/file", "missing"]:
            side = copy.deepcopy(self.faces[1])
            side["verify"][0]["path"] = rel
            with self.assertRaises(ValueError):
                ship.chunk_entry(self.faces[0], side=side)
        side = copy.deepcopy(self.faces[1])
        side["verify"].append(side["verify"][0])
        with self.assertRaises(ValueError):
            ship.chunk_entry(self.faces[0], side=side)

    def test_required_pack_without_checks_rejected(self):
        with self.assertRaises(ValueError):
            ship.chunk_entry(self.hair[0], side={"required": True})

    def test_metadata_only_change_updates_manifest_without_reupload(self):
        for path, chunk in [self.hair, self.faces]:
            Path(path + ".json").write_text(json.dumps(chunk))
        original = {"client_ver": "30260904_1", "chunks": [self.hair[1],
            {k: v for k, v in self.faces[1].items() if k not in ("requires", "verify", "required")} ]}
        outputs = {}
        def get(key, path):
            if key.endswith("manifest.json"):
                Path(path).write_text(outputs.get(key, json.dumps(original)))
            else:
                raise AssertionError("unchanged ZIP should not be read back")
        def put(key, path, content_type):
            self.assertFalse(key.endswith(".zip"))
            outputs[key] = Path(path).read_text()
        with patch.object(ship, "r2_get", side_effect=get), patch.object(ship, "r2_put", side_effect=put):
            ship.ship_version("30260904_1", self.root, self.root, False)
        actual = json.loads(outputs["30260904_1/manifest.json"])
        self.assertEqual(self.faces[1], actual["chunks"][-1])
        self.assertTrue(any("manifest.backup-" in key for key in outputs))

    def test_invalid_pinned_baseline_does_not_upload_anything(self):
        # Only the dependent is in the pack directory, but the live baseline hash has changed.
        folder = self.root / "packs"
        folder.mkdir()
        target = folder / "faces.zip"
        target.write_bytes(Path(self.faces[0]).read_bytes())
        Path(str(target) + ".json").write_text(json.dumps(self.faces[1]))
        original = {"client_ver": "30260904_1", "chunks": [dict(self.hair[1], sha256="0" * 64)]}
        with patch.object(ship, "r2_get", side_effect=lambda key, path: Path(path).write_text(json.dumps(original))), patch.object(ship, "r2_put") as put:
            with self.assertRaises(ValueError):
                ship.ship_version("30260904_1", folder, self.root, False)
            put.assert_not_called()


if __name__ == "__main__":
    unittest.main()
