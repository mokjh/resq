"""Synthetic F04 byte-binding fixtures. Never actual Editor/build evidence."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

import bootstrap_doctor as doctor
import build_candidate as candidate
import task_execution as execution


class BuildCandidateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='resq-synthetic-build-binding-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        for target in (doctor, candidate, execution):
            change = patch.object(target, 'ROOT', self.root)
            change.start(); self.addCleanup(change.stop)
        capacity = patch.object(candidate.shutil, 'disk_usage', return_value=SimpleNamespace(free=10*1024**3))
        capacity.start(); self.addCleanup(capacity.stop)
        self.directory = self.root / 'evidence/runs/F04/synthetic-fixture/attempt-01'
        self.directory.mkdir(parents=True)
        self.put('automation/tasks.json', {})
        self.put('automation/checks.json', {})
        self.put('automation/gates.json', {})
        self.put('automation/resource_estimates.json', {'F04': {'peak_bytes': 2*1024**3}})
        self.put('automation/local_operation_profiles.json',{'reserve_bytes':2*1024**3,
            'stages':{'android-probe-build':{'task':'F04','growth':2*1024**3,'buffer':256*1024**2}}})
        self.put('specification/content/acceptance_cases.json', {})
        self.put('specification/content/mission_contracts.json', {'synthetic_fixture_only': True})
        self.put('game/ProjectSettings/ProjectVersion.txt', b'm_EditorVersion: synthetic-editor-1\n')
        self.put('game/Packages/manifest.json', {'dependencies': {'invalid.synthetic': '1'}})
        self.put('game/Assets/Probe.cs', b'// Synthetic fixture, not gameplay\n')
        initial = doctor.snapshot_identity(self.directory, execution.source_files(), {'synthetic': True})
        self.context = {'task': {'id': 'F04'}, 'run_dir': self.directory, 'identity': initial}
        # Simulate native resolution only inside this synthetic temporary fixture.
        self.put('game/Packages/packages-lock.json', {'dependencies': {'invalid.synthetic': {'version': '1'}}})
        self.put('game/Assets/Probe.cs.meta', b'synthetic-metadata-only\n')
        self.package_path = self.directory / 'package-metadata/invalid.synthetic.json'
        self.put(self.package_path, {'name': 'invalid.synthetic', 'version': '1'})
        required = ('game/ProjectSettings/ProjectVersion.txt','game/Packages/manifest.json','game/Packages/packages-lock.json')
        for name in required:
            self.put(self.directory/'resolved-inputs'/name,(self.root/name).read_bytes())
        self.lock = {'schema_version': 1, 'status': 'resolved_local_candidate',
                     'inputs': {name: self.reference(self.directory/'resolved-inputs'/name) for name in (
                         'game/ProjectSettings/ProjectVersion.txt', 'game/Packages/manifest.json',
                         'game/Packages/packages-lock.json')},
                     'packages': {'invalid.synthetic': {'version': '1', 'manifest': self.reference(self.package_path)}},
                     'android_tools': {}}
        for label in ('editor', 'sdk', 'ndk', 'jdk'):
            tool = self.root / ('synthetic-tools/' + label)
            self.put(tool, ('Synthetic unit fixture executable: ' + label).encode())
            entry = {'version': 'synthetic-editor-1' if label == 'editor' else 'synthetic-1',
                     'executable': dict(self.reference(tool), path=str(tool))}
            if label == 'editor': self.lock['editor'] = entry
            else: self.lock['android_tools'][label] = entry
        self.context['launched_editor'] = deepcopy(self.lock['editor']['executable'])
        self.lock_path = self.root / 'automation/toolchain.lock.json'
        self.put(self.lock_path, self.lock)
        self.apk = self.directory / 'resq.apk'
        self.write_apk()
        self.report_path = self.directory / 'build-report.json'
        self.report = {'result': 'Succeeded', 'total_errors': 0, 'total_size': self.apk.stat().st_size,
                       'output_path': str(self.apk), 'apk_byte_length': self.apk.stat().st_size,
                       'apk_sha256': doctor.digest(self.apk), 'platform': 'Android',
                       'build_guid': 'synthetic-build-guid', 'unity_version': 'synthetic-editor-1'}
        self.put(self.report_path, self.report)
        self.packet = {'status': 'passed', 'case_outcomes': [
            {'case_id': f'check-f04/assertion/assertion_id=check-f04-a{i:02}', 'kind': 'assertion',
             'dimensions': {'assertion_id': f'check-f04-a{i:02}'}, 'status': 'passed',
             'metrics': {}, 'participant_alias': None, 'proof_index': None,
             'observations': {'synthetic_fixture_only': True}} for i in range(1, 5)]}
        self.refresh_packet()

    def put(self, path, value):
        path = Path(path); path = path if path.is_absolute() else self.root / path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value if isinstance(value, bytes) else (json.dumps(value, indent=2)+'\n').encode())
        return path

    def reference(self, path):
        return {'path': str(path.relative_to(self.root)), 'sha256': doctor.digest(path), 'byte_length': path.stat().st_size}

    def refresh_packet(self):
        self.packet['build_binding'] = {'build_guid': self.report['build_guid'],
            'apk_sha256': doctor.digest(self.apk), 'build_report_sha256': doctor.digest(self.report_path),
            'toolchain_lock_sha256': doctor.digest(self.lock_path)}

    def write_apk(self, architecture='arm64-v8a', header=None):
        with zipfile.ZipFile(self.apk, 'w') as archive:
            archive.writestr('AndroidManifest.xml', b'SYNTHETIC MANIFEST UNIT FIXTURE ONLY')
            archive.writestr('lib/'+architecture+'/libil2cpp.so', header or b'\x7fELF\x02\x01'+bytes(12)+b'\xb7\x00')

    def bind(self): candidate.bind_candidate(self.context, self.packet)

    def reject(self, message):
        before = deepcopy(self.context['identity'])
        with self.assertRaisesRegex((candidate.CandidateError, RuntimeError, ValueError), message): self.bind()
        self.assertEqual(self.context['identity'], before)

    def adapter_fixture(self):
        import adapter_unity
        import run_unity_bounded
        self.put('automation/resource_estimates.json', {'F04': {'peak_bytes': 2*1024**3}})
        summary={'schema_version':1,'status':'passed','evidence_kind':'automated','unity_version':'synthetic-editor-1',
            'build_binding':deepcopy(self.packet['build_binding']),'executed':4,'passed':4,'failed':0,'skipped':0,
            'cases':[dict(case_id=case['case_id'],status='passed',executed=True,observations=deepcopy(case['observations'])) for case in self.packet['case_outcomes']]}
        path=self.put(self.directory/'f04-native-summary.json',summary)
        self.packet['native_summary']=dict(self.reference(path),path=str(path))
        for case in self.packet['case_outcomes']:
            case['observations'].update(native_summary_sha256=doctor.digest(path),unity_version='synthetic-editor-1')
        self.put(self.directory/'unity-cases.json', self.packet)
        self.context['owner'] = {'pid': 12345, 'synthetic_fixture_only': True}
        editor = Path(self.lock['editor']['executable']['path'])
        patches = [patch.object(adapter_unity, 'ROOT', self.root),
                   patch.object(adapter_unity, 'check_storage', return_value={'status':'passed'}),
                   patch.object(adapter_unity, 'run_process', return_value={'status':'passed','exit_code':0}),
                   patch.object(run_unity_bounded,'preflight',return_value={'status':'passed'}),
                   patch.object(run_unity_bounded,'execute',return_value={'status':'passed','exit_code':0}),
                   patch.object(adapter_unity,'editor_for_version',return_value=editor)]
        for change in patches:
            change.start(); self.addCleanup(change.stop)
        return adapter_unity

    def test_adapter_binds_only_after_native_build_and_packet_pass(self):
        adapter = self.adapter_fixture()
        original = self.context['identity']
        with patch.object(candidate, 'bind_candidate', wraps=candidate.bind_candidate) as binding:
            outcomes = adapter.execute(['game','build','--profile','xr-probe'], self.context)
        binding.assert_called_once()
        self.assertIs(self.context['identity'], original)
        self.assertEqual(self.context['identity']['toolchain_hash'], doctor.digest(self.lock_path))
        self.assertEqual(len(outcomes), 4)

    def test_adapter_failed_packet_never_changes_candidate_identity(self):
        adapter = self.adapter_fixture()
        self.put(self.directory/'unity-cases.json', dict(self.packet,status='failed'))
        before = deepcopy(self.context['identity'])
        with patch.object(candidate, 'bind_candidate') as binding:
            with self.assertRaisesRegex(RuntimeError, 'case packet did not pass'):
                adapter.execute(['game','build','--profile','xr-probe'], self.context)
        binding.assert_not_called()
        self.assertEqual(self.context['identity'], before)

    def test_exact_resolved_candidate_retained_with_identity_alias(self):
        identity = self.context['identity']; before = deepcopy(identity); lock_bytes = self.lock_path.read_bytes()
        self.bind()
        self.assertIs(self.context['identity'], identity)
        self.assertEqual(identity['toolchain_hash'], hashlib.sha256(lock_bytes).hexdigest())
        self.assertEqual((self.root/identity['toolchain_lock']['path']).read_bytes(), lock_bytes)
        self.assertEqual(identity['build_sha256'], doctor.digest(self.apk))
        self.assertEqual(doctor.read(self.directory/'executor-identity.json'), before)
        copied = self.directory/'resolved-candidate/inputs/source/game/Packages/packages-lock.json'
        self.assertEqual(copied.read_bytes(), (self.root/'game/Packages/packages-lock.json').read_bytes())
        bindings = doctor.read(self.directory/'resolved-candidate/bindings.json')
        self.assertEqual({row['path'] for row in bindings['resolution_changes']},
                         {'game/Packages/packages-lock.json','game/Assets/Probe.cs.meta'})
        self.assertEqual(bindings['physical_qualification'], 'not_executed')

    def test_other_task_cannot_use_probe_binding(self):
        self.context['task']['id'] = 'F03'; self.reject('restricted to F04')

    def test_incomplete_native_assertions_rejected(self):
        self.packet['case_outcomes'].pop(); self.reject('Exact four')

    def test_failed_native_assertion_rejected(self):
        self.packet['case_outcomes'][0]['status'] = 'failed'; self.reject('Malformed/nonpassing')

    def test_packet_bound_to_different_native_build_rejected(self):
        self.packet['build_binding']['build_guid'] = 'other'; self.reject('different build/lock')

    def test_observed_or_physical_lock_label_rejected(self):
        self.lock['status'] = 'physically_qualified'; self.put(self.lock_path,self.lock); self.refresh_packet()
        self.reject('resolved local candidate lock')

    def test_stale_project_hash_rejected(self):
        self.lock['inputs']['game/Packages/manifest.json']['sha256'] = '0'*64
        self.put(self.lock_path,self.lock); self.refresh_packet(); self.reject('Stale toolchain input')

    def test_native_editor_mismatch_rejected(self):
        self.report['unity_version'] = 'other'; self.put(self.report_path,self.report); self.refresh_packet()
        self.reject('Native/ProjectVersion')

    def test_editor_lock_must_match_launched_executable(self):
        self.context['launched_editor']['path'] = str(self.root/'different-editor')
        self.reject('actual launched Editor')

    def test_mutable_project_input_reference_rejected(self):
        name = 'game/Packages/manifest.json'
        self.lock['inputs'][name] = self.reference(self.root/name)
        self.put(self.lock_path,self.lock); self.refresh_packet(); self.reject('project inputs must be retained')

    def test_mutable_package_cache_reference_rejected(self):
        path = self.put('game/Library/PackageCache/invalid.synthetic/package.json', {'name':'invalid.synthetic','version':'1'})
        self.lock['packages']['invalid.synthetic']['manifest'] = self.reference(path)
        self.put(self.lock_path,self.lock); self.refresh_packet(); self.reject('not a mutable cache')

    def test_incomplete_android_tool_inventory_rejected(self):
        self.lock['android_tools'].pop('ndk'); self.put(self.lock_path,self.lock); self.refresh_packet()
        self.reject('Exact Android tool inventory')

    def test_tool_executable_hash_mismatch_rejected(self):
        self.lock['android_tools']['jdk']['executable']['sha256'] = '0'*64
        self.put(self.lock_path,self.lock); self.refresh_packet(); self.reject('tool executable hash')

    def test_package_version_mismatch_rejected(self):
        self.lock['packages']['invalid.synthetic']['version'] = '2'
        self.put(self.lock_path,self.lock); self.refresh_packet(); self.reject('package version mismatch')

    def test_changed_existing_source_rejected(self):
        self.put('game/Assets/Probe.cs', b'// Unexpected changed source\n'); self.reject('Unexpected source change')

    def test_new_unexpected_source_rejected(self):
        self.put('game/Assets/Unexpected.cs', b'// New code during build\n'); self.reject('Unexpected source change')

    def test_source_symlink_directory_rejected(self):
        (self.root/'game/Assets/link').symlink_to(self.root/'synthetic-tools',target_is_directory=True)
        self.reject('symlink')

    def test_admission_snapshot_mutation_rejected(self):
        self.put(self.directory/'inputs/source/game/Assets/Probe.cs', b'changed admission copy')
        self.reject('Admission input bytes changed')

    def test_changed_content_rejected(self):
        self.put('specification/content/mission_contracts.json', {'different_synthetic_content':True})
        self.reject('Authored content changed')

    def test_source_added_during_retention_rejected(self):
        original = candidate.snapshot_identity
        def changed(*args, **kwargs):
            identity = original(*args, **kwargs)
            self.put('game/Assets/Late.cs.meta', b'late metadata')
            return identity
        with patch.object(candidate,'snapshot_identity',side_effect=changed): self.reject('Source inventory changed')

    def test_lock_changed_during_retention_rejected(self):
        original = candidate.snapshot_identity
        def changed(*args, **kwargs):
            identity = original(*args, **kwargs)
            self.put(self.lock_path, {'status':'arbitrary_changed_lock'})
            return identity
        with patch.object(candidate,'snapshot_identity',side_effect=changed): self.reject('Toolchain lock changed')

    def test_low_space_snapshot_rejected(self):
        from runner_core import StorageBlocked
        with patch.object(candidate.shutil,'disk_usage',return_value=SimpleNamespace(free=2*1024**3)):
            before = deepcopy(self.context['identity'])
            with self.assertRaisesRegex(StorageBlocked,'2 GiB'): self.bind()
            self.assertEqual(self.context['identity'],before)
        self.assertFalse((self.directory/'resolved-candidate').exists())

    def test_scoped_retention_succeeds_below_legacy_five_gib_floor(self):
        with patch.object(candidate.shutil,'disk_usage',return_value=SimpleNamespace(free=3*1024**3)):
            self.bind()
        self.assertTrue((self.directory/'resolved-candidate/bindings.json').is_file())

    def test_capacity_drop_during_copy_stops_without_identity_acceptance(self):
        from runner_core import StorageBlocked
        before=deepcopy(self.context['identity'])
        observations=[SimpleNamespace(free=3*1024**3),SimpleNamespace(free=3*1024**3),SimpleNamespace(free=2*1024**3)]
        with patch.object(candidate.shutil,'disk_usage',side_effect=observations):
            with self.assertRaisesRegex(StorageBlocked,'copying stopped'):self.bind()
        self.assertEqual(self.context['identity'],before)
        self.assertFalse((self.directory/'resolved-candidate/bindings.json').exists())

    def test_invalid_arm64_payload_rejected(self):
        self.write_apk(header=b'NOT A REAL ARM64 ELF')
        self.report.update(total_size=self.apk.stat().st_size, apk_byte_length=self.apk.stat().st_size, apk_sha256=doctor.digest(self.apk))
        self.put(self.report_path,self.report); self.refresh_packet(); self.reject('not ARM64 ELF')

    def test_non_arm64_payload_rejected(self):
        self.write_apk(architecture='x86_64')
        self.report.update(total_size=self.apk.stat().st_size, apk_byte_length=self.apk.stat().st_size, apk_sha256=doctor.digest(self.apk))
        self.put(self.report_path,self.report); self.refresh_packet(); self.reject('ARM64 IL2CPP payload')

    def test_existing_candidate_output_never_overwritten(self):
        self.bind(); before = (self.directory/'resolved-candidate/bindings.json').read_bytes()
        with self.assertRaises((candidate.CandidateError,FileExistsError)): self.bind()
        self.assertEqual((self.directory/'resolved-candidate/bindings.json').read_bytes(),before)


if __name__ == '__main__': unittest.main()
