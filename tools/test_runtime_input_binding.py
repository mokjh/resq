"""The task snapshot is shared by all native check directories."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import adapter_unity


class RuntimeInputBindingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.live = self.root / 'game/Assets/rules.cs'
        self.live.parent.mkdir(parents=True)
        self.live.write_bytes(b'actual rules')
        self.snapshot = self.root / 'evidence/runs/C06/run/attempt/inputs/source/game/Assets/rules.cs'
        self.snapshot.parent.mkdir(parents=True)
        self.snapshot.write_bytes(self.live.read_bytes())
        self.manifest = self.snapshot.parents[3] / 'source-manifest.json'
        self.rows = [{'path': str(self.snapshot.relative_to(self.root)),
                      'sha256': hashlib.sha256(self.snapshot.read_bytes()).hexdigest()}]
        self.identity = {'source_manifest': {'path': str(self.manifest.relative_to(self.root))}}
        self.write_manifest()

    def write_manifest(self):
        self.manifest.write_text(json.dumps({'files': self.rows}))

    def check(self):
        with patch.object(adapter_unity, 'ROOT', self.root):
            adapter_unity.verify_runtime_inputs(self.identity)

    def test_shared_task_snapshot_accepts_inputs_for_both_check_directories(self):
        for check in ('check-c06', 'check-runtime-units'):
            (self.root / 'evidence/runs/C06/run/attempt/checks' / check).mkdir(parents=True)
            self.check()

    def test_live_change_rejected_while_preserved_snapshot_remains_intact(self):
        self.live.write_bytes(b'changed rules')
        with self.assertRaisesRegex(RuntimeError, 'input changed'):
            self.check()
        self.assertEqual(b'actual rules', self.snapshot.read_bytes())

    def test_missing_live_input_rejected(self):
        self.live.unlink()
        with self.assertRaisesRegex(RuntimeError, 'input changed'):
            self.check()

    def test_wrong_snapshot_prefix_rejected(self):
        self.rows[0]['path'] = 'evidence/foreign/source/game/Assets/rules.cs'
        self.write_manifest()
        with self.assertRaisesRegex(RuntimeError, 'prefix mismatch'):
            self.check()


if __name__ == '__main__':
    unittest.main()
