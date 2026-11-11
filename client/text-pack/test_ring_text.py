"""Offline ring text checks. Set TEXT_PACK_TEST_CLIENT for read-only real-DAT tests."""
import copy
import json
import os
from pathlib import Path
import struct
import unittest

import build

HERE = Path(__file__).resolve().parent
RINGS = {15793: (76, 10000), 11666: (76, 9000), 10796: (76, 10000),
         28528: (55668, 11111), 28562: (55668, 12000)}
FIXES = json.loads((HERE / 'fixes.json').read_text())['items']


def description_span(data, item_id):
    # Independently identify a single record and its fifth string, using the
    # maintained five-string table format rather than assuming a record grid.
    signature = b'\x05\0\0\0\x2c\0\0\0'
    key = struct.pack('<I', item_id)
    candidates = []
    at = data.find(key)
    while at != -1:
        table = data.find(signature, at, at + 0x100)
        if table != -1:
            candidates.append((at, table))
        at = data.find(key, at + 1)
    if len(candidates) != 1:
        raise AssertionError((item_id, candidates))
    record, table = candidates[0]
    offset = struct.unpack_from('<I', data, table + 4 + 8 * 4)[0]
    return table + offset + 0x1c, record + 0x280


class RingCatalogTests(unittest.TestCase):
    def test_exact_scope_and_description_only(self):
        rings = [f for f in FIXES if f['id'] in RINGS]
        self.assertEqual(len(rings), 5)
        self.assertEqual({f['id'] for f in rings}, set(RINGS))
        for fix in rings:
            with self.subTest(item=fix['id']):
                fid, cap = RINGS[fix['id']]
                self.assertEqual(fix['fid'], fid)
                self.assertEqual(fix['old_name'], fix['new_name'])
                self.assertEqual(fix['old_log'], fix['new_log'])
                expected = ('Experience point bonus: +100%\n'
                            'Maximum duration: 720 min.\n'
                            f'Maximum bonus: {cap}')
                self.assertEqual(fix['old_desc'], expected)
                self.assertEqual(fix['new_desc'], expected.replace('Experience', 'Exemplar'))
        duo = next(f for f in rings if f['id'] == 28562)
        self.assertEqual(duo['new_name'], 'Duodec. Ring')


@unittest.skipUnless(os.environ.get('TEXT_PACK_TEST_CLIENT'), 'Set TEXT_PACK_TEST_CLIENT')
class RingClientTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.files = {}
        for fid in {f[0] for f in RINGS.values()}:
            path = Path(os.environ['TEXT_PACK_TEST_CLIENT']) / build.rom_path(os.environ['TEXT_PACK_TEST_CLIENT'], fid)
            raw = path.read_bytes()
            cls.files[fid] = (path, raw, raw.translate(build.ROT_DEC))

    def test_roundtrip_and_only_descriptions_change(self):
        for fid, (path, raw, original) in self.files.items():
            with self.subTest(fid=fid):
                self.assertEqual(original.translate(build.ROT_ENC), raw)
                edited = bytearray(original)
                spans = []
                for fix in FIXES:
                    if fix['id'] not in RINGS or fix['fid'] != fid:
                        continue
                    span = description_span(original, fix['id'])
                    spans.append(span)
                    build.edit_item(edited, fix)
                    a, b = span
                    self.assertEqual(edited[a:b].rstrip(b'\0'), fix['new_desc'].encode('ascii'))
                encoded = edited.translate(build.ROT_ENC)
                self.assertEqual(encoded.translate(build.ROT_DEC), edited)
                self.assertEqual(len(encoded), len(raw))
                restored = bytearray(edited)
                for a, b in spans:
                    restored[a:b] = original[a:b]
                self.assertEqual(restored, original)  # Includes names, icons and every other record.
                self.assertEqual(path.read_bytes(), raw)  # The input client is never written.

    def test_fail_closed_on_mismatch_overflow_and_unknown_id(self):
        fix = next(f for f in FIXES if f['id'] == 15793)
        original = self.files[fix['fid']][2]
        for change in ({'old_desc': 'incorrect source'}, {'new_desc': 'x' * 1000}, {'id': 0xffffffff}):
            with self.subTest(change=change):
                altered = copy.deepcopy(fix)
                altered.update(change)
                with self.assertRaises(SystemExit):
                    build.edit_item(bytearray(original), altered)

    def test_skillz_record_is_preserved(self):
        original = self.files[55668][2]
        edited = bytearray(original)
        for fix in FIXES:
            if fix['id'] in RINGS and fix['fid'] == 55668:
                build.edit_item(edited, fix)
        # This client may already contain the existing Skillz patch. Do not
        # claim it is untouched retail or apply that unrelated patch again.
        a, b = description_span(original, 27593)
        self.assertEqual(edited[a:b], original[a:b])


if __name__ == '__main__':
    unittest.main()
