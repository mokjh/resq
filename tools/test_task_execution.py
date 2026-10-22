"""Real dispatcher/history regression checks; temp fixtures never count as game evidence."""
import copy
import json
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
import task_execution as te
from bootstrap_doctor import write_once


class ControlTests(unittest.TestCase):
    def candidate_binding_fixture(self,root):
        # Minimal unit fixture assumes prerequisite validation already happened.
        # It is deliberately not a valid task result and cannot accept F04/F05.
        identity={'build_sha256':'a'*64,'source_identity':'sha256:'+'b'*64,
                  'content_hash':'c'*64,'toolchain_hash':'d'*64,'fixture':'synthetic candidate only'}
        path=root/'evidence/synthetic-f04.json';path.parent.mkdir(parents=True)
        path.write_text(json.dumps({'task_id':'F04','status':'passed','identity':identity}))
        reference={'task_id':'F04','result_path':str(path.relative_to(root)),'result_sha256':te.digest(path)}
        (root/'automation').mkdir()
        (root/'automation/task_state.json').write_text(json.dumps({'task_states':{'F04':dict(reference,status='passed')}}))
        context={'task':{'id':'F05'},'prerequisites':[reference],'identity':{'fixture':'executor source only','build_sha256':None},'run_dir':root/'evidence/owned-f05'}
        packet={'candidate_identity':copy.deepcopy(identity),'case_outcomes':[{'observations':{'fixture':'synthetic session details'}}]}
        return context,packet,path

    def test_f05_uses_exact_accepted_candidate_and_retains_executor_identity(self):
        import resq
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();context,packet,path=self.candidate_binding_fixture(root)
            alias=context['identity'];before=copy.deepcopy(alias)
            with patch.object(resq,'ROOT',root),patch('bootstrap_doctor.ROOT',root):
                resq.bind_backend_candidate(packet,context)
            self.assertIs(alias,context['identity'])
            self.assertEqual(alias,packet['candidate_identity'])
            self.assertEqual(json.loads((context['run_dir']/'executor-identity.json').read_text()),before)
            self.assertEqual(packet['case_outcomes'][0]['observations']['executor_identity']['path'],
                             'evidence/owned-f05/executor-identity.json')

    def test_f05_rejects_missing_or_changed_candidate_without_overwriting_identity(self):
        import resq
        for field in (None,'build_sha256','toolchain_hash','source_identity'):
            with self.subTest(field=field),tempfile.TemporaryDirectory() as directory:
                root=Path(directory).resolve();context,packet,path=self.candidate_binding_fixture(root)
                before=copy.deepcopy(context['identity'])
                if field is None:packet.pop('candidate_identity')
                else:packet['candidate_identity'][field]='different candidate'
                with patch.object(resq,'ROOT',root),self.assertRaisesRegex(RuntimeError,'F05_CANDIDATE'):
                    resq.bind_backend_candidate(packet,context)
                self.assertEqual(context['identity'],before)
                self.assertFalse((context['run_dir']/'executor-identity.json').exists())

    def test_backend_cannot_replace_other_tasks_candidate_identity(self):
        import resq
        with self.assertRaisesRegex(RuntimeError,'CANDIDATE_OVERRIDE_NOT_ALLOWED'):
            resq.bind_backend_candidate({'candidate_identity':{'fixture':True}},{'task':{'id':'F06'}})

    def test_f05_rejects_head_or_immutable_result_changed_after_admission(self):
        import resq
        for mutation in ('head','bytes'):
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as directory:
                root=Path(directory).resolve();context,packet,path=self.candidate_binding_fixture(root)
                if mutation=='bytes':path.write_text(path.read_text()+'\n')
                else:
                    state=json.loads((root/'automation/task_state.json').read_text());state['task_states']['F04']['result_sha256']='e'*64
                    (root/'automation/task_state.json').write_text(json.dumps(state))
                with patch.object(resq,'ROOT',root),self.assertRaisesRegex(RuntimeError,'F05_F04_'):
                    resq.bind_backend_candidate(packet,context)

    def test_backend_exit75_blocks_without_promoting_native_cases(self):
        import resq
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();context={'run_dir':root/'owned-run','owner':{'test_fixture':True}}
            process={'status':'failed','exit_code':75,'timed_out':False,'launch_error':None,'output_errors':[],'stop_reason':None}
            with patch.object(te,'require_backend',return_value=root/'synthetic-backend.py'),patch('runner_core.run_process',return_value=process):
                with self.assertRaisesRegex(te.Blocked,'BACKEND_BLOCKED'):
                    resq.dispatch_backend('art',['art','audit','--kit','calibration'],context)
            self.assertTrue(context['backend_started'])
            self.assertFalse((context['run_dir']/'backend-cases.json').exists())

    def test_backend_execution_failure_is_not_a_preflight_block(self):
        import resq
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();context={'run_dir':root/'owned-run','owner':{'test_fixture':True}}
            process={'status':'failed','exit_code':1}
            with patch.object(te,'require_backend',return_value=root/'synthetic-backend.py'),patch('runner_core.run_process',return_value=process):
                with self.assertRaisesRegex(RuntimeError,'BACKEND_FAILED'):
                    resq.dispatch_backend('art',['art','audit','--kit','calibration'],context)

    def test_doctor_reserve_blocks_before_native_test_launch(self):
        import adapter_doctors as adapter
        from types import SimpleNamespace
        with tempfile.TemporaryDirectory() as directory:
            context={'run_dir':Path(directory)/'owned','owner':{'synthetic_fixture':True}}
            with patch.object(adapter.shutil,'disk_usage',return_value=SimpleNamespace(free=4*1024**3)),patch.object(adapter,'run_process') as process:
                with self.assertRaisesRegex(te.Blocked,'STORAGE_BELOW_RESERVE'):
                    adapter.run_suite('test_synthetic.py','fixture',context)
                process.assert_not_called()
            self.assertFalse(context.get('backend_started',False))

    def test_doctor_resource_cancel_is_named_block_without_native_success(self):
        import adapter_doctors as adapter
        from types import SimpleNamespace
        with tempfile.TemporaryDirectory() as directory:
            context={'run_dir':Path(directory)/'owned','owner':{'synthetic_fixture':True}}
            outcome={'status':'failed','exit_code':-15,'stop_reason':'output volume below 5 GiB reserve'}
            with patch.object(adapter.shutil,'disk_usage',return_value=SimpleNamespace(free=10*1024**3)),patch.object(adapter,'run_process',return_value=outcome):
                with self.assertRaisesRegex(te.Blocked,'STORAGE_CANCELLED'):
                    adapter.run_suite('test_synthetic.py','fixture',context)
            self.assertTrue(context['backend_started'])
            self.assertFalse((context['run_dir']/'fixture/native.json').exists())

    def test_doctor_missing_native_report_after_execution_failure_is_not_storage_block(self):
        import adapter_doctors as adapter
        from types import SimpleNamespace
        with tempfile.TemporaryDirectory() as directory:
            context={'run_dir':Path(directory)/'owned','owner':{'synthetic_fixture':True}}
            outcome={'status':'failed','exit_code':1,'stop_reason':None}
            with patch.object(adapter.shutil,'disk_usage',return_value=SimpleNamespace(free=10*1024**3)),patch.object(adapter,'run_process',return_value=outcome):
                with self.assertRaisesRegex(RuntimeError,'emitted no report') as result:
                    adapter.run_suite('test_synthetic.py','fixture',context)
            self.assertNotIsInstance(result.exception,te.Blocked)

    def unity_preflight_fixture(self,root):
        version=root/'game/ProjectSettings/ProjectVersion.txt'
        version.parent.mkdir(parents=True)
        version.write_text('m_EditorVersion: not-installed-fixture\n')
        estimates=root/'automation/resource_estimates.json'
        estimates.parent.mkdir(parents=True)
        estimates.write_text(json.dumps({'F04':{'peak_bytes':2*2**30}}))
        return {'task':{'id':'F04'},'run_dir':root/'evidence/test-owned-run','owner':{'test_fixture':True}}

    def test_unity_live_storage_block_never_launches_editor(self):
        import adapter_unity as au
        import run_unity_bounded as bounded
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();context=self.unity_preflight_fixture(root)
            observation={'status':'blocked','required_free_bytes':int(4.25*2**30),'volumes':[{'path':str(root),'free_bytes':3*2**30}]}
            with patch.object(au,'ROOT',root),patch.object(bounded,'preflight',return_value=observation),patch.object(au,'run_process') as launched:
                with self.assertRaisesRegex(te.Blocked,'WORKING_STORAGE_BELOW_FLOOR'):
                    au.execute(['game','build','--profile','xr-probe'],context)
                launched.assert_not_called()
            self.assertEqual(json.loads((context['run_dir']/'operation-storage.json').read_text()),observation)
            self.assertNotIn('backend_started',context)

    def test_unity_live_storage_observation_retained_when_later_setup_blocks(self):
        import adapter_unity as au
        import run_unity_bounded as bounded
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();context=self.unity_preflight_fixture(root)
            observation={'status':'passed','required_free_bytes':int(4.25*2**30),'volumes':[{'path':str(root),'free_bytes':5*2**30}]}
            with patch.object(au,'ROOT',root),patch.object(bounded,'preflight',return_value=observation),patch.object(au,'run_process') as launched:
                with self.assertRaisesRegex(te.Blocked,'EXACT_EDITOR_NOT_INSTALLED'):
                    au.execute(['game','build','--profile','xr-probe'],context)
                launched.assert_not_called()
            self.assertEqual(json.loads((context['run_dir']/'operation-storage.json').read_text()),observation)

    def host_profile_fixture(self,root):
        profile=json.loads((Path(__file__).resolve().parents[1]/'automation/resource_profiles.json').read_text())
        (root/'automation').mkdir();(root/'automation/resource_profiles.json').write_text(json.dumps(profile))
        project=root/'game';(project/'Packages').mkdir(parents=True);(project/'Assets').mkdir()
        (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':profile['profiles'][0]['exact_packages']}))
        return project

    def test_f03_scoped_profile_enforces_hard_reserve_and_bound(self):
        import run_f03_bounded as host
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);project=self.host_profile_fixture(root)
            required=host.RESERVE+host.LIMIT+host.BUFFER
            for free,status in [(required-1,'blocked'),(required,'passed')]:
                usage=type('Usage',(),{'free':free})()
                with patch.object(host,'ROOT',root),patch.object(host.shutil,'disk_usage',return_value=usage):
                    data=host.preflight(project,host.LIMIT)
                self.assertEqual(data['status'],status)
                self.assertFalse(data['general_Unity_Android_render_gate_passed'])

    def test_f03_scoped_profile_refuses_large_package_and_unknown_estimate(self):
        import run_f03_bounded as host
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);project=self.host_profile_fixture(root)
            with patch.object(host,'ROOT',root),self.assertRaisesRegex(ValueError,'explicit'):
                host.preflight(project,None)
            manifest=project/'Packages/manifest.json';data=json.loads(manifest.read_text());data['dependencies']['com.unity.burst']='1.8.30';manifest.write_text(json.dumps(data))
            with patch.object(host,'ROOT',root),self.assertRaisesRegex(ValueError,'Non-host package'):
                host.preflight(project,host.LIMIT)

    def test_apfs_source_snapshot_is_independent_and_refuses_overwrite(self):
        from bootstrap_doctor import clone_source_snapshot
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);source=root/'source';destination=root/'snapshot';source.write_bytes(b'original')
            clone_source_snapshot(source,destination)
            self.assertNotEqual(source.stat().st_ino,destination.stat().st_ino)
            source.write_bytes(b'changed');self.assertEqual(destination.read_bytes(),b'original')
            with self.assertRaises(FileExistsError):clone_source_snapshot(source,destination)

    def test_catalog_registered_commands_resolve(self):
        commands=[c for c in te.check_registry().values() if c['task_id']!='F01']
        self.assertEqual(len(commands),230)
        for c in commands:
            self.assertEqual(te.bound_check(c['command'][2:-2])['id'],c['id'])
            self.assertIn(c['command'][2],te.FAMILIES)
            self.assertTrue(callable(__import__('resq').dispatch_backend))

    def test_command_rejects_unregistered_extra_flags(self):
        with self.assertRaises(te.Blocked):te.bound_check(['doctor','--scope','runner','--pretend-passed'])

    def test_source_snapshot_binds_nested_unity_settings_and_binary_assets(self):
        import bootstrap_doctor as bd
        with tempfile.TemporaryDirectory() as directory,patch.object(te,'ROOT',Path(directory)),patch.object(bd,'ROOT',Path(directory)):
            root=Path(directory)
            paths={'tools/art/source.py':b'# original source\n','tools/HostCoreHarness.cs':b'// C#\n',
                'game/Assets/Core.asmdef':b'{}','game/Assets/Core.cs.meta':b'guid: native-future-guid\n',
                'game/Assets/unit.fbx':b'\x00\xff\x80native-binary','game/Packages/manifest.json':b'{}',
                'game/ProjectSettings/ProjectVersion.txt':b'candidate-version',
                'automation/tasks.json':b'{}','automation/checks.json':b'{}','automation/gates.json':b'{}',
                'automation/local_operation_profiles.json':b'{}',
                'content/runtime/fixtures/compiler-valid.json':b'{"synthetic":true}',
                'content/runtime/schemas/ContentBundle.schema.json':b'{"type":"object"}',
                'specification/content/acceptance_cases.json':b'{}','specification/content/mission_contracts.json':b'{}',
                'game/Library/ignored.cs':b'cache','tools/__pycache__/ignored.py':b'cache'}
            for name,data in paths.items():
                p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
            selected=te.source_files()
            self.assertIn('game/Assets/unit.fbx',selected)
            self.assertIn('game/Assets/Core.cs.meta',selected)
            self.assertIn('game/Packages/manifest.json',selected)
            self.assertIn('game/ProjectSettings/ProjectVersion.txt',selected)
            self.assertIn('tools/art/source.py',selected)
            self.assertIn('content/runtime/fixtures/compiler-valid.json',selected)
            self.assertIn('content/runtime/schemas/ContentBundle.schema.json',selected)
            self.assertIn('automation/local_operation_profiles.json',selected)
            self.assertNotIn('game/Library/ignored.cs',selected)
            self.assertNotIn('tools/__pycache__/ignored.py',selected)
            bd.snapshot_identity(root/'snapshot',selected,{'test_fixture':True})
            self.assertEqual((root/'snapshot/inputs/source/game/Assets/unit.fbx').read_bytes(),paths['game/Assets/unit.fbx'])
            self.assertEqual((root/'snapshot/inputs/source/content/runtime/fixtures/compiler-valid.json').read_bytes(),paths['content/runtime/fixtures/compiler-valid.json'])

    def test_source_snapshot_refuses_symlinked_input(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(te,'ROOT',Path(directory)):
            root=Path(directory);asset=root/'game/Assets';asset.mkdir(parents=True)
            target=root/'external.txt';target.write_text('not a regular owned input')
            (asset/'linked.asset').symlink_to(target)
            with self.assertRaisesRegex(te.Invalid,'symlink'):te.source_files()

    def test_source_snapshot_refuses_symlinked_directory(self):
        for top in (False,True):
            with self.subTest(top=top),tempfile.TemporaryDirectory() as directory,patch.object(te,'ROOT',Path(directory)):
                root=Path(directory);assets=root/'game/Assets';assets.parent.mkdir(parents=True)
                outside=root/'outside';outside.mkdir();(outside/'hidden.cs').write_text('synthetic fixture only')
                if top:assets.symlink_to(outside,target_is_directory=True)
                else:assets.mkdir();(assets/'linked').symlink_to(outside,target_is_directory=True)
                with self.assertRaisesRegex(te.Invalid,'symlink'):te.source_files()

    def test_unknown_family_cannot_pass(self):
        with self.assertRaises(te.Blocked):te.backend_for('imaginary')

    def test_missing_backend_is_explicit_block(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            with self.assertRaisesRegex(te.Blocked,'BACKEND_NOT_IMPLEMENTED'):te.require_backend('content')

    def fixture(self,root,task_id='F02'):
        (root/'automation').mkdir()
        state={'task_states':{task_id:{'status':'pending','result_path':None,'result_sha256':None,'repair_attempts':0}}}
        (root/'automation/task_state.json').write_text(json.dumps(state))
        return state

    def test_history_chain_follows_sequence_not_run_name(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);self.fixture(root)
            te.record_state('F02','running',None,'test fixture','z-run','first')
            te.record_state('F02','blocked',None,'test fixture','a-run','second')
            te.record_state('F02','running',None,'test fixture','m-run','third')
            events=sorted((root/'automation/history/F02').rglob('*.json'),key=lambda p:json.loads(p.read_text())['sequence'])
            self.assertEqual([json.loads(p.read_text())['sequence'] for p in events],[1,2,3])
            self.assertEqual(json.loads(events[2].read_text())['previous_event_sha256'],te.digest(events[1]))

    def test_failed_attempts_survive_new_run_ids(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);self.fixture(root)
            for n in range(4):te.record_state('F02','failed',None,'fixture defect',f'run-{n}','attempt')
            task={'id':'F02','workstream':'foundation','max_repair_attempts':3,'depends_on':[],'required_gates':[]}
            with self.assertRaisesRegex(te.Blocked,'REPAIR_BUDGET_EXHAUSTED'):te.prerequisites(task)

    def test_block_before_launch_does_not_consume_repair(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);self.fixture(root);te.record_state('F02','blocked',None,'fixture unavailable','one','attempt')
            state=json.loads((root/'automation/task_state.json').read_text())['task_states']['F02']
            self.assertEqual(state.get('failed_attempts',0),0)

    def test_blocked_recheck_preserves_accepted_projection_and_history(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);state=self.fixture(root);state['task_states']['F02']['status']='passed'
            (root/'automation/task_state.json').write_text(json.dumps(state))
            te.record_state('F02','blocked',None,'fixture recheck','recheck','attempt')
            self.assertEqual(json.loads((root/'automation/task_state.json').read_text())['task_states']['F02']['status'],'passed')
            self.assertEqual(len(list((root/'automation/history/F02').rglob('*.json'))),1)

    def test_failed_rechecks_preserve_passed_head_but_consume_budget(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);state=self.fixture(root);state['task_states']['F02'].update(status='passed',result_path='original.json',result_sha256='a'*64)
            (root/'automation/task_state.json').write_text(json.dumps(state))
            for n in range(4):te.record_state('F02','failed',None,'fixture recheck failure',f'run-{n}','attempt')
            actual=json.loads((root/'automation/task_state.json').read_text())['task_states']['F02']
            self.assertEqual(actual['status'],'passed');self.assertEqual(actual['result_path'],'original.json')
            task={'id':'F02','workstream':'foundation','max_repair_attempts':3,'depends_on':[],'required_gates':[]}
            with self.assertRaisesRegex(te.Blocked,'REPAIR_BUDGET_EXHAUSTED'):te.prerequisites(task)

    def test_passed_head_replacement_invalidates_transitive_dependents(self):
        with tempfile.TemporaryDirectory() as d,patch.object(te,'ROOT',Path(d)):
            root=Path(d);self.fixture(root,'A')
            graph={'A':{'depends_on':[]},'B':{'depends_on':['A']},'C':{'depends_on':['B']}}
            state={'task_states':{}}
            for tid,parent in [('A',None),('B','A'),('C','B')]:
                refs=[] if parent is None else [{'task_id':parent,'result_path':parent+'.json','result_sha256':te.digest(root/(parent+'.json'))}]
                (root/(tid+'.json')).write_text(json.dumps({'prerequisite_results':refs}))
                state['task_states'][tid]={'status':'passed','result_path':tid+'.json','result_sha256':te.digest(root/(tid+'.json')),'repair_attempts':0}
            old_b=(root/'B.json').read_bytes();(root/'automation/task_state.json').write_text(json.dumps(state))
            new=root/'A-new.json';new.write_text('{"prerequisite_results":[],"new_head":true}')
            with patch.object(te,'task_registry',lambda:graph):te.record_state('A','passed',new,'fixture replacement','fresh','attempt')
            actual=json.loads((root/'automation/task_state.json').read_text())['task_states']
            self.assertEqual(actual['A']['status'],'passed')
            for tid in ['B','C']:
                self.assertEqual(actual[tid]['status'],'pending');self.assertIsNone(actual[tid]['result_path'])
                self.assertEqual(len(list((root/'automation/history'/tid).rglob('*.json'))),1)
            self.assertEqual((root/'B.json').read_bytes(),old_b)

    def test_atomic_immutable_write_never_replaces_evidence(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'evidence.json';write_once(p,{'actual':'first'})
            with self.assertRaises(FileExistsError):write_once(p,{'actual':'replacement'})
            self.assertEqual(json.loads(p.read_text()),{'actual':'first'})
            self.assertEqual(list(Path(d).glob('*.tmp')),[])

    def test_nonpassed_result_cannot_be_accepted(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'blocked.json';p.write_text(json.dumps({'status':'blocked'}))
            with patch.object(te,'validate',lambda root:None):
                with self.assertRaisesRegex(te.Blocked,'ONLY_PASSED'):te.accept_result(p)

    def test_additional_registered_check_has_its_own_context(self):
        from bootstrap_doctor import report_context
        result=report_context('evidence/checks/runtime-units/test-context-only/no-write.json','C06',check_id='check-runtime-units')
        self.assertEqual(result[-1]['id'],'check-runtime-units')

    def test_aggregate_rejects_missing_or_mixed_required_checks(self):
        base={'task_id':'fixture','identity':{'fixture':1},'output_bindings':[],'artifacts':[],'prerequisite_results':[],
          'gate_results':[],'proof_records':[],'checks':[{'check_id':'one'}],'status':'passed','blockers':[]}
        task={'acceptance_checks':['one','two']}
        with self.assertRaises(te.Invalid):te.combine_check_results([base],task)
        other=copy.deepcopy(base);other['checks']=[{'check_id':'two'}]
        self.assertEqual(te.combine_check_results([base,other],task)['status'],'passed')
        other['identity']['fixture']=2
        with self.assertRaises(te.Invalid):te.combine_check_results([base,other],task)


if __name__=='__main__':unittest.main(verbosity=2)
