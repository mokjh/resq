"""Safety tests for scoped admission/cancellation. No Unity result is simulated."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

import run_unity_bounded as bounded


class BoundedNativeSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='rsq-', dir='/tmp')
        self.root = Path(self.temp.name)
        (self.root/'game').mkdir()
        self.root_patch = patch.object(bounded, 'ROOT', self.root)
        self.root_patch.start()
        self.editor_patch = patch.object(bounded, 'EDITOR', Path('/usr/bin/true'))
        self.editor_patch.start()
        self.addCleanup(self.root_patch.stop)
        self.addCleanup(self.editor_patch.stop)
        self.addCleanup(self.temp.cleanup)
        self.free = patch.object(bounded.shutil, 'disk_usage', return_value=shutil._ntuple_diskusage(20*bounded.GIB, 10*bounded.GIB, 10*bounded.GIB))
        self.free.start()
        self.addCleanup(self.free.stop)

    def test_low_capacity_never_launches_editor(self):
        with patch.object(bounded.shutil, 'disk_usage', return_value=shutil._ntuple_diskusage(20*bounded.GIB,19*bounded.GIB,bounded.GIB)), patch.object(bounded,'run_process') as launch:
            with self.assertRaisesRegex(RuntimeError,'BOUNDED_ADMISSION_BLOCKED'):
                bounded.execute('runtime-content',self.root/'evidence/run',[])
            launch.assert_not_called()
        denial=json.loads((self.root/'evidence/run/outcome.json').read_text())
        self.assertEqual(denial['status'],'blocked')
        self.assertFalse(denial['process_started'])
        self.assertFalse((self.root/'game/Temp').exists())

    def test_commands_has_own_exact_profile_and_capacity_boundary(self):
        profile=bounded.STAGES['runtime-commands']
        self.assertEqual(profile['task'],'C02')
        required=bounded.RESERVE+profile['growth']+profile['buffer']
        self.assertEqual(required,2415919104)
        with patch.object(bounded,'memory_pressure_level',return_value=1):
            with patch.object(bounded.shutil,'disk_usage',return_value=shutil._ntuple_diskusage(20*bounded.GIB,0,required-1)):
                self.assertEqual(bounded.preflight('runtime-commands')['status'],'blocked')
            with patch.object(bounded.shutil,'disk_usage',return_value=shutil._ntuple_diskusage(20*bounded.GIB,0,required)):
                receipt=bounded.preflight('runtime-commands')
                self.assertEqual(receipt['status'],'passed')
                self.assertEqual(receipt['resource_profile_sha256'],bounded.sha256_file(bounded.RUNTIME_PROFILE_PATH))

    def test_critical_memory_never_launches_editor(self):
        with patch.object(bounded,'memory_pressure_level',return_value=4),patch.object(bounded,'run_process') as launch:
            with self.assertRaisesRegex(RuntimeError,'BOUNDED_ADMISSION_BLOCKED'):
                bounded.execute('runtime-content',self.root/'evidence/run',[])
            launch.assert_not_called()

    def test_project_override_refused(self):
        with self.assertRaisesRegex(ValueError,'ownership flags'):
            bounded.execute('runtime-content',self.root/'evidence/run',['-projectPath','/other/project'])

    def test_setup_project_cannot_replace_game_content_tests(self):
        with self.assertRaisesRegex(ValueError,'bootstrap project'):
            bounded.execute('runtime-content',self.root/'evidence/run',[],setup_project=self.root/'other')

    def test_linked_evidence_destination_refused(self):
        (self.root/'evidence').symlink_to('/tmp')
        with self.assertRaisesRegex(ValueError,'Linked owned path'):
            bounded.execute('runtime-content',self.root/'evidence/run',[])

    def test_positive_growth_triggers_owned_cancellation_reason(self):
        profile=dict(bounded.STAGES['runtime-content'],growth=32768)
        def observe(command, **kwargs):
            (self.root/'game/import-output').write_bytes(b'x'*65536)
            reason=kwargs['stop_check']()
            self.assertIn('positive file growth budget reached',reason)
            return {'status':'failed','exit_code':-15,'stop_reason':reason}
        with patch.dict(bounded.STAGES,{'runtime-content':profile}),patch.object(bounded,'run_process',side_effect=observe):
            outcome=bounded.execute('runtime-content',self.root/'evidence/run',[])
        self.assertEqual(outcome['exit_code'],-15)
        measured=json.loads((self.root/'evidence/run/measurements.json').read_text())
        self.assertGreaterEqual(measured['peak_positive_growth_bytes'],65536)
        self.assertFalse(measured['task_accepted'])

    def test_free_volume_guard_has_priority(self):
        def observe(command,**kwargs):
            with patch.object(bounded.shutil,'disk_usage',return_value=shutil._ntuple_diskusage(20*bounded.GIB,19*bounded.GIB,bounded.GIB)):
                reason=kwargs['stop_check']()
            self.assertIn('below2GiB hard reserve',reason)
            return {'status':'failed','exit_code':-15,'stop_reason':reason}
        with patch.object(bounded,'run_process',side_effect=observe):
            bounded.execute('runtime-content',self.root/'evidence/run',[])

    def test_sustained_critical_memory_stops_owned_attempt(self):
        def observe(command,**kwargs):
            with patch.object(bounded,'memory_pressure_level',return_value=4),patch.object(bounded.time,'monotonic',side_effect=[0.0,6.0]):
                self.assertIsNone(kwargs['stop_check']())
                reason=kwargs['stop_check']()
            self.assertIn('sustained critical memory pressure',reason)
            return {'status':'failed','exit_code':-15,'stop_reason':reason}
        with patch.object(bounded,'run_process',side_effect=observe):
            bounded.execute('runtime-content',self.root/'evidence/run',[])

    def test_short_private_temp_and_gradle_paths_even_for_deep_setup_project(self):
        setup=self.root/'evidence/foundation/f03-bounded-20261016/bootstrap-project'
        setup.mkdir(parents=True)
        def observe(command,**kwargs):
            environment=dict(item.split('=',1) for item in command[1:] if '=' in item and not item.startswith('-'))
            self.assertLessEqual(len(environment['TMPDIR'].encode()),64)
            self.assertTrue(Path(environment['TMPDIR']).is_relative_to(self.root/'game/Temp'))
            self.assertTrue(Path(environment['GRADLE_USER_HOME']).is_relative_to(self.root/'evidence/run'))
            self.assertEqual(command[command.index('-projectPath')+1],str(setup))
            self.assertEqual(kwargs['monitored_volumes'],[])
            return {'status':'passed','exit_code':0,'stop_reason':None}
        with patch.object(bounded,'run_process',side_effect=observe):
            bounded.execute('render-packages',self.root/'evidence/run',[],setup_project=setup)


if __name__ == '__main__':
    unittest.main()
