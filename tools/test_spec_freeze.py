#!/usr/bin/env python3
"""Mutation and evidence-forgery regression tests for planning validators only."""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from validate_spec_freeze import ROOT,Invalid,read,sha,schema_check,validate,validate_task_result,validate_assets,validate_release,local_path
from evidence_case_contracts import derive_coverage


class FreezeTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='resq-freeze-test-')
        self.root=Path(self.temp.name)/'repo';self.root.mkdir()
        for name in ('specification','automation','Resq_Production_Pack'):
            shutil.copytree(ROOT/name,self.root/name,ignore=shutil.ignore_patterns('__pycache__','freeze_manifest.json'))
        for name in ('AGENTS.md','BUILD_PLAN.md'):shutil.copy2(ROOT/name,self.root/name)
        # Planning mutation fixtures are independent of live accepted execution history.
        # Reset ONLY the isolated copy; never mutate the project's actual task state.
        state=read(self.root/'automation/task_state.json')
        for row in state['task_states'].values():row.update(status='pending',result_path=None,result_sha256=None,repair_attempts=0)
        self.write('automation/task_state.json',state)
        # Gate mutation fixtures likewise own their synthetic readiness heads.
        # Live license/storage/account observations must never leak into them.
        self.live_gate_bytes=(ROOT/'automation/gates.json').read_bytes()
        gates=read(self.root/'automation/gates.json')
        for row in gates['gates']:row.update(status='not_verified',evidence_path=None,evidence_sha256=None)
        self.write('automation/gates.json',gates)
        # Synthetic fixtures must not inherit real action approvals or their external evidence paths.
        self.live_policy_bytes=(ROOT/'automation/autonomy_policy.json').read_bytes()
        policy=read(self.root/'automation/autonomy_policy.json')
        policy['authorization_records']=[]
        self.write('automation/autonomy_policy.json',policy)

    def tearDown(self):self.temp.cleanup()

    def write(self,path,data):
        p=self.root/path;p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text(json.dumps(data,indent=2)+'\n' if not isinstance(data,str) else data)
        return p

    def mutate(self,path,fn):
        d=read(self.root/path);fn(d);self.write(path,d)

    def art(self,path,kind='test-fixture',data=None):
        p=self.root/path
        if data is not None or not p.exists():self.write(path,data if data is not None else {'fixture_only':True})
        return {'path':path,'sha256':sha(p),'kind':kind,'byte_length':p.stat().st_size}

    def identity(self):
        src=self.art('fixtures/source.cs',data='// validation fixture, not game code\n')
        source=self.art('fixtures/source-manifest.json',data={'files':[src]})
        payload=self.art('fixtures/content-payload.json',data='{"fixture_only":true}')
        content=self.art('fixtures/content-manifest.json',data={'payload_path':payload['path'],'payload_sha256':payload['sha256'],'files':[payload]})
        tool=self.art('fixtures/toolchain.json')
        return {'freeze_id':'resq-spec-freeze-1.1.0','rules_version':'resq-rules-1',
          'source_identity':'sha256:'+source['sha256'],'content_hash':payload['sha256'],'toolchain_hash':tool['sha256'],
          'source_manifest':source,'content_manifest':content,'toolchain_lock':tool,'git_head':None,'dirty':True,'build_sha256':None}

    def release_fixture(self):
        a=self.art('fixtures/apk.bin');missions=[f'W{w}-M{m}' for w in range(1,6) for m in range(1,6)]
        return {'schema_version':2,'status':'qualified','scope':'full','edition':'game_only','semantic_version':'1.0.0',
          'platform_version_code':1,'source_commit':None,'source_snapshot':a,'rules_version':'resq-rules-1','toolchain_hash':'a'*64,
          'content_hash':'b'*64,'asset_manifest_hash':'c'*64,'apk':a,'signing_certificate_sha256':'d'*64,
          'implemented_missions':missions,'qualified_missions':missions,'qualified_devices':['quest3','quest3s'],
          'verified_features':[],'evidence':[a],'qualification_tasks':[],'known_limitations':[],'open_issues':[],
          'capture_compatibility':{'build_sha256':a['sha256'],'rules_version':'resq-rules-1','content_hash':'b'*64},'publication_records':[]}

    def result(self,tid='F01'):
        graph=read(self.root/'automation/tasks.json');checks=read(self.root/'automation/checks.json')['checks']
        task=next(t for t in graph['tasks'] if t['id']==tid)
        bindings=[{'expected_path':p,'artifact_path':f'evidence/runs/{tid}/fixture-run/fixture-attempt/artifacts/'+p} for p in task['expected_artifacts']]
        artifacts=[self.art(b['artifact_path']) for b in bindings]
        identity=self.identity();records=[]
        for cid in task['acceptance_checks']:
            c=next(x for x in checks if x['id']==cid)
            contract=next(x for x in read(self.root/'specification/content/acceptance_cases.json')['contracts'] if x['id']==cid)
            cases=[]
            for n,e in enumerate(contract['required_cases']):
                row={'case_id':e['case_id'],'kind':e['kind'],'status':'passed','dimensions':e['dimensions'],
                  'metrics':dict(e['minimum_metrics']),'participant_alias':None,'proof_index':None}
                for k in e['maximum_metrics']:row['metrics'].setdefault(k,0)
                if row['kind']=='base':row['metrics']['stars']=3
                a=self.art(f'fixtures/{cid}/case-{n}.json',data=dict(row,identity=identity));cases.append(dict(row,evidence=a))
            native=self.art('fixtures/'+cid+'-native.json',data={'status':'passed','executed_count':len(cases),'failed_count':0,'required_skipped_count':0,'case_records':[{'case_id':x['case_id'],'status':'passed'} for x in cases]})
            stdout=self.art('fixtures/stdout.txt',data='fixture stdout\n');stderr=self.art('fixtures/stderr.txt',data='')
            coverage={k:derive_coverage(cases,v) for k,v in contract['coverage_derivation'].items()}
            substitutions={'run_id':'fixture-run','attempt_id':'fixture-attempt'}
            report_path=c['report_path'].format(**substitutions)
            assertion_cases={x['dimensions']['assertion_id']:x for x in cases if x['kind']=='assertion'}
            report={'schema_version':1,'check_id':cid,'status':'passed','exit_code':0,'evidence_kind':task['required_evidence_kind'],
              'identity':identity,'command':[x.format(**substitutions) for x in c['command']],'coverage':coverage,
              'execution_context':{'run_id':'fixture-run','attempt_id':'fixture-attempt','task_id':tid,'started_utc':'2026-10-15T00:00:00Z','finished_utc':'2026-10-15T00:00:01Z','actor':'test-fixture-only','cwd':str(self.root),'stdout':stdout,'stderr':stderr},
              'runner':{'executed':True,'exit_code':0,'native_report':native,'executed_count':len(cases),'failed_count':0,'required_skipped_count':0},
              'case_records':cases,
              'assertions':[{'id':a['id'],'status':'passed','evidence_path':assertion_cases[a['id']]['evidence']['path'],'evidence_sha256':assertion_cases[a['id']]['evidence']['sha256']} for a in c['assertions']],
              'artifacts':artifacts,'proof_records':[],'limitations':['Isolated validator fixture; never game evidence.']}
            self.write(report_path,report)
            p=self.root/report_path
            records.append({'check_id':cid,'status':'passed','exit_code':0,'report_path':report_path,'report_sha256':sha(p),'started_utc':'2026-10-15T00:00:00Z','finished_utc':'2026-10-15T00:00:01Z'})
        return {'schema_version':2,'task_id':tid,'status':'passed','evidence_kind':task['required_evidence_kind'],'identity':identity,'prerequisite_results':[],'gate_results':[],
          'output_bindings':bindings,'artifacts':artifacts,'checks':records,'proof_records':[],'costs':[],'limitations':['Validator fixture only'],'blockers':[]}

    def edit_report(self,r,fn):
        c=r['checks'][0];p=self.root/c['report_path'];d=read(p);fn(d);self.write(c['report_path'],d);c['report_sha256']=sha(p)

    def assertInvalidFreeze(self,pattern):
        with self.assertRaisesRegex(Invalid,pattern):validate(self.root,False)

    def test_valid_freeze(self):self.assertEqual(validate(self.root,False)['counts']['missions'],25)
    def test_fixture_gate_heads_are_isolated_from_live_observations(self):
        for row in read(self.root/'automation/gates.json')['gates']:
            self.assertEqual(row['status'],'not_verified')
            self.assertIsNone(row['evidence_path']);self.assertIsNone(row['evidence_sha256'])
        self.assertEqual((ROOT/'automation/gates.json').read_bytes(),self.live_gate_bytes)
    def test_scope_cut(self):
        self.mutate('automation/tasks.json',lambda d:d['scope'].update(missions=24));self.assertInvalidFreeze('scope changed')
    def test_missing_mission(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'].pop());self.assertInvalidFreeze('full25')
    def test_duplicate_mission(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'].append(copy.deepcopy(d['missions'][0])));self.assertInvalidFreeze('duplicate row')
    def test_changed_original_pack(self):
        with (self.root/'Resq_Production_Pack/README.md').open('a') as f:f.write('drift')
        self.assertInvalidFreeze('original pack changed')
    def test_task_cycle(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][0]['depends_on'].append('FULL05'));self.assertInvalidFreeze('cycle')
    def test_unknown_dependency(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][1]['depends_on'].append('absent'));self.assertInvalidFreeze('unknown dependency')
    def test_missing_acceptance_check(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][1]['acceptance_checks'].append('absent'));self.assertInvalidFreeze('unknown acceptance')
    def test_check_wrong_owner(self):
        self.mutate('automation/checks.json',lambda d:d['checks'][0].update(task_id='F02'));self.assertInvalidFreeze('ownership')
    def test_no_assertions(self):
        self.mutate('automation/checks.json',lambda d:d['checks'][0].update(assertions=[]));self.assertInvalidFreeze('no assertions')
    def test_gate_removed(self):
        self.mutate('automation/gates.json',lambda d:d['gates'].pop());self.assertInvalidFreeze('unknown task gate')
    def test_paid_not_authorized(self):
        self.mutate('automation/autonomy_policy.json',lambda d:d['paid_generation'].update(enabled=True,total_budget_usd=1));self.assertInvalidFreeze('unapproved paid')
    def test_publication_not_authorized(self):
        self.mutate('automation/autonomy_policy.json',lambda d:d['external_actions'].update(production_deployment=True));self.assertInvalidFreeze('unapproved external')
    def expired_authorization_fixture(self):
        return {'schema_version':1,'action':'preview_deployment','provider':'Vercel','destination':'fixture-only',
          'authorized_by':'user','user_instruction_reference':'isolated validator fixture; no real approval',
          'recorded_utc':'2026-01-01T00:00:00Z','maximum_credits':0,'maximum_usd':0,'allowed_artifact_hashes':[],
          'expires_utc':'2026-01-02T00:00:00Z'}
    def test_expired_approval_preserved_as_history(self):
        self.mutate('automation/autonomy_policy.json',lambda d:d['authorization_records'].append(self.expired_authorization_fixture()))
        self.assertEqual(validate(self.root,False)['status'],'passed')
    def test_expired_approval_cannot_enable_new_work(self):
        def change(d):
            d['authorization_records'].append(self.expired_authorization_fixture());d['external_actions']['preview_deployment']=True
        self.mutate('automation/autonomy_policy.json',change);self.assertInvalidFreeze('unapproved external')
    def test_stock_overrides(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['maximum_stock'].update(bridge=3));self.assertInvalidFreeze('stock policy')
    def test_spawn_count(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['groups'][0].update(count=10));self.assertInvalidFreeze('population')
    def test_temporal_unknown_event(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['objectives'][-1]['temporal'].update(end_event='absent'));self.assertInvalidFreeze('temporal milestone')
    def test_temporal_unknown_field(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['objectives'][0]['temporal'].update(delay=1));self.assertInvalidFreeze('not exactly oneOf')
    def test_warning_after_effect(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][0].update(warning_at_scenario_tick=9999));self.assertInvalidFreeze('warning after')
    def test_receiving_capacity(self):
        self.mutate('specification/content/layout_recipes.json',lambda d:d['recipes'][0]['receiving'].update(total_permanent_capacity=1));self.assertInvalidFreeze('receiving capacity')
    def test_challenge_removed(self):
        self.mutate('specification/content/modes_and_challenges.json',lambda d:d['challenges'].pop());self.assertInvalidFreeze('challenge scope')
    def test_relaxed_clock(self):
        self.mutate('specification/content/modes_and_challenges.json',lambda d:d['rule_profiles'][1].update(hazard_clock_denominator=3));self.assertInvalidFreeze('Relaxed clock')
    def test_state_cannot_claim_pass_without_result(self):
        self.mutate('automation/task_state.json',lambda d:d['task_states']['F01'].update(status='passed'));self.assertInvalidFreeze('no result')
    def test_duplicate_json_key(self):
        self.write('automation/gates.json','{"gates":[],"gates":[]}')
        self.assertInvalidFreeze('duplicate JSON key')
    def test_boolean_not_integer(self):
        with self.assertRaisesRegex(Invalid,'integer'):schema_check(True,{'type':'integer'})
    def test_unsupported_schema_not_ignored(self):
        with self.assertRaisesRegex(Invalid,'unsupported'):schema_check(1,{'multipleOf':2})
    def test_artifact_path_escape(self):
        with self.assertRaisesRegex(Invalid,'traversal'):local_path(self.root,'../escape')
    def test_symlink_escape(self):
        (self.root/'escape').symlink_to(Path(self.temp.name))
        with self.assertRaisesRegex(Invalid,'escapes'):local_path(self.root,'escape/outside')
    def test_valid_isolated_execution_fixture(self):validate_task_result(self.root,self.result())
    def test_blocked_report_can_truthfully_have_no_native_execution(self):
        r=self.result();d=read(self.root/r['checks'][0]['report_path'])
        d.update(status='blocked',exit_code=75,assertions=[],case_records=[])
        d['runner'].update(executed=False,exit_code=75,native_report=None,executed_count=0,failed_count=0,required_skipped_count=0)
        schema_check(d,read(self.root/'specification/schemas/check_report.schema.json'))
    def test_passed_report_still_requires_individual_native_cases(self):
        r=self.result();d=read(self.root/r['checks'][0]['report_path']);d['case_records']=[]
        with self.assertRaises(Invalid):schema_check(d,read(self.root/'specification/schemas/check_report.schema.json'))
    def test_passed_report_still_requires_native_artifact(self):
        r=self.result();d=read(self.root/r['checks'][0]['report_path']);d['runner']['native_report']=None
        with self.assertRaises(Invalid):schema_check(d,read(self.root/'specification/schemas/check_report.schema.json'))
    def test_failed_case_can_be_preserved_in_failed_report(self):
        r=self.result();d=read(self.root/r['checks'][0]['report_path']);d.update(status='failed',exit_code=1)
        d['case_records'][0]['status']='failed';d['runner'].update(exit_code=1,failed_count=1)
        schema_check(d,read(self.root/'specification/schemas/check_report.schema.json'))
    def test_state_wrong_task_result(self):
        r=self.result();p=self.write('fixtures/result.json',r)
        self.mutate('automation/task_state.json',lambda d:d['task_states']['F02'].update(status='passed',result_path='fixtures/result.json',result_sha256=sha(p)))
        self.assertInvalidFreeze('identity does not match')
    def test_state_nonpassed_result(self):
        r=self.result();r['status']='blocked';p=self.write('fixtures/result.json',r)
        self.mutate('automation/task_state.json',lambda d:d['task_states']['F01'].update(status='passed',result_path='fixtures/result.json',result_sha256=sha(p)))
        self.assertInvalidFreeze('nonpassed result')
    def test_missing_prerequisite_results(self):
        with self.assertRaisesRegex(Invalid,'prerequisite result set'):validate_task_result(self.root,self.result('F02'))
    def test_prerequisite_not_passed(self):
        r=self.result('F02');a=self.art('fixtures/upstream.json',data=self.result())
        r['prerequisite_results']=[{'task_id':'F01','result_path':a['path'],'result_sha256':a['sha256']}]
        with self.assertRaisesRegex(Invalid,'prerequisite task not passed'):validate_task_result(self.root,r)
    def test_missing_required_gate(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][0]['required_gates'].append('host_license'))
        with self.assertRaisesRegex(Invalid,'prerequisite gate set'):validate_task_result(self.root,self.result())
    def test_gate_not_passed(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][0]['required_gates'].append('host_license'))
        r=self.result();r['gate_results']=[{'gate_id':'host_license','evidence_path':'fixtures/gate.json','evidence_sha256':'0'*64}]
        with self.assertRaisesRegex(Invalid,'gate not passed'):validate_task_result(self.root,r)
    def gate_history_fixture(self):
        self.mutate('automation/tasks.json',lambda d:d['tasks'][0]['required_gates'].append('host_license'))
        r=self.result();support=self.art('fixtures/license-ready.txt',data='Isolated validator fixture only')
        g={'schema_version':1,'gate_id':'host_license','status':'passed','type':'account','reviewer_kind':'machine',
          'reviewer_alias':'fixture-only','observed_utc':'2026-10-15T00:00:00Z','criteria':{'pass_condition':True},
          'evidence':[support],'authorization_path':None}
        old=self.art('fixtures/gate-old.json',data=g);g['observed_utc']='2026-10-15T01:00:00Z';new=self.art('fixtures/gate-new.json',data=g)
        r['gate_results']=[{'gate_id':'host_license','evidence_path':old['path'],'evidence_sha256':old['sha256']}]
        self.mutate('automation/gates.json',lambda d:next(x for x in d['gates'] if x['id']=='host_license').update(status='passed',evidence_path=new['path'],evidence_sha256=new['sha256']))
        return r
    def test_gate_refresh_preserves_accepted_history(self):
        validate_task_result(self.root,self.gate_history_fixture(),verify_current_gates=False)
    def test_new_operation_requires_current_gate_head(self):
        with self.assertRaisesRegex(Invalid,'gate head mismatch'):validate_task_result(self.root,self.gate_history_fixture())
    def test_output_history_not_mutable(self):
        r=self.result();r['output_bindings'][0]['artifact_path']='fixtures/mutable.json';r['artifacts'].append(self.art('fixtures/mutable.json'))
        with self.assertRaisesRegex(Invalid,'immutable per-attempt'):validate_task_result(self.root,r)
    def test_report_path_template_cannot_remain_literal(self):
        r=self.result();c=r['checks'][0];a=self.art('evidence/checks/f01/{run_id}/{attempt_id}.json',data=read(self.root/c['report_path']))
        c.update(report_path=a['path'],report_sha256=a['sha256'])
        with self.assertRaisesRegex(Invalid,'registered check report path'):validate_task_result(self.root,r)
    def test_unsafe_execution_id(self):
        r=self.result();self.edit_report(r,lambda d:d['execution_context'].update(run_id='../escape'))
        with self.assertRaisesRegex(Invalid,'unsafe run'):validate_task_result(self.root,r)
    def test_missing_individual_case(self):
        r=self.result();self.edit_report(r,lambda d:d['case_records'].pop())
        with self.assertRaisesRegex(Invalid,'required case set'):validate_task_result(self.root,r)
    def test_native_case_absence(self):
        r=self.result()
        def change(d):
            native=d['runner']['native_report'];data=read(self.root/native['path']);data['case_records'].pop();self.write(native['path'],data);native.update(self.art(native['path']))
        self.edit_report(r,change)
        with self.assertRaisesRegex(Invalid,'not executed by native'):validate_task_result(self.root,r)
    def test_case_evidence_stale_identity(self):
        r=self.result()
        def change(d):
            a=d['case_records'][0]['evidence'];data=read(self.root/a['path']);data['identity']['content_hash']='0'*64;self.write(a['path'],data);a.update(self.art(a['path']))
        self.edit_report(r,change)
        with self.assertRaisesRegex(Invalid,'case evidence stale'):validate_task_result(self.root,r)
    def test_inflated_coverage_summary(self):
        tid='W1-M1-AUTHOR'
        def fixture_dependencies(d):
            next(t for t in d['tasks'] if t['id']==tid)['depends_on']=[]
        self.mutate('automation/tasks.json',fixture_dependencies)
        r=self.result(tid);self.edit_report(r,lambda d:d['coverage'].update(layout_count=2.0))
        # Exact numeric equivalence still means two cases; inflation must fail.
        self.edit_report(r,lambda d:d['coverage'].update(layout_count=3))
        with self.assertRaisesRegex(Invalid,'coverage exact mismatch'):validate_task_result(self.root,r)
    def test_duplicate_case_dimensions(self):
        tid='W1-M1-AUTHOR';self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']==tid).update(depends_on=[]))
        r=self.result(tid);self.edit_report(r,lambda d:d['case_records'][1]['dimensions'].update(layout='compact'))
        with self.assertRaisesRegex(Invalid,'case dimensions'):validate_task_result(self.root,r)
    def test_unknown_event_opcode(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][0]['operations'][0].update(kind='force_rescue'))
        self.assertInvalidFreeze('not exactly oneOf')
    def test_event_operand_bound(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][0]['operations'][0].update(value=1000000001))
        self.assertInvalidFreeze('not exactly oneOf')
    def test_zero_effect_tick(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][0].update(at_scenario_tick=0,warning_at_scenario_tick=0))
        self.assertInvalidFreeze('below minimum')
    def test_effect_at_deadline(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][-1].update(at_scenario_tick=d['missions'][0]['duration_scenario_ticks']))
        self.assertInvalidFreeze('event order/bound')
    def test_disruptive_warning_too_short(self):
        self.mutate('specification/content/mission_contracts.json',lambda d:d['missions'][0]['events'][0].update(warning_at_scenario_tick=599))
        self.assertInvalidFreeze('warning below 100')
    def test_inventory_scope_loss(self):
        self.mutate('specification/content/production_inventory.json',lambda d:d['rows'].pop())
        self.assertInvalidFreeze('487 unique')
    def test_acceptance_matrix_scope_loss(self):
        self.mutate('specification/content/acceptance_cases.json',lambda d:d['contracts'][0]['required_cases'].pop())
        self.assertInvalidFreeze('case contract drift')
    def test_qualified_release_cannot_declare_success_without_results(self):
        with self.assertRaisesRegex(Invalid,'qualification task set'):validate_release(self.root,self.release_fixture())
    def test_release_rejects_unqualified_device(self):
        r=self.release_fixture();r['qualified_devices']=['quest3']
        with self.assertRaises(Invalid):validate_release(self.root,r)
    def test_release_rejects_p2_issue(self):
        r=self.release_fixture();r['open_issues']=[{'id':'fixture','severity':'P2','description':'Unresolved qualification defect'}]
        with self.assertRaises(Invalid):validate_release(self.root,r)
    def test_video_human_review_is_upstream_of_bundle(self):
        tasks={t['id']:t for t in read(self.root/'automation/tasks.json')['tasks']}
        self.assertIn('FULL-VIDEO-REVIEW',tasks['FULL04']['depends_on'])
        self.assertIn('D03-REVIEW',tasks['D07']['depends_on'])
    def test_physical_matrix_counts(self):
        contracts=read(self.root/'specification/content/acceptance_cases.json')['contracts']
        for w in range(1,6):
            c=next(c for c in contracts if c['task_id']==f'W{w}-DEVICE')
            self.assertEqual(sum(r['kind']=='full_mission' for r in c['required_cases']),42)
        c=next(c for c in contracts if c['task_id']=='FULL-DEVICE')
        self.assertEqual(sum(r['kind']=='full_mission' for r in c['required_cases']),210)
    def test_regeneration_preserves_execution_and_approval_heads(self):
        import contextlib
        import io
        from unittest.mock import patch
        import author_task_graph as author
        state=read(self.root/'automation/task_state.json')
        state['task_states']['F03'].update(status='blocked',repair_attempts=2)
        self.write('automation/task_state.json',state)
        gates=read(self.root/'automation/gates.json')
        gates['gates'][0].update(status='passed',evidence_path='synthetic-only.json',evidence_sha256='a'*64)
        self.write('automation/gates.json',gates)
        policy=(self.root/'automation/autonomy_policy.json').read_bytes()
        checks=read(self.root/'automation/checks.json')
        cases=read(self.root/'specification/content/acceptance_cases.json')
        with patch.object(author,'ROOT',self.root),patch.object(author,'TASKS',[]),patch.object(author,'CHECKS',[]),contextlib.redirect_stdout(io.StringIO()):
            author.main()
        self.assertEqual(read(self.root/'automation/task_state.json'),state)
        self.assertEqual(read(self.root/'automation/gates.json'),gates)
        self.assertEqual((self.root/'automation/autonomy_policy.json').read_bytes(),policy)
        self.assertEqual(read(self.root/'automation/checks.json'),checks)
        self.assertEqual(read(self.root/'specification/content/acceptance_cases.json'),cases)
        self.assertEqual(read(self.root/'automation/tasks.json'),read(ROOT/'automation/tasks.json'))

    def test_authorization_fixture_isolation_preserves_live_policy(self):
        self.assertEqual(read(self.root/'automation/autonomy_policy.json')['authorization_records'],[])
        self.assertEqual((ROOT/'automation/autonomy_policy.json').read_bytes(),self.live_policy_bytes)

    def test_physical_probe_cannot_precede_last_mission_polish(self):
        self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']=='F05')['depends_on'].remove('W5-M5-POLISH'))
        self.assertInvalidFreeze('physical testing precedes full25 development')

    def test_mixed_quest_regression_cannot_escape_final_phase(self):
        self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']=='W1-M1-QUALIFY').update(depends_on=['W1-M1-POLISH','X05','X06']))
        self.assertInvalidFreeze('physical testing precedes full25 development')

    def test_device_look_review_cannot_escape_final_phase(self):
        self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']=='ART-LOOKDEV-HUMAN').update(depends_on=['ART-LOOKDEV']))
        self.assertInvalidFreeze('physical testing precedes full25 development')

    def test_device_look_review_requires_hardware_gate(self):
        self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']=='ART-LOOKDEV-HUMAN')['required_gates'].remove('quest_devices'))
        self.assertInvalidFreeze('device look review needs current device gate')

    def test_android_replay_requires_current_device_gate(self):
        self.mutate('automation/tasks.json',lambda d:next(t for t in d['tasks'] if t['id']=='FULL01')['required_gates'].remove('quest_devices'))
        self.assertInvalidFreeze('Android replay needs current device')
    def test_failed_test_cannot_pass(self):
        r=self.result();r['checks'][0]['status']='failed'
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_not_run_cannot_pass(self):
        r=self.result();r['checks'][0]['status']='not_run'
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_missing_expected_artifact(self):
        r=self.result();r['artifacts'].pop()
        with self.assertRaisesRegex(Invalid,'expected task artifact'):validate_task_result(self.root,r)
    def test_hash_mismatch(self):
        r=self.result();r['artifacts'][0]['sha256']='0'*64
        with self.assertRaisesRegex(Invalid,'hash mismatch'):validate_task_result(self.root,r)
    def test_byte_length_mismatch(self):
        r=self.result();r['artifacts'][0]['byte_length']+=1
        with self.assertRaisesRegex(Invalid,'byte length'):validate_task_result(self.root,r)
    def test_missing_required_check(self):
        r=self.result();r['checks']=[]
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_stale_identity(self):
        r=self.result();self.edit_report(r,lambda d:d['identity'].update(content_hash='0'*64))
        with self.assertRaisesRegex(Invalid,'stale'):validate_task_result(self.root,r)
    def test_wrong_command(self):
        r=self.result();self.edit_report(r,lambda d:d['command'].append('--fake-success'))
        with self.assertRaisesRegex(Invalid,'command mismatch'):validate_task_result(self.root,r)
    def test_assertion_missing(self):
        r=self.result();self.edit_report(r,lambda d:d['assertions'].pop())
        with self.assertRaisesRegex(Invalid,'assertion set'):validate_task_result(self.root,r)
    def test_native_failure_hidden_by_wrapper(self):
        r=self.result()
        def change(d):
            native=d['runner']['native_report'];self.write(native['path'],{'status':'failed','executed_count':4,'failed_count':0,'required_skipped_count':0});native.update(self.art(native['path']))
        self.edit_report(r,change)
        with self.assertRaisesRegex(Invalid,'native JSON did not pass'):validate_task_result(self.root,r)
    def test_zero_native_tests(self):
        r=self.result();self.edit_report(r,lambda d:d['runner'].update(executed_count=0))
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_skipped_native_tests(self):
        r=self.result();self.edit_report(r,lambda d:d['runner'].update(required_skipped_count=1))
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_fake_device_label(self):
        r=self.result();r['evidence_kind']='physical_device'
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_human_ai_record_rejected(self):
        r=self.result();r['proof_records']=[{'record_type':'human_observation','observer_kind':'ai'}]
        with self.assertRaises(Invalid):validate_task_result(self.root,r)
    def test_unqualified_asset_flags(self):
        schema=read(self.root/'specification/schemas/asset_manifest.schema.json');a=self.art('fixtures/asset.blend')
        verification={k:True for k in schema['properties']['assets']['items']['properties']['verification']['properties']}
        verification['unity_import']=False
        data={'schema_version':2,'freeze_id':'resq-spec-freeze-1.1.0','assets':[{'asset_id':'fixture','world_id':'W1','status':'qualified','source':a,'export':a,'rights_status':'verified','rights_record':a,'statistics':{'triangles_lod0':100,'material_count':1,'maximum_texture_size':512},'verification':verification,'unity_render':a,'unity_import_report':a,'machine_review':'pass','human_review':'not_verified','human_review_record':None,'budget_exception':None,'limitations':[]}]}
        with self.assertRaises(Invalid):validate_assets(self.root,data)
    def test_full_release_missing_mission(self):
        a=self.art('fixtures/apk.bin');data={'schema_version':2,'status':'qualified','scope':'full','edition':'game_only','semantic_version':'1.0.0','platform_version_code':1,'source_commit':None,'source_snapshot':a,'rules_version':'resq-rules-1','toolchain_hash':'a'*64,'content_hash':'b'*64,'asset_manifest_hash':'c'*64,'apk':a,'signing_certificate_sha256':'d'*64,'implemented_missions':[f'W{w}-M{m}' for w in range(1,6) for m in range(1,6)][:-1],'qualified_missions':[f'W{w}-M{m}' for w in range(1,6) for m in range(1,6)][:-1],'qualified_devices':['quest3','quest3s'],'verified_features':[],'evidence':[a],'qualification_tasks':[],'known_limitations':[],'open_issues':[],'capture_compatibility':{'build_sha256':a['sha256'],'rules_version':'resq-rules-1','content_hash':'b'*64},'publication_records':[]}
        with self.assertRaisesRegex(Invalid,'all25'):validate_release(self.root,data)


if __name__=='__main__':unittest.main(verbosity=2)
