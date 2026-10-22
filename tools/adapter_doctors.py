"""Foundation adapters execute real tests or validate freshly captured primary sources."""
from pathlib import Path
import sys
import tempfile
import shutil
from bootstrap_doctor import ROOT,artifact,read
from task_execution import Blocked
from runner_core import run_process,crosscheck_status


def run_suite(pattern,name,context):
    directory=context['run_dir']/name;native=directory/'native.json'
    volumes=[ROOT,Path(tempfile.gettempdir())]
    if any(shutil.disk_usage(volume).free < 5*1024**3+48*1024**2 for volume in volumes):
        raise Blocked('RUNNER_TEST_STORAGE_BELOW_RESERVE: small suite cannot preserve5GiB plus48MiB working margin')
    command=[sys.executable,'-B',str(ROOT/'tools/run_native_tests.py'),'--pattern',pattern,'--json',str(native)]
    context['backend_started']=True
    process=run_process(command,cwd=ROOT,stdout_path=directory/'stdout.txt',stderr_path=directory/'stderr.txt',
      allowed_commands=[command],timeout_seconds=1200,owner=context['owner'],process_record_path=directory/'process.json',outcome_path=directory/'outcome.json',
      monitored_volumes=volumes)
    if process.get('stop_reason')=='output volume below 5 GiB reserve':
        raise Blocked('RUNNER_TEST_STORAGE_CANCELLED: owned process stopped by the resource guard; native success not inferred')
    if not native.is_file():raise RuntimeError('Native test process emitted no report: '+pattern)
    packet=read(native)
    crosscheck_status(process,packet,reported_status=packet['status'])
    if process['status']!='passed' or packet['status']!='passed':raise RuntimeError('Required test suite failed: '+pattern)
    if packet['executed_count']<=0 or packet['failed_count'] or packet['required_skipped_count']:raise RuntimeError('Zero/failed/skipped native suite: '+pattern)
    return {'process':process,'native':packet,'native_artifact':artifact(native,'actual_unittest_results'),
      'stdout':artifact(directory/'stdout.txt','stdout'),'stderr':artifact(directory/'stderr.txt','stderr')}


def execute(argv,context):
    scope=argv[argv.index('--scope')+1] if '--scope' in argv else None
    if scope=='competition-rules':
        from adapter_research import inspect_rules
        return inspect_rules(context)
    if scope!='runner':raise Blocked('DOCTOR_SCOPE_NOT_IMPLEMENTED: '+str(scope))
    core=run_suite('test_runner_core.py','core-tests',context)
    control=run_suite('test_task_execution.py','control-tests',context)
    evidence=run_suite('test_spec_freeze.py','evidence-tests',context)
    build_binding=run_suite('test_build_candidate.py','build-binding-tests',context)
    # Each acceptance assertion is grounded in actually executed tests, not set true by the router.
    groups=[[(control,'catalog'),(control,'command')],[(control,'missing_backend'),(control,'unknown_family')],
      [(core,'native'),(core,'build'),(core,'status'),(core,'output'),(build_binding,'')],[(evidence,'')],
      [(core,'lock'),(core,'timeout'),(core,'launch')]]
    contract=next(c for c in read(ROOT/'specification/content/acceptance_cases.json')['contracts'] if c['id']==context['check']['id'])
    rows=[]
    for expected,group in zip(contract['required_cases'],groups,strict=True):
        selected=[];sources=[]
        for suite,fragment in group:
            matches=[r for r in suite['native']['case_records'] if fragment in r['case_id']]
            if not matches:raise RuntimeError('Acceptance group has no executed tests: '+fragment)
            selected.extend(matches);sources.append(suite['native_artifact'])
        rows.append({'case_id':expected['case_id'],'kind':expected['kind'],'dimensions':expected['dimensions'],
          'status':'passed' if all(r['status']=='passed' for r in selected) else 'failed','metrics':{'executed_native_tests':len(selected)},
          'participant_alias':None,'proof_index':None,
          'observations':{'native_suite_artifacts':sources,'executed_test_ids':[r['case_id'] for r in selected],
            'scope':'Python runner/parser/forgery checks. C# source exists; Unity compilation is not claimed.'}})
    return rows
