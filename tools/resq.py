#!/usr/bin/env python3
"""Execute frozen RESQ commands; missing prerequisites/backends exit blocked (75)."""
from __future__ import annotations
import json
from pathlib import Path
import sys
import time
import os
from copy import deepcopy
from bootstrap_doctor import ROOT,now,read,relative,report_context,snapshot_identity,assemble_evidence,write_once,artifact,digest
from task_execution import Blocked,RoundBlocked,FAMILIES,accept_result,backend_for,terminal_result,bound_check,prerequisites,ready_snapshot,source_files,task_registry,record_state,check_registry,combine_check_results,implementation_round,round_projection


def bind_backend_candidate(packet,context):
    """F05 tests the immutable F04 APK; executor source is retained separately."""
    claimed=packet.get('candidate_identity')
    task=context['task']['id']
    if task!='F05':
        if claimed is not None:raise RuntimeError('CANDIDATE_OVERRIDE_NOT_ALLOWED: '+task)
        return
    if not isinstance(claimed,dict):raise RuntimeError('F05_CANDIDATE_IDENTITY_REQUIRED')
    references=[ref for ref in context.get('prerequisites',[]) if ref['task_id']=='F04']
    if len(references)!=1:raise RuntimeError('F05_CURRENT_F04_REFERENCE_REQUIRED')
    reference=references[0]
    head=read(ROOT/'automation/task_state.json')['task_states']['F04']
    if head['status']!='passed' or any(head[key]!=reference[key] for key in ('result_path','result_sha256')):
        raise RuntimeError('F05_F04_HEAD_CHANGED_AFTER_ADMISSION')
    path=(ROOT/reference['result_path']).resolve()
    if not path.is_relative_to(ROOT.resolve()) or not path.is_file() or digest(path)!=reference['result_sha256']:
        raise RuntimeError('F05_F04_RESULT_IDENTITY_CHANGED')
    result=read(path);expected=result.get('identity')
    if result.get('task_id')!='F04' or result.get('status')!='passed' or claimed!=expected:
        raise RuntimeError('F05_CANDIDATE_DIFFERS_FROM_ACCEPTED_F04')
    build=expected.get('build_sha256')
    if not isinstance(build,str) or len(build)!=64 or any(c not in '0123456789abcdef' for c in build):
        raise RuntimeError('F05_ACTUAL_APK_IDENTITY_REQUIRED')
    cases=packet.get('case_outcomes')
    if not isinstance(cases,list) or not cases or any(not isinstance(case.get('observations'),dict) for case in cases):
        raise RuntimeError('F05_BOUND_CASE_OBSERVATIONS_REQUIRED')
    executor=write_once(context['run_dir']/'executor-identity.json',deepcopy(context['identity']))
    for case in cases:case['observations']=dict(case['observations'],executor_identity=artifact(executor,'executor_source_identity'))
    # The local `identity` in main points to this same object. Preserve that alias.
    context['identity'].clear();context['identity'].update(deepcopy(expected))


def dispatch_backend(family,argv,context):
    """Only in-process reviewed adapters or exact allowlisted backend executables."""
    if family=='doctor':
        from adapter_doctors import execute
        return execute(argv,context)
    if family=='web' and '--stage' in argv and argv[argv.index('--stage')+1]=='reference-lock':
        from adapter_research import inspect_reference_lock
        return inspect_reference_lock(context)
    if family=='game':
        from adapter_unity import execute
        return execute(argv,context)
    from task_execution import require_backend
    from runner_core import run_process
    backend=require_backend(family)
    native=context['run_dir']/'backend-cases.json'
    command=[sys.executable,'-B',str(backend),*argv[1:],'--native-cases',str(native)]
    context['backend_started']=True
    process=run_process(command,cwd=ROOT,stdout_path=context['run_dir']/'backend.stdout.txt',stderr_path=context['run_dir']/'backend.stderr.txt',
      allowed_commands=[command],timeout_seconds=1800,owner=context['owner'],process_record_path=context['run_dir']/'process.json',outcome_path=context['run_dir']/'process-outcome.json')
    if process.get('exit_code')==75 and not any(process.get(k) for k in ('timed_out','launch_error','output_errors','stop_reason','interrupted')):
        raise Blocked('BACKEND_BLOCKED: backend exited75 without native success cases; see retained backend stdout/stderr')
    if process['status']!='passed' or process['exit_code']!=0:raise RuntimeError('BACKEND_FAILED: actual process status/exit '+str(process['status'])+'/'+str(process['exit_code']))
    if not native.is_file():raise RuntimeError('BACKEND_MISSING_NATIVE_CASES')
    packet=read(native)
    if packet.get('status')!='passed':raise RuntimeError('BACKEND_NATIVE_STATUS_NOT_PASSED')
    bind_backend_candidate(packet,context)
    context['proof_records']=packet.get('proof_records',[])
    return packet['case_outcomes']


