"""Focused offline regression checks. Set VANADUNNA_SOURCE to effective client root."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from build import build, FILES, PACK

SOURCE = os.environ.get('VANADUNNA_SOURCE')
XI = os.environ.get('XI_TOOLS_SRC', r'D:\Workshop\Claude\Output\xi-vision\xi-tools-src\src')

@unittest.skipUnless(SOURCE, 'VANADUNNA_SOURCE must point to client fixtures')
class ClientTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix='vanadunna-test-')
        cls.root = Path(cls.tmp.name)
        cls.report, cls.chunk = build(SOURCE, cls.root / 'first', XI)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_both_languages_preserve_other_records(self):
        self.assertEqual(set(self.report['files']), set(FILES))
        self.assertTrue(all(x['blank_target'] for x in self.report['files'].values()))

    def test_deterministic_package(self):
        _, chunk = build(SOURCE, self.root / 'second', XI)
        self.assertEqual(chunk, self.chunk)

    def test_refuse_occupied_target(self):
        with self.assertRaisesRegex(ValueError, 'already occupied'):
            build(self.root / 'first' / 'overlay', self.root / 'occupied', XI)

    def test_refuse_source_output_overlap(self):
        with self.assertRaisesRegex(ValueError, 'separate staging'):
            build(SOURCE, SOURCE, XI)

    def test_rebuild_cannot_overwrite_staging(self):
        with self.assertRaisesRegex(ValueError, 'already exists'):
            build(SOURCE, self.root / 'first', XI)

    def test_description_and_price_contract(self):
        spec = json.loads(Path(__file__).with_name('vanadunna.json').read_text(encoding='utf-8-sig'))
        e = spec['edits'][0]
        self.assertEqual(e['set'], {'jobs': 'all', 'level': 1})
        self.assertEqual(e['copy_from'], 21460)
        self.assertEqual(self.report['login_points'], 500)
        self.assertIn('MP+100%', e['strings']['en']['description'])
        self.assertIn('Geomancy +15', e['strings']['en']['description'])
        self.assertFalse(spec['server']['emit'])

if __name__ == '__main__':
    unittest.main()
