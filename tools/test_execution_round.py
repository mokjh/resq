"""Synthetic admission tests only; these cannot accept C08 or prove native performance."""
from contextlib import redirect_stdout
from copy import deepcopy
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import task_execution as te


class ImplementationRoundTests(unittest.TestCase):
    def setUp(self):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        self.root=Path(temporary.name).resolve()
        self.binding=deepcopy(te.C08_IMPLEMENTATION_ROUND)
        self.run=self.binding['run_id']
        self.task={'id':'C08','workstream':'gameplay','max_repair_attempts':3,'depends_on':[],
                   'required_gates':[],'acceptance_checks':['check-c08','check-runtime-ordering']}
        self.write('automation/tasks.json',{'tasks':[self.task]})
        self.write('automation/gates.json',{'gates':[]})
        previous=None
        events=[('running','attempt-01'),('failed','attempt-01'),('running','attempt-02'),
                ('blocked','attempt-02'),('running','attempt-03'),('failed','attempt-03'),
                ('running','attempt-04'),('failed','attempt-04'),('running','attempt-05'),('blocked','attempt-05')]
        self.historical={}
        for sequence,(status,attempt) in enumerate(events,1):
            run='performance-review-20261017' if sequence==10 else 'bounded-local-20261017'
            name=f'automation/history/C08/{run}/{attempt}-{sequence:04}.json'
            data={'schema_version':1,'task_id':'C08','event':status,'run_id':run,'attempt_id':attempt,
                  'sequence':sequence,'previous_event_sha256':previous,'result_path':None,'result_sha256':None,
                  'reason':'synthetic historical fixture; no native evidence','verifier':'unit_fixture',
                  'recorded_utc':'2026-10-17T20:00:00+00:00'}
            path=self.write(name,data);previous=te.digest(path);self.historical[name]=path.read_bytes()
        self.binding['history_anchor']['sha256']=previous
        proposal=self.write(self.binding['proposal']['path'],'Synthetic proposal only: retain the whole-call 1 ms budget.\n')
        self.binding['proposal']['sha256']=te.digest(proposal)
        authorization={'kind':'human_authorized_new_implementation_round','round_id':self.binding['round_id'],
                       'proposal':self.binding['proposal'],'history_reset':False,
                       'historical_task_counters':self.binding['historical_task_counters'],
                       'new_round':{'initial_implementation_authorized':True,'repair_limit':3,'repair_attempts':0},
                       'unit_fixture':True}
        receipt=self.write(self.binding['authorization']['path'],authorization)
        self.binding['authorization']['sha256']=te.digest(receipt)
        self.write('automation/task_state.json',{'task_states':{'C08':{
            'status':'blocked','result_path':None,'result_sha256':None,**self.binding['historical_task_counters']}}})
        for target,value in [('ROOT',self.root),('C08_IMPLEMENTATION_ROUND',self.binding)]:
            patcher=patch.object(te,target,value);patcher.start();self.addCleanup(patcher.stop)

    def write(self,name,value):
        path=self.root/name;path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(value if isinstance(value,str) else json.dumps(value,indent=2)+'\n')
        return path

    def own(self):
        return json.loads((self.root/'automation/task_state.json').read_text())['task_states']['C08']

    def state_bytes(self):
        return (self.root/'automation/task_state.json').read_bytes()

    def assert_historical_unchanged(self):
        for name,data in self.historical.items():self.assertEqual((self.root/name).read_bytes(),data,name)

    def admit(self,attempt='attempt-01'):
        return te.prerequisites(self.task,self.run,attempt)

    def record(self,status,attempt,result=None):
        te.record_state('C08',status,result,'synthetic round fixture',self.run,attempt)

    def test_exact_authorized_round_admits_without_resetting_or_writing(self):
        before=self.state_bytes()
        self.assertEqual(self.admit(),([],[]))
        self.assertEqual(self.state_bytes(),before)
        info=te.implementation_round('C08',self.run,self.own())
        self.assertEqual(info['counts'],{'repair_attempts':0,'execution_attempts':0,'failed_attempts':0})
        self.assertEqual(len(info['source_paths']),12)
        self.assert_historical_unchanged()

    def test_other_run_ids_cannot_open_or_record_the_round(self):
        before=self.state_bytes()
        for run in (None,'bounded-local-20261017','resumable-20261019'):
            with self.subTest(run=run),self.assertRaisesRegex(te.RoundBlocked,'exact authorized run ID'):
                te.prerequisites(self.task,run,'attempt-01')
            with self.subTest(record_run=run),self.assertRaises(te.RoundBlocked):
                te.record_state('C08','running',None,'wrong run fixture',run,'attempt-01')
        self.assertEqual(self.state_bytes(),before);self.assert_historical_unchanged()

    def test_reserved_run_never_extends_another_tasks_budget(self):
        with self.assertRaisesRegex(te.RoundBlocked,'belongs only to C08'):
            te.implementation_round('F06',self.run,{})
        state=json.loads(self.state_bytes());state['task_states']['F02']={'status':'failed','failed_attempts':4,'repair_attempts':3}
        self.write('automation/task_state.json',state)
        task=dict(self.task,id='F02')
        with self.assertRaisesRegex(te.Blocked,'REPAIR_BUDGET_EXHAUSTED'):
            te.prerequisites(task,'unrelated-run','attempt-01')

    def test_receipt_proposal_anchor_and_prefix_tamper_fail_closed(self):
        before=self.state_bytes()
        names=[self.binding[key]['path'] for key in ('authorization','proposal','history_anchor')]
        names.append(next(iter(self.historical)))
        for name in names:
            with self.subTest(path=name):
                path=self.root/name;original=path.read_bytes();path.write_bytes(original+b' ')
                try:
                    with self.assertRaises(te.RoundBlocked):self.admit()
                    self.assertEqual(self.state_bytes(),before)
                finally:path.write_bytes(original)

    def test_missing_receipt_and_symlinked_receipt_fail_closed(self):
        path=self.root/self.binding['authorization']['path'];original=path.read_bytes();path.unlink()
        with self.assertRaises(te.RoundBlocked):self.admit()
        target=self.root/'fixture-receipt.json';target.write_bytes(original);path.symlink_to(target)
        with self.assertRaisesRegex(te.RoundBlocked,'symlinked'):self.admit()

    def test_four_failed_attempts_exhaust_only_the_new_round(self):
        for number in range(1,5):
            attempt=f'attempt-{number:02}'
            self.admit(attempt);self.record('running',attempt)
            self.assertEqual(self.own()['repair_attempts'],3+number-1)
            self.record('failed',attempt)
            self.assertEqual(self.own()['failed_attempts'],3+number)
            self.assertEqual(self.own()['execution_attempts'],5+number)
        before=self.state_bytes()
        with self.assertRaisesRegex(te.RoundBlocked,'REPAIR_BUDGET_EXHAUSTED'):self.admit('attempt-05')
        with self.assertRaisesRegex(te.RoundBlocked,'REPAIR_BUDGET_EXHAUSTED'):self.record('running','attempt-05')
        self.assertEqual(self.state_bytes(),before)
        projection=self.own()['implementation_rounds'][self.binding['round_id']]
        self.assertEqual(projection['failed_attempts'],4);self.assertEqual(projection['repair_attempts'],3)
        self.assert_historical_unchanged()

    def test_storage_blocks_keep_the_allowance_and_started_execution_count(self):
        self.record('blocked','preflight-block')
        self.assertEqual({k:self.own()[k] for k in self.binding['historical_task_counters']},self.binding['historical_task_counters'])
        self.admit('launch-block');self.record('running','launch-block');self.record('blocked','launch-block')
        self.assertEqual(self.own()['execution_attempts'],6)
        self.assertEqual(self.own()['failed_attempts'],3);self.assertEqual(self.own()['repair_attempts'],3)
        self.assertEqual(self.admit('next-attempt'),([],[]))
        self.assert_historical_unchanged()

    def test_unterminated_duplicate_and_unstarted_attempts_cannot_spend_budget(self):
        with self.assertRaisesRegex(te.RoundBlocked,'no matching running'):self.record('failed','unstarted')
        self.record('running','attempt-01')
        with self.assertRaisesRegex(te.RoundBlocked,'no terminal observation'):self.admit('attempt-02')
        self.record('failed','attempt-01');before=self.state_bytes()
        with self.assertRaises(te.RoundBlocked):self.record('failed','attempt-01')
        with self.assertRaisesRegex(te.RoundBlocked,'already been used'):self.admit('attempt-01')
        self.assertEqual(self.state_bytes(),before)

    def test_pass_preserves_every_historical_and_round_counter_and_closes_round(self):
        self.record('running','attempt-01');self.record('failed','attempt-01')
        self.record('running','attempt-02')
        result=self.write('synthetic-result.json',{'prerequisite_results':[],'unit_fixture':True})
        self.record('passed','attempt-02',result)
        own=self.own()
        self.assertEqual(own['status'],'passed')
        self.assertEqual({k:own[k] for k in self.binding['historical_task_counters']},
                         {'repair_attempts':4,'execution_attempts':7,'failed_attempts':4})
        with self.assertRaisesRegex(te.RoundBlocked,'already accepted'):self.admit('attempt-03')
        self.assert_historical_unchanged()

    def test_last_history_event_or_projection_counter_tamper_is_rejected(self):
        self.record('running','attempt-01');self.record('failed','attempt-01')
        head=self.own()['implementation_rounds'][self.binding['round_id']]['history_head']
        path=self.root/head['path'];original=path.read_bytes();path.write_bytes(original+b' ')
        with self.assertRaisesRegex(te.RoundBlocked,'projection differs'):self.admit('attempt-02')
        path.write_bytes(original)
        state=json.loads(self.state_bytes());state['task_states']['C08']['failed_attempts']=3
        self.write('automation/task_state.json',state)
        with self.assertRaisesRegex(te.RoundBlocked,'cumulative task counters'):self.admit('attempt-02')

    def test_cross_run_or_unbound_post_anchor_event_is_rejected(self):
        te.append_history('C08','blocked',None,'synthetic foreign observation','another-run','attempt-01')
        with self.assertRaisesRegex(te.RoundBlocked,'post-anchor event'):self.admit()

    def test_acceptance_rejects_old_run_before_reconciliation_or_validation(self):
        report=self.write('synthetic-check.json',{'execution_context':{'run_id':'bounded-local-20261017','attempt_id':'attempt-05'}})
        result=self.write('synthetic-result.json',{'status':'passed','task_id':'C08',
            'checks':[{'report_path':str(report.relative_to(self.root))}],'unit_fixture':True})
        before=self.state_bytes()
        with patch.object(te,'reconcile_dependency_heads') as reconcile,patch.object(te,'validate_task_result') as validate:
            with self.assertRaisesRegex(te.RoundBlocked,'exact authorized run ID'):te.accept_result(result)
        reconcile.assert_not_called();validate.assert_not_called()
        self.assertEqual(self.state_bytes(),before);self.assert_historical_unchanged()

    def test_dispatcher_does_not_launch_or_mutate_history_for_wrong_round(self):
        import resq
        report=self.root/'evidence/checks/c08/wrong-run/attempt-01.json'
        run_dir=self.root/'evidence/runs/C08/wrong-run/attempt-01'
        before=self.state_bytes();output=io.StringIO()
        with patch.object(resq,'ROOT',self.root),patch.object(resq,'bound_check',return_value={'task_id':'C08','id':'check-c08'}), \
                patch.object(resq,'relative',side_effect=lambda path:str(Path(path).resolve().relative_to(self.root))), \
                patch.object(resq,'report_context',return_value=(report,run_dir,'wrong-run','attempt-01',None,None)), \
                patch.object(resq,'dispatch_backend') as dispatch,patch.object(resq,'terminal_result') as terminal, \
                patch.object(resq,'snapshot_identity') as snapshot,redirect_stdout(output):
            code=resq.main(['game','test','--suite','runtime-forecast','--report',str(report.relative_to(self.root))])
        self.assertEqual(code,75);dispatch.assert_not_called();terminal.assert_not_called();snapshot.assert_not_called()
        self.assertFalse(json.loads(output.getvalue())['history_recorded'])
        self.assertEqual(self.state_bytes(),before);self.assert_historical_unchanged()


if __name__=='__main__':unittest.main()
