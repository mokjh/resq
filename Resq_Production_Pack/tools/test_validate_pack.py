#!/usr/bin/env python3
"""Mutation tests: confirm the specification checker rejects actual bad inputs."""
from __future__ import annotations
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from validate_pack import validate, ValidationFailure

SOURCE=Path(__file__).resolve().parents[1]

class PackValidatorTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)/'pack'
        shutil.copytree(SOURCE,self.root,ignore=shutil.ignore_patterns('evidence','__pycache__'))
    def tearDown(self):
        self.temp.cleanup()
    def mutate(self,name,change):
        path=self.root/name
        data=json.loads(path.read_text())
        change(data)
        path.write_text(json.dumps(data))
    def test_valid_pack(self):
        self.assertEqual(validate(self.root)['status'],'passed')
    def test_missing_equipment(self):
        self.mutate('content/missions.json',lambda d:d[0]['default_loadout'].update({'imaginary_tool':1}))
        with self.assertRaisesRegex(ValidationFailure,'missing equipment'):
            validate(self.root)
    def test_over_budget(self):
        self.mutate('content/missions.json',lambda d:d[0].update(supply_budget=1))
        with self.assertRaisesRegex(ValidationFailure,'exceeds budget'):
            validate(self.root)
    def test_duplicate_mission(self):
        self.mutate('content/missions.json',lambda d:d.append(d[0].copy()))
        with self.assertRaisesRegex(ValidationFailure,'Duplicate mission'):
            validate(self.root)
    def test_outside_event(self):
        self.mutate('content/missions.json',lambda d:d[0]['event_briefs'][0].update(simulation_second=9999))
        with self.assertRaisesRegex(ValidationFailure,'outside mission'):
            validate(self.root)
    def test_rescue_minimum(self):
        self.mutate('content/missions.json',lambda d:d[0].update(minimum_rescues=999))
        with self.assertRaisesRegex(ValidationFailure,'minimum exceeds'):
            validate(self.root)
    def test_task_cycle(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][0]['depends_on'].append('T02'))
        with self.assertRaisesRegex(ValidationFailure,'Task cycle'):
            validate(self.root)
    def test_false_release_claim(self):
        self.mutate('content/release_manifest.template.json',lambda d:d['qualified_devices'].append('invented proof'))
        with self.assertRaisesRegex(ValidationFailure,'must not claim'):
            validate(self.root)
    def test_duplicate_json_key(self):
        path=self.root/'content/asset_manifest.template.json'
        path.write_text('{"schema_version":1,"schema_version":2,"assets":[]}')
        with self.assertRaisesRegex(ValidationFailure,'Duplicate JSON key'):
            validate(self.root)

if __name__ == '__main__':
    unittest.main(verbosity=2)
