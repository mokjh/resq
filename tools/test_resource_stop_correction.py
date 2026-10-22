"""Prepared synthetic tests; never rewrite real RESQ receipts or execute native work."""
from copy import deepcopy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

import task_execution as te
import test_execution_round as round_fixtures


class ResourceStopCorrectionTests(unittest.TestCase):
    def setUp(self):
        self.fixture=round_fixtures.ImplementationRoundTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.root=self.fixture.root;self.run=self.fixture.run;self.task=self.fixture.task
        self.correction=deepcopy(te.C08_RESOURCE_STOP_CORRECTION)
        self.reason=self.correction['stop_reason']
        for attempt in ('attempt-01','attempt-02'):
            self.fixture.record('running',attempt);self.fixture.record('failed',attempt)
        self.fixture.record('running','attempt-03')
        documents={
            'source_manifest':{'unit_fixture':True,'files':[]},
            'wrapper_source':'Synthetic old wrapper: raises final resource guard RuntimeError.\n',
            'adapter_source':'Synthetic adapter: would classify retained stop_reason as blocked.\n',
            'editor_log':'Synthetic fixture only.\nExitCode: 0 Duration: 12s\nRESQ native tests discovered: 49\n',
            'outcome':{'status':'failed','exit_code':-15,'stop_reason':self.reason,'timed_out':False,
                'cleanup_attempted':True,'cleanup_errors':[],'execution_error':None,'launch_error':None,'output_errors':[],
                'process_group_terminal':True,'pid':202,'parent_pid':201,'pgid':202,'unit_fixture':True},
            'measurements':{'final_guard_reason':self.reason,'final_observation_error':None,
                'lowest_free_bytes':2078433280,'peak_positive_growth_bytes':1244668,'task_accepted':False,'unit_fixture':True},
            'admission':{'status':'passed','task':'C08','stage':'runtime-forecast','reserve_bytes':2**31,
                'growth':2**28,'task_accepted':False,'unit_fixture':True},
            'terminal_lock':{'terminal':True,'owned_pid':202,'owned_parent_pid':201,'owned_pgid':202,
                'holders':'','resq_native_processes':[],'unit_fixture':True},
            'check_report':{'check_id':'check-c08','status':'failed','exit_code':1,'assertions':[],'case_records':[],
                'runner':{'executed':True,'exit_code':1,'native_report':None,'executed_count':0,'failed_count':0,'required_skipped_count':0},
                'execution_context':{'task_id':'C08','run_id':self.run,'attempt_id':'attempt-03'},'unit_fixture':True},
            'terminal_observation':{'status':'failed','exit_code':1,'backend_started':True,
                'reason':'Final resource guard failed: '+self.reason,'unit_fixture':True},
        }
        for role,document in documents.items():self.replace_fixture(role,document)
        source=self.correction['references']['source_manifest']
        self.replace_fixture('result',{'task_id':'C08','status':'failed','checks':[],'output_bindings':[],
            'blockers':['Final resource guard failed: '+self.reason],'identity':{'source_manifest':dict(source)},'unit_fixture':True})
        result=self.root/self.correction['references']['result']['path']
        te.record_state('C08','failed',result,'Final resource guard failed: '+self.reason,self.run,'attempt-03')
        failed=self.correction['references']['failed_event'];failed['sha256']=te.digest(self.root/failed['path'])
        patcher=patch.object(te,'C08_RESOURCE_STOP_CORRECTION',self.correction);patcher.start();self.addCleanup(patcher.stop)
        self.protected={str(p.relative_to(self.root)):p.read_bytes() for p in (self.root/'automation/history/C08').rglob('*.json')}
        for ref in self.correction['references'].values():self.protected[ref['path']]=(self.root/ref['path']).read_bytes()

    def replace_fixture(self,role,document):
        reference=self.correction['references'][role]
        path=self.fixture.write(reference['path'],document);reference['sha256']=te.digest(path)
        return path

    def own(self):return self.fixture.own()

    def assert_protected_unchanged(self):
        for name,data in self.protected.items():self.assertEqual((self.root/name).read_bytes(),data,name)

    def test_annotation_is_required_and_never_changes_recorded_counts(self):
        before=self.own();info=te.implementation_round('C08',self.run,before)
        self.assertEqual(te._code_failed_attempts(info),3)
        self.assertNotIn('resource_stop_corrections',te.round_projection(info))
        result=te.record_c08_resource_stop_correction();after=self.own()
        self.assertEqual(result['code_failed_attempts'],2)
        for key in ('repair_attempts','execution_attempts','failed_attempts','status','result_path','result_sha256'):
            self.assertEqual(after[key],before[key],key)
        self.assertEqual(after['repair_attempts'],5);self.assertEqual(after['execution_attempts'],8);self.assertEqual(after['failed_attempts'],6)
        projection=after['implementation_rounds'][self.fixture.binding['round_id']]
        self.assertEqual(projection['repair_attempts'],2);self.assertEqual(projection['failed_attempts'],3)
        self.assertEqual(projection['code_failed_attempts'],2)
        self.assertTrue(projection['history_head']['path'].endswith('attempt-03-0017.json'))
        self.assert_protected_unchanged()
        self.fixture.admit('attempt-04');self.fixture.record('running','attempt-04')
        self.assertEqual(self.own()['repair_attempts'],5)
        self.assertEqual(self.own()['execution_attempts'],9)

    def test_same_repair_retry_then_last_code_repair_reaches_original_limit(self):
        te.record_c08_resource_stop_correction()
        self.fixture.record('running','attempt-04');self.fixture.record('failed','attempt-04')
        self.assertEqual({key:self.own()[key] for key in ('repair_attempts','execution_attempts','failed_attempts')},
                         {'repair_attempts':5,'execution_attempts':9,'failed_attempts':7})
        self.fixture.record('running','attempt-05');self.fixture.record('failed','attempt-05')
        self.assertEqual({key:self.own()[key] for key in ('repair_attempts','execution_attempts','failed_attempts')},
                         {'repair_attempts':6,'execution_attempts':10,'failed_attempts':8})
        info=te.implementation_round('C08',self.run,self.own())
        self.assertEqual(te._code_failed_attempts(info),4)
        before=self.fixture.state_bytes()
        with self.assertRaisesRegex(te.RoundBlocked,'REPAIR_BUDGET_EXHAUSTED'):self.fixture.admit('attempt-06')
        with self.assertRaisesRegex(te.RoundBlocked,'REPAIR_BUDGET_EXHAUSTED'):self.fixture.record('running','attempt-06')
        with self.assertRaises(te.RoundBlocked):te.prerequisites(self.task,'another-round','attempt-06')
        self.assertEqual(self.fixture.state_bytes(),before);self.assert_protected_unchanged()

    def test_duplicate_annotation_is_refused_without_mutation(self):
        te.record_c08_resource_stop_correction();before=self.fixture.state_bytes()
        histories={p:p.read_bytes() for p in (self.root/'automation/history/C08').rglob('*.json')}
        with self.assertRaises(te.RoundBlocked):te.record_c08_resource_stop_correction()
        self.assertEqual(self.fixture.state_bytes(),before)
        self.assertEqual({p:p.read_bytes() for p in (self.root/'automation/history/C08').rglob('*.json')},histories)

    def test_pass_after_resource_retry_keeps_raw_counters_and_the_annotation(self):
        te.record_c08_resource_stop_correction();self.fixture.record('running','attempt-04')
        result=self.fixture.write('synthetic-passed-result.json',{'prerequisite_results':[],'unit_fixture':True})
        self.fixture.record('passed','attempt-04',result)
        own=self.own();self.assertEqual(own['status'],'passed')
        self.assertEqual({key:own[key] for key in ('repair_attempts','execution_attempts','failed_attempts')},
                         {'repair_attempts':5,'execution_attempts':9,'failed_attempts':6})
        projection=own['implementation_rounds'][self.fixture.binding['round_id']]
        self.assertEqual(projection['code_failed_attempts'],2);self.assertEqual(len(projection['resource_stop_corrections']),1)
        with self.assertRaisesRegex(te.RoundBlocked,'already accepted'):self.fixture.admit('attempt-05')
        self.assert_protected_unchanged()

    def test_any_changed_evidence_bytes_refuse_annotation(self):
        before=self.fixture.state_bytes()
        for role,ref in self.correction['references'].items():
            with self.subTest(role=role):
                path=self.root/ref['path'];original=path.read_bytes();path.write_bytes(original+b' ')
                try:
                    with self.assertRaises(te.RoundBlocked):te.record_c08_resource_stop_correction()
                    self.assertEqual(self.fixture.state_bytes(),before)
                finally:path.write_bytes(original)
        self.assert_protected_unchanged()

    def test_other_process_failure_classes_never_receive_credit(self):
        changes=[('outcome','stop_reason','another stop'),('outcome','stop_reason',None),('outcome','exit_code',1),
            ('outcome','timed_out',True),('outcome','cleanup_errors',['synthetic cleanup error']),
            ('outcome','execution_error','synthetic execution error'),('outcome','launch_error','synthetic launch error'),
            ('outcome','output_errors',['synthetic output error']),('outcome','process_group_terminal',False),
            ('measurements','final_observation_error','synthetic observation error'),
            ('measurements','lowest_free_bytes',3*2**30),('terminal_lock','terminal',False)]
        before=self.fixture.state_bytes()
        for role,key,value in changes:
            with self.subTest(role=role,key=key):
                ref=self.correction['references'][role];old_hash=ref['sha256'];path=self.root/ref['path'];original=path.read_bytes()
                changed=json.loads(original);changed[key]=value;self.replace_fixture(role,changed)
                try:
                    with self.assertRaises(te.RoundBlocked):te.record_c08_resource_stop_correction()
                    self.assertEqual(self.fixture.state_bytes(),before)
                finally:path.write_bytes(original);ref['sha256']=old_hash
        self.assert_protected_unchanged()

    def test_missing_null_failure_field_is_not_treated_as_success(self):
        ref=self.correction['references']['outcome'];path=self.root/ref['path'];document=json.loads(path.read_text())
        del document['execution_error'];self.replace_fixture('outcome',document)
        with self.assertRaises(te.RoundBlocked):te.record_c08_resource_stop_correction()

    def test_native_output_or_dangling_symlink_refuses_annotation(self):
        before=self.fixture.state_bytes()
        for name in self.correction['absent_native_outputs']:
            for symlink in (False,True):
                with self.subTest(path=name,symlink=symlink):
                    path=self.root/name;path.parent.mkdir(parents=True,exist_ok=True)
                    if symlink:path.symlink_to(self.root/'missing-native-fixture')
                    else:path.write_text('{}')
                    try:
                        with self.assertRaises(te.RoundBlocked):te.record_c08_resource_stop_correction()
                        self.assertEqual(self.fixture.state_bytes(),before)
                    finally:path.unlink()

    def test_projection_cannot_claim_credit_without_an_annotation(self):
        state=json.loads(self.fixture.state_bytes());projection=state['task_states']['C08']['implementation_rounds'][self.fixture.binding['round_id']]
        projection['resource_stop_corrections']=[self.correction];projection['code_failed_attempts']=2
        self.fixture.write('automation/task_state.json',state)
        with self.assertRaisesRegex(te.RoundBlocked,'projection differs'):self.fixture.admit('attempt-04')

    def test_correction_refuses_a_changed_head_or_different_authorization(self):
        for key in ('task_id','run_id','round_id'):
            original=self.correction[key];self.correction[key]='unapproved'
            try:
                with self.assertRaisesRegex(te.RoundBlocked,'different authorization'):te.record_c08_resource_stop_correction()
            finally:self.correction[key]=original
        self.fixture.record('blocked','later-preflight')
        with self.assertRaisesRegex(te.RoundBlocked,'exact original history head'):te.record_c08_resource_stop_correction()

    def test_annotation_tamper_and_late_native_output_block_future_admission(self):
        te.record_c08_resource_stop_correction()
        projection=self.own()['implementation_rounds'][self.fixture.binding['round_id']]
        path=self.root/projection['history_head']['path'];original=path.read_bytes();event=json.loads(original)
        event['resource_stop_correction']['id']='forged-credit';path.write_text(json.dumps(event))
        with self.assertRaisesRegex(te.RoundBlocked,'unknown resource correction'):self.fixture.admit('attempt-04')
        path.write_bytes(original)
        self.fixture.write(self.correction['absent_native_outputs'][0],'<test-run result="Failed"/>')
        with self.assertRaisesRegex(te.RoundBlocked,'present or escaped native output'):self.fixture.admit('attempt-04')


if __name__=='__main__':unittest.main()