def main(argv=None):
    argv=list(sys.argv[1:] if argv is None else argv)
    if argv in (['status'],['ready']):print(json.dumps(ready_snapshot(),indent=2));return 0
    if len(argv)==2 and argv[0]=='accept':
        path=(ROOT/argv[1]).resolve()
        if not path.is_relative_to(ROOT):raise Blocked('result path escapes workspace')
        print(json.dumps(accept_result(path),indent=2));return 0
    if '--report' not in argv or argv.index('--report')!=len(argv)-2:raise Blocked('MISSING_REPORT: use exact registered argv and --report path')
    command=argv[:-2];check=bound_check(command);task=task_registry()[check['task_id']]
    report_path,run_dir,run_id,attempt_id,_,_=report_context(argv[-1],task['id'],check_id=check['id'])
    started=now();owner={'task_id':task['id'],'run_id':run_id,'attempt_id':attempt_id,'pid':os.getpid(),'cwd':str(ROOT)}
    context={'task':task,'check':check,'report_path':report_path,'run_dir':run_dir,'run_id':run_id,'attempt_id':attempt_id,'owner':owner}
    try:
        refs,gates=prerequisites(task,run_id,attempt_id)
        context.update(prerequisites=refs,gates=gates)
        from runner_core import resource_locks
        locks=list(task['single_writer_locks'])
        if task['id']=='F02' and 'heavy-job' not in locks:locks.append('heavy-job')
        with resource_locks(ROOT/'automation/locks',locks,owner):
            inputs=source_files()
            round_info=implementation_round(task['id'],run_id,read(ROOT/'automation/task_state.json')['task_states'][task['id']])
            if round_info is not None:
                context['implementation_round']=round_info['binding']
                admission=write_once(run_dir/'implementation-round-admission.json',round_projection(round_info))
                inputs=sorted(set(inputs+round_info['source_paths']+[relative(admission)]))
            identity=snapshot_identity(run_dir,inputs,{'status':'observed_unqualified','python':sys.version,'observed_utc':now()},
                clone_sources=task['id'] in ('F03','C01','C02','C03','C04','C05','C06','C07','C08'))
            context['identity']=identity
            record_state(task['id'],'running',None,'Backend admitted with exclusive ownership.',run_id,attempt_id)
            context['attempt_started']=True
            from evidence_case_contracts import derive_coverage
            parts=[];code=0
            for cid in task['acceptance_checks']:
                c=check_registry()[cid];actual_report=ROOT/c['report_path'].format(run_id=run_id,attempt_id=attempt_id)
                actual_command=[s.format(run_id=run_id,attempt_id=attempt_id) for s in c['command']]
                check_dir=run_dir if len(task['acceptance_checks'])==1 else run_dir/'checks'/cid
                context.update(check=c,run_dir=check_dir,report_path=actual_report,command=actual_command[2:-2])
                outcomes=dispatch_backend(actual_command[2],actual_command[2:-2],context)
                contract=next(row for row in read(ROOT/'specification/content/acceptance_cases.json')['contracts'] if row['id']==cid)
                coverage={k:derive_coverage(outcomes,r) for k,r in contract['coverage_derivation'].items()}
                part,part_code=assemble_evidence(task['id'],actual_report,check_dir,run_id,attempt_id,identity,outcomes,started,
                  task['expected_artifacts'],actual_command,prerequisites=refs,gates=gates,coverage=coverage,proofs=context.get('proof_records',[]),
                  limitations=['Native local adapter evidence only; other evidence dimensions require their actual actions.','No physical device, human observation, authored campaign solvability or release qualification inferred.'],
                  emit_result=False,output_directory=run_dir/'artifacts')
                parts.append(part);code=max(code,part_code)
            aggregate=combine_check_results(parts,task)
            result=ROOT/'evidence/results'/task['id']/run_id/(attempt_id+'.json');write_once(result,aggregate)
            if code==0:
                from validate_spec_freeze import validate_task_result
                validate_task_result(ROOT,read(result),expected_task=task['id'])
            else:record_state(task['id'],'failed',result,'Native required cases failed.',run_id,attempt_id)
            print(json.dumps({'status':'passed' if code==0 else 'failed','task_id':task['id'],'result_path':relative(result),'accepted':False},indent=2))
            return code
    except RoundBlocked as exc:
        # Never append to a history whose authorization/identity/counters failed validation.
        print(json.dumps({'status':'blocked','task_id':task['id'],'reason':str(exc),'history_recorded':False,
            'retained_evidence':relative(run_dir)},indent=2));return 75
    except Blocked as exc:
        result=terminal_result(task,context.get('report_path',report_path),run_dir,run_id,attempt_id,exc,context=context,argv=command,started=started)
        print(json.dumps({'status':'blocked','task_id':task['id'],'reason':str(exc),'result_path':relative(result)},indent=2));return 75
    except Exception as exc:
        from runner_core import LockBusy,StorageBlocked
        blocked=isinstance(exc,(LockBusy,StorageBlocked)) or (task['id']=='C08' and not context.get('attempt_started',False))
        result=terminal_result(task,context.get('report_path',report_path),run_dir,run_id,attempt_id,exc,status='blocked' if blocked else 'failed',context=context,argv=command,started=started)
        if blocked:
            print(json.dumps({'status':'blocked','task_id':task['id'],'reason':str(exc),'result_path':relative(result)},indent=2));return 75
        print(json.dumps({'status':'failed','task_id':task['id'],'reason':str(exc),'retained_evidence':relative(run_dir)},indent=2));return 1


if __name__=='__main__':
    try:raise SystemExit(main())
    except (Blocked,ValueError,OSError,KeyError) as exc:print(json.dumps({'status':'blocked','exit_code':75,'reason':str(exc)}));raise SystemExit(75)
