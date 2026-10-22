"""Admission, immutable history and adapter dispatch for real RESQ tasks."""
from __future__ import annotations
from datetime import datetime,timezone
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import re
import uuid
from bootstrap_doctor import ROOT,artifact,digest,now,read,relative,snapshot_identity,write_once
from validate_spec_freeze import Invalid,validate,validate_task_result,validate_result_ref,validate_gate


class Blocked(RuntimeError):
    exit_code=75


class RoundBlocked(Blocked):
    """An untrusted/unauthorized round cannot append to its trusted history."""


# This is one reviewed human authorization, not a generic retry-budget override.
C08_IMPLEMENTATION_ROUND={
    'task_id':'C08','run_id':'resumable-20261018','round_id':'c08-resumable-executor-20261018',
    'authorization':{'path':'evidence/runtime-forecast-refactor-20261018/authorization.json',
        'sha256':'a3eeefe73f9a975976e3371d3adb27deb526b305c458bf7f501ebfc1c1cd18d6'},
    'proposal':{'path':'evidence/runtime-forecast-20261017/milestone/resumable-executor-proposal.md',
        'sha256':'e49add24b0cda90125849194942648ff56b4f4e02083bf3c12ad5aa1a988c1e6'},
    'history_anchor':{'path':'automation/history/C08/performance-review-20261017/attempt-05-0010.json',
        'sha256':'ff593b49e915e551266f0d994f4bae28b868e3fe349ba594ca5e2f112c804351','sequence':10},
    'historical_task_counters':{'repair_attempts':3,'execution_attempts':5,'failed_attempts':3},
    'repair_limit':3,
}


# One observed misclassification; applying it requires the exact append-only annotation.
C08_RESOURCE_STOP_CORRECTION={'id': 'c08-attempt03-resource-stop-20261018',
 'task_id': 'C08',
 'run_id': 'resumable-20261018',
 'round_id': 'c08-resumable-executor-20261018',
 'attempt_id': 'attempt-03',
 'failed_sequence': 16,
 'annotation_sequence': 17,
 'recorded_round_counters': {'repair_attempts': 2, 'execution_attempts': 3, 'failed_attempts': 3},
 'stop_reason': 'runtime-forecast volume below2GiB hard reserve',
 'lowest_free_bytes': 2078433280,
 'peak_positive_growth_bytes': 1244668,
 'references': {'failed_event': {'path': 'automation/history/C08/resumable-20261018/attempt-03-0016.json',
                                 'sha256': '0e5dca3f50718c4e5aa5f2556ff5e4a29cdaf7da78e6a898aeb3bf271bf6c585'},
                'result': {'path': 'evidence/results/C08/resumable-20261018/attempt-03.json',
                           'sha256': '8b80f0002c9c0347280dc2467d43701065b4b78e34b8747cb0b59e0bbbb47a4e'},
                'check_report': {'path': 'evidence/checks/c08/resumable-20261018/attempt-03.json',
                                 'sha256': '487f12c300e0d469ff6912c575429f5ec52906e8a0fe512dc6ff0a1e39c89875'},
                'terminal_observation': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/terminal-observation.json',
                                         'sha256': '84acd6ac6ae2e5c62b58d308c75a523f4382f13c850b9e98f3cee46fd65524e1'},
                'source_manifest': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/inputs/source-manifest.json',
                                    'sha256': '44acf41fa503291ad0ea28407e426055fc43e13c547dd8a511c19dc90cd72613'},
                'wrapper_source': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/inputs/source/tools/run_unity_bounded.py',
                                   'sha256': '788b68941721a049504a77e8ee22157007c1770570cc6b4d47e024b7389b94e7'},
                'adapter_source': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/inputs/source/tools/adapter_unity.py',
                                   'sha256': '18d898ddbd1b7b00853e0684c3a2f989ff7940f4240c6abb42f4e81a9ddf4d4b'},
                'outcome': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/native-local/outcome.json',
                            'sha256': '34f1e2c2f8be997379017ec046fa7f693d8d8b489e1f4aba84cb20635201d2e0'},
                'measurements': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/native-local/measurements.json',
                                 'sha256': '0d9e984b28f74b32620ac9f42bd1be4d03841f8704663c6f0275cded79122028'},
                'admission': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/native-local/admission.json',
                              'sha256': '4af320f24080848078ece3306c87dea83bac4302ecd12033434449facf26c7d9'},
                'editor_log': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/native-local/editor.log',
                               'sha256': 'caa895ea0843f0b0e4d25c75e19ed6a1533396c046899e7ed5a9cf78ceffedad'},
                'terminal_lock': {'path': 'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/native-local/terminal-lock-verification.json',
                                  'sha256': 'abca31ca3b802ae3f1886fba4b759a9188e14e7c6f4f1e9b8e92dca11c976495'}},
 'absent_native_outputs': ['evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/nunit.xml',
                           'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-c08/unity-cases.json',
                           'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-runtime-ordering/nunit.xml',
                           'evidence/runs/C08/resumable-20261018/attempt-03/checks/check-runtime-ordering/unity-cases.json',
                           'evidence/checks/runtime-ordering/resumable-20261018/attempt-03.json']}


def _round_require(condition,reason):
    if not condition:raise RoundBlocked('IMPLEMENTATION_ROUND_REJECTED: '+reason)


def _round_file(name,expected=None):
    path=ROOT/name
    _round_require(path.resolve().is_relative_to(ROOT.resolve()) and path.is_file(), 'missing/escaped round input: '+name)
    _round_require(not any(p.is_symlink() for p in (path,*path.parents) if p==ROOT or ROOT in p.parents), 'symlinked round input: '+name)
    data=path.read_bytes();sha=hashlib.sha256(data).hexdigest()
    _round_require(expected is None or sha==expected,'round input hash mismatch: '+name)
    return data,sha


def _resource_fields(actual,expected,label):
    _round_require(isinstance(actual,dict) and all(key in actual and type(actual[key]) is type(value)
        and actual[key]==value for key,value in expected.items()),'resource correction '+label+' differs from exact reviewed evidence')


def _validate_resource_stop_correction(correction):
    """No generic stop-reason exemption: every byte belongs to one known failed event."""
    _round_require(correction==C08_RESOURCE_STOP_CORRECTION,'unknown resource correction')
    refs=correction['references'];documents={}
    for role,ref in refs.items():
        data,_=_round_file(ref['path'],ref['sha256'])
        documents[role]=json.loads(data) if ref['path'].endswith('.json') else data.decode('utf-8')
    for name in correction['absent_native_outputs']:
        path=ROOT/name
        _round_require(path.resolve().is_relative_to(ROOT.resolve()) and not path.exists()
            and not any(p.is_symlink() for p in (path,*path.parents) if p==ROOT or ROOT in p.parents),
            'resource correction cannot waive present or escaped native output: '+name)
    reason=correction['stop_reason'];failed=documents['failed_event'];result=documents['result'];report=documents['check_report']
    _resource_fields(failed,{'task_id':correction['task_id'],'run_id':correction['run_id'],'attempt_id':correction['attempt_id'],
        'sequence':correction['failed_sequence'],'event':'failed','implementation_round':C08_IMPLEMENTATION_ROUND,
        'result_path':refs['result']['path'],'result_sha256':refs['result']['sha256'],
        'reason':'Final resource guard failed: '+reason},'failed history event')
    _resource_fields(result,{'task_id':'C08','status':'failed','checks':[],'output_bindings':[],
        'blockers':['Final resource guard failed: '+reason]},'task result')
    source=result.get('identity',{}).get('source_manifest',{})
    _resource_fields(source,refs['source_manifest'],'source identity')
    _resource_fields(report,{'check_id':'check-c08','status':'failed','exit_code':1,'assertions':[],'case_records':[],
        'runner':{'executed':True,'exit_code':1,'native_report':None,'executed_count':0,'failed_count':0,'required_skipped_count':0}},'check report')
    _resource_fields(report.get('execution_context'),{'task_id':'C08','run_id':correction['run_id'],
        'attempt_id':correction['attempt_id']},'execution context')
    _resource_fields(documents['terminal_observation'],{'status':'failed','exit_code':1,'backend_started':True,
        'reason':'Final resource guard failed: '+reason},'terminal observation')
    outcome=documents['outcome']
    _resource_fields(outcome,{'status':'failed','exit_code':-15,'stop_reason':reason,'timed_out':False,
        'cleanup_attempted':True,'cleanup_errors':[],'execution_error':None,'launch_error':None,'output_errors':[],
        'process_group_terminal':True},'process outcome')
    _resource_fields(documents['measurements'],{'final_guard_reason':reason,'final_observation_error':None,
        'lowest_free_bytes':correction['lowest_free_bytes'],'peak_positive_growth_bytes':correction['peak_positive_growth_bytes'],
        'task_accepted':False},'resource measurements')
    _round_require(0<=correction['lowest_free_bytes']<2**31 and 0<=correction['peak_positive_growth_bytes']<=2**28,
        'resource correction measurements do not show the reviewed reserve stop')
    _resource_fields(documents['admission'],{'status':'passed','task':'C08','stage':'runtime-forecast',
        'reserve_bytes':2**31,'growth':2**28,'task_accepted':False},'fresh admission')
    _resource_fields(documents['terminal_lock'],{'terminal':True,'owned_pid':outcome['pid'],
        'owned_parent_pid':outcome['parent_pid'],'owned_pgid':outcome['pgid'],'holders':'','resq_native_processes':[]},'terminal ownership')
    # Exact logs/sources are retained even though their earlier diagnostics are not erased.
    # Native discovery is not a case result; absence of completed reports remains unqualified.
    _round_require('RESQ native tests discovered: 49' in documents['editor_log']
        and 'ExitCode: 0 Duration: 12s' in documents['editor_log'],'reviewed log landmarks differ')
    return [ref['path'] for ref in refs.values()]


def _apply_resource_stop_correction(info,event):
    correction=event.get('resource_stop_correction')
    _round_require(correction==C08_RESOURCE_STOP_CORRECTION,'unknown resource correction event')
    _resource_fields(event,{'task_id':correction['task_id'],'run_id':correction['run_id'],'attempt_id':correction['attempt_id'],
        'sequence':correction['annotation_sequence'],'event':'resource_stop_correction',
        'implementation_round':info['binding'],'previous_event_sha256':correction['references']['failed_event']['sha256'],
        'result_path':correction['references']['result']['path'],'result_sha256':correction['references']['result']['sha256']},'annotation event')
    _round_require(not info.get('resource_stop_corrections'),'resource correction already applied')
    _round_require(info['counts']==correction['recorded_round_counters']
        and info['attempts'].get(correction['attempt_id'])=='failed','resource correction is not at its exact failed attempt')
    paths=_validate_resource_stop_correction(correction)
    result=deepcopy(info)
    result['resource_stop_corrections']=[deepcopy(correction)]
    result['source_paths']=sorted(set(result['source_paths']+paths))
    return result


def _code_failed_attempts(info):
    return info['counts']['failed_attempts']-len(info.get('resource_stop_corrections',[]))


def _round_transition(info,status,attempt_id):
    """Derive a projection from one new event; blocked starts spend no repair."""
    _round_require(isinstance(attempt_id,str) and re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,63}',attempt_id), 'invalid attempt ID')
    result=deepcopy(info);counts=result['counts'];attempts=result['attempts']
    _round_require('passed' not in attempts.values(),'authorized round is already accepted')
    previous=attempts.get(attempt_id)
    if status=='running':
        _round_require(previous is None,'attempt ID has already been used')
        _round_require('running' not in attempts.values(),'prior round attempt has no terminal observation')
        _round_require(_code_failed_attempts(info)<info['binding']['repair_limit']+1,'REPAIR_BUDGET_EXHAUSTED for authorized round')
        # Recorded counters never decrease; a proven resource stop does not create a new code repair.
        counts['repair_attempts']=max(counts['repair_attempts'],_code_failed_attempts(info))
        counts['execution_attempts']+=1
    elif status in ('failed','passed'):
        _round_require(previous=='running','terminal result has no matching running attempt')
        if status=='failed':counts['failed_attempts']+=1
    elif status=='blocked':
        _round_require(previous in (None,'running'),'attempt already has a terminal observation')
        _round_require(previous=='running' or 'running' not in attempts.values(),'another round attempt is still running')
    else:raise RoundBlocked('IMPLEMENTATION_ROUND_REJECTED: unsupported round transition '+status)
    attempts[attempt_id]=status
    return result


def round_projection(info):
    projection={'binding':deepcopy(info['binding']),**info['counts'],'history_head':dict(info['history_head'])}
    if info.get('resource_stop_corrections'):
        projection.update(resource_stop_corrections=deepcopy(info['resource_stop_corrections']),
            code_failed_attempts=_code_failed_attempts(info))
    return projection


def implementation_round(task_id,run_id,own):
    """Reconstruct only this authorized round from an anchored append-only chain."""
    binding=C08_IMPLEMENTATION_ROUND
    if task_id!=binding['task_id']:
        _round_require(run_id!=binding['run_id'],'reserved round run ID belongs only to C08')
        return None
    _round_require(run_id==binding['run_id'],'C08 requires the exact authorized run ID '+binding['run_id'])
    try:
        _round_require(isinstance(own,dict),'task projection is not an object')
        auth=json.loads(_round_file(binding['authorization']['path'],binding['authorization']['sha256'])[0])
        _round_file(binding['proposal']['path'],binding['proposal']['sha256'])
        _round_file(binding['history_anchor']['path'],binding['history_anchor']['sha256'])
        _round_require(auth['kind']=='human_authorized_new_implementation_round' and auth['round_id']==binding['round_id']
            and auth['proposal']==binding['proposal'] and auth['history_reset'] is False
            and auth['historical_task_counters']==binding['historical_task_counters']
            and auth['new_round']=={'initial_implementation_authorized':True,'repair_limit':binding['repair_limit'],'repair_attempts':0},
            'authorization scope differs from the reviewed round')
        directory=ROOT/'automation/history'/task_id
        _round_require(not any(p.is_symlink() for p in (directory,*directory.rglob('*'))),'symlinked history is not admitted')
        events=[]
        for path in directory.rglob('*.json'):
            name=str(path.relative_to(ROOT));data,sha=_round_file(name);event=json.loads(data)
            _round_require(isinstance(event,dict),'history event is not an object')
            _round_require(type(event.get('sequence')) is int,'history sequence is not an integer')
            events.append((name,event,sha))
        events.sort(key=lambda row:row[1]['sequence'])
        anchor=binding['history_anchor'];previous=None
        _round_require(len(events)>=anchor['sequence'],'history anchor has been removed')
        for sequence,(name,event,sha) in enumerate(events,1):
            _round_require(event.get('task_id')==task_id and event['sequence']==sequence
                and event.get('previous_event_sha256')==previous,'task history sequence/hash chain broken')
            _round_require(name==f'automation/history/{task_id}/{event["run_id"]}/{event["attempt_id"]}-{sequence:04}.json',
                'history path and event identity differ')
            previous=sha
        _round_require(events[anchor['sequence']-1][0]==anchor['path'] and events[anchor['sequence']-1][2]==anchor['sha256'],
            'history anchor does not match its exact chain position')
        info={'binding':deepcopy(binding),'counts':{'repair_attempts':0,'execution_attempts':0,'failed_attempts':0},'attempts':{},
            'history_head':{'path':events[-1][0],'sha256':events[-1][2]},
            'source_paths':[binding['authorization']['path'],binding['proposal']['path'],*[row[0] for row in events]]}
        for _,event,_ in events[anchor['sequence']:]:
            _round_require(event.get('run_id')==binding['run_id'] and event.get('implementation_round')==binding,
                'post-anchor event is not bound to this exact authorized round')
            if event['event']=='resource_stop_correction':info=_apply_resource_stop_correction(info,event)
            else:info=_round_transition(info,event['event'],event['attempt_id'])
        expected={key:binding['historical_task_counters'][key]+value for key,value in info['counts'].items()}
        _round_require(all(type(own.get(key)) is int and own[key]==value for key,value in expected.items()),
            'cumulative task counters differ from preserved history plus round events')
        projections=own.get('implementation_rounds',{})
        _round_require(isinstance(projections,dict) and set(projections)<= {binding['round_id']},'unknown implementation round projection')
        projection=projections.get(binding['round_id'])
        _round_require(projection==round_projection(info) if len(events)>anchor['sequence'] else projection in (None,round_projection(info)),
            'round projection differs from the immutable history head')
        return info
    except RoundBlocked:raise
    except (OSError,ValueError,KeyError,TypeError) as exc:
        raise RoundBlocked('IMPLEMENTATION_ROUND_REJECTED: unreadable round evidence: '+str(exc)) from exc


FAMILIES=('doctor','game','content','art','device','human','web','capture','video','release')


def task_registry():return {t['id']:t for t in read(ROOT/'automation/tasks.json')['tasks']}
def check_registry():return {c['id']:c for c in read(ROOT/'automation/checks.json')['checks']}


def bound_check(argv):
    """Only exact registered argv can be admitted; no arbitrary command execution."""
    for c in check_registry().values():
        if c['task_id']=='F01':continue
        expected=c['command'][2:-2]
        if argv==expected:return c
    raise Blocked('UNREGISTERED_COMMAND: command must match a frozen acceptance command exactly')


def prerequisites(task,run_id=None,attempt_id=None):
    state=read(ROOT/'automation/task_state.json')['task_states'];refs=[]
    own=state[task['id']]
    limit=task.get('max_asset_repair_attempts') if task['workstream']=='art' else task['max_repair_attempts']
    if limit is None:limit=task['max_repair_attempts']
    round_info=implementation_round(task['id'],run_id,own)
    if round_info is not None:_round_transition(round_info,'running',attempt_id)
    elif own.get('failed_attempts',0)>=limit+1:raise Blocked('REPAIR_BUDGET_EXHAUSTED: preserve failures; review a new approach before another attempt')
    for tid in task['depends_on']:
        s=state[tid]
        if s['status']!='passed':raise Blocked('DEPENDENCY_NOT_PASSED: '+tid)
        ref={'task_id':tid,'result_path':s['result_path'],'result_sha256':s['result_sha256']}
        validate_result_ref(ROOT,ref,set(),set());refs.append(ref)
    gates=[];registry={g['id']:g for g in read(ROOT/'automation/gates.json')['gates']}
    for gid in task['required_gates']:
        g=registry[gid]
        if g['status']!='passed':raise Blocked('GATE_NOT_VERIFIED: '+gid+' — '+g['pass_condition'])
        ref={'gate_id':gid,'evidence_path':g['evidence_path'],'evidence_sha256':g['evidence_sha256']}
        validate_gate(ROOT,gid,ref);gates.append(ref)
    return refs,gates


def ready_snapshot():
    state=read(ROOT/'automation/task_state.json')['task_states'];rows=[]
    for tid,t in task_registry().items():
        if state[tid]['status']=='passed':continue
        missing=[d for d in t['depends_on'] if state[d]['status']!='passed']
        gates={g['id']:g for g in read(ROOT/'automation/gates.json')['gates']}
        missing_gates=[g for g in t['required_gates'] if gates[g]['status']!='passed']
        if not missing:rows.append({'task_id':tid,'status':'blocked' if missing_gates else 'ready','missing_gates':missing_gates,'checks':t['acceptance_checks']})
    return rows


def atomic_json(path,data):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    tmp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.pending')
    with tmp.open('x') as f:json.dump(data,f,indent=2);f.write('\n');f.flush();os.fsync(f.fileno())
    os.replace(tmp,path)


def append_history(task_id,status,result_path,reason,run_id,attempt_id,*,implementation_round=None,resource_stop_correction=None):
    """Caller holds task-state lock; append before publishing an atomic projection."""
    prior=list((ROOT/'automation/history'/task_id).rglob('*.json')) if (ROOT/'automation/history'/task_id).exists() else []
    prior.sort(key=lambda p:read(p)['sequence'])
    for n,p in enumerate(prior,1):
        event=read(p)
        if event['sequence']!=n or event.get('previous_event_sha256')!=(digest(prior[n-2]) if n>1 else None):
            raise Invalid('task history sequence/hash chain broken')
    event={'schema_version':1,'task_id':task_id,'event':status,'run_id':run_id,'attempt_id':attempt_id,
      'sequence':len(prior)+1,'previous_event_sha256':digest(prior[-1]) if prior else None,
      'result_path':str(Path(result_path).resolve().relative_to(ROOT.resolve())) if result_path else None,
      'result_sha256':digest(result_path) if result_path else None,
      'reason':reason,'verifier':'independent_validate_task_result' if status=='passed' else 'admission_or_process_observation',
      'recorded_utc':now()}
    if implementation_round is not None:event['implementation_round']=deepcopy(implementation_round)
    if resource_stop_correction is not None:event['resource_stop_correction']=deepcopy(resource_stop_correction)
    write_once(ROOT/'automation/history'/task_id/run_id/f'{attempt_id}-{event["sequence"]:04}.json',event)
    return event


def record_c08_resource_stop_correction():
    """Append this exact observation once; never change an old failure or its counters."""
    from runner_core import ResourceLock
    correction=C08_RESOURCE_STOP_CORRECTION;binding=C08_IMPLEMENTATION_ROUND
    _round_require(all(correction[key]==binding[key] for key in ('task_id','run_id','round_id')),
        'resource correction belongs to a different authorization')
    with ResourceLock(ROOT/'automation/locks','task-state',{'operation':'c08_exact_resource_stop_correction','pid':os.getpid(),'cwd':str(ROOT)}):
        state=read(ROOT/'automation/task_state.json');current=state['task_states']['C08']
        info=implementation_round('C08',binding['run_id'],current)
        _round_require(info['history_head']==correction['references']['failed_event'],'resource correction requires its exact original history head')
        _resource_fields(current,{'status':'failed','result_path':correction['references']['result']['path'],
            'result_sha256':correction['references']['result']['sha256']},'current failed projection')
        # Validate the exact prospective event before creating any immutable output.
        prospective={'task_id':'C08','event':'resource_stop_correction','run_id':binding['run_id'],
            'attempt_id':correction['attempt_id'],'sequence':correction['annotation_sequence'],
            'previous_event_sha256':correction['references']['failed_event']['sha256'],
            'result_path':correction['references']['result']['path'],'result_sha256':correction['references']['result']['sha256'],
            'implementation_round':binding,'resource_stop_correction':correction}
        corrected=_apply_resource_stop_correction(info,prospective)
        event=append_history('C08','resource_stop_correction',ROOT/correction['references']['result']['path'],
            'Exact resource-stop classification annotation; original failed event/result and all cumulative counters retained.',
            binding['run_id'],correction['attempt_id'],implementation_round=binding,resource_stop_correction=correction)
        path=ROOT/'automation/history/C08'/binding['run_id']/f'{correction["attempt_id"]}-{event["sequence"]:04}.json'
        corrected['history_head']={'path':str(path.relative_to(ROOT)),'sha256':digest(path)}
        current['implementation_rounds'][binding['round_id']]=round_projection(corrected)
        atomic_json(ROOT/'automation/task_state.json',state)
        return {'status':'resource_stop_correction_recorded','history_head':corrected['history_head'],
            'recorded_round_counters':dict(corrected['counts']),'code_failed_attempts':_code_failed_attempts(corrected),
            'task_accepted':False}


def invalidate_dependency_heads(state,run_id,attempt_id):
    """Invalidate stale accepted descendants; retain their immutable result/history."""
    rows=state['task_states'];stale={}
    for tid,row in rows.items():
        if row['status']!='passed':continue
        result=read(ROOT/row['result_path'])
        if digest(ROOT/row['result_path'])!=row['result_sha256']:
            raise Invalid('accepted projection result hash mismatch: '+tid)
        for ref in result.get('prerequisite_results',[]):
            upstream=rows[ref['task_id']]
            if upstream['status']!='passed' or upstream['result_path']!=ref['result_path'] or upstream['result_sha256']!=ref['result_sha256']:
                stale[tid]='Accepted prerequisite head changed: '+ref['task_id'];break
    graph=task_registry()
    while True:
        additional={tid:'Transitive prerequisite invalidated: '+next(dep for dep in task['depends_on'] if dep in stale)
            for tid,task in graph.items() if tid in rows and rows[tid]['status']=='passed' and tid not in stale and any(dep in stale for dep in task['depends_on'])}
        if not additional:break
        stale.update(additional)
    for tid,reason in sorted(stale.items()):
        old=ROOT/rows[tid]['result_path']
        append_history(tid,'pending',old,reason+'; historical result retained, fresh checks required.',run_id,attempt_id)
        rows[tid].update(status='pending',result_path=None,result_sha256=None)
        if tid!=C08_IMPLEMENTATION_ROUND['task_id']:rows[tid].update(repair_attempts=0,failed_attempts=0)
    return sorted(stale)


def reconcile_dependency_heads():
    from runner_core import ResourceLock
    with ResourceLock(ROOT/'automation/locks','task-state',{'operation':'reconcile_dependency_heads','pid':os.getpid()}):
        state=read(ROOT/'automation/task_state.json')
        changed=invalidate_dependency_heads(state,'dependency-refresh','reconcile')
        if changed:atomic_json(ROOT/'automation/task_state.json',state)
        return changed


def record_state(task_id,status,result_path,reason,run_id,attempt_id):
    from runner_core import ResourceLock
    owner={'task_id':task_id,'run_id':run_id,'attempt_id':attempt_id,'pid':os.getpid(),'cwd':str(ROOT)}
    with ResourceLock(ROOT/'automation/locks','task-state',owner):
        state=read(ROOT/'automation/task_state.json');current=state['task_states'][task_id]
        round_info=implementation_round(task_id,run_id,current)
        if round_info is not None:round_info=_round_transition(round_info,status,attempt_id)
        event=append_history(task_id,status,result_path,reason,run_id,attempt_id,
            implementation_round=round_info['binding'] if round_info is not None else None)
        retained=current['status']=='passed' and status!='passed'
        if not retained:current.update(status=status,result_path=event['result_path'],result_sha256=event['result_sha256'])
        if round_info is not None:
            current.update({key:round_info['binding']['historical_task_counters'][key]+value for key,value in round_info['counts'].items()})
            path=ROOT/'automation/history'/task_id/run_id/f'{attempt_id}-{event["sequence"]:04}.json'
            round_info['history_head']={'path':str(path.relative_to(ROOT)),'sha256':digest(path)}
            current.setdefault('implementation_rounds',{})[round_info['binding']['round_id']]=round_projection(round_info)
        elif status=='running':
            current['execution_attempts']=current.get('execution_attempts',0)+1
            current['repair_attempts']=current.get('failed_attempts',0)
        elif status=='failed':current['failed_attempts']=current.get('failed_attempts',0)+1
        elif status=='passed':current.update(failed_attempts=0,repair_attempts=0)
        if status=='passed':invalidate_dependency_heads(state,run_id,attempt_id)
        atomic_json(ROOT/'automation/task_state.json',state)


def accept_result(path):
    result=read(path)
    if result['status']!='passed':raise Blocked('ONLY_PASSED_RESULTS_CAN_BE_ACCEPTED')
    report=read(ROOT/result['checks'][0]['report_path']);context=report['execution_context']
    round_info=implementation_round(result['task_id'],context['run_id'],read(ROOT/'automation/task_state.json')['task_states'][result['task_id']])
    if round_info is not None:_round_transition(round_info,'passed',context['attempt_id'])
    reconcile_dependency_heads()
    validate(ROOT)
    validate_task_result(ROOT,result,expected_task=result['task_id'])
    record_state(result['task_id'],'passed',path,'All required native checks and immutable artifacts independently verified.',context['run_id'],context['attempt_id'])
    return {'status':'passed','task_id':result['task_id'],'result_path':relative(path)}


def terminal_result(task,report_path,run_dir,run_id,attempt_id,reason,*,status='blocked',context=None,argv=None,started=None):
    context=context or {};code=75 if status=='blocked' else 1;finished=now();executed=context.get('backend_started',False)
    write_once(run_dir/'terminal-observation.json',{'status':status,'exit_code':code,'reason':str(reason),'observed_utc':finished,'backend_started':executed})
    identity=context.get('identity') or snapshot_identity(run_dir,['tools/resq.py','tools/task_execution.py','automation/tasks.json','automation/checks.json'],
      {'status':'admission_observation_only','observed_utc':finished,'free_bytes':__import__('shutil').disk_usage(ROOT).free})
    if not report_path.exists():
        stdout=write_once(run_dir/'terminal.stdout.txt','')
        stderr=write_once(run_dir/'terminal.stderr.txt',str(reason)+'\n')
        check=context.get('check') or next(c for c in check_registry().values() if c['task_id']==task['id'])
        report={'schema_version':1,'check_id':check['id'],'status':status,'exit_code':code,'evidence_kind':task['required_evidence_kind'],
          'identity':identity,'command':['python3','tools/resq.py',*context.get('command',argv or []),'--report',relative(report_path)],'coverage':{},
          'execution_context':{'run_id':run_id,'attempt_id':attempt_id,'task_id':task['id'],'started_utc':started or finished,'finished_utc':finished,
            'actor':'real_local_command','cwd':str(ROOT),'stdout':artifact(stdout,'stdout'),'stderr':artifact(stderr,'stderr')},
          'runner':{'executed':executed,'exit_code':code,'native_report':None,'executed_count':0,'failed_count':0,'required_skipped_count':0},
          'assertions':[],'case_records':[],'artifacts':[artifact(run_dir/'terminal-observation.json')],'proof_records':[],
          'limitations':['No accepted native cases. Partial logs are retained; this is not passing evidence.']}
        write_once(report_path,report)
    check_record=read(report_path)
    result={'schema_version':2,'task_id':task['id'],'status':status,'evidence_kind':task['required_evidence_kind'],'identity':identity,
      'prerequisite_results':context.get('prerequisites',[]),'gate_results':context.get('gates',[]),'output_bindings':[],
      'artifacts':[artifact(report_path,'nonpassing_report'),artifact(run_dir/'terminal-observation.json')],
      'checks':[],'proof_records':[],'costs':[],'limitations':['Terminal nonpassing attempt; no acceptance implied.'], 'blockers':[str(reason)]}
    path=ROOT/'evidence/results'/task['id']/run_id/(attempt_id+'.json')
    if path.exists():path=path.with_name(attempt_id+'-rejected.json') # Preserve any worker report independently rejected.
    write_once(path,result)
    record_state(task['id'],status,path,str(reason),run_id,attempt_id)
    return path


def backend_for(family):
    if family not in FAMILIES:raise Blocked('UNKNOWN_COMMAND_FAMILY: '+family)
    # Each family has an adapter contract and an actual executable implementation path.
    return ROOT/'tools/backends'/f'{family}.py'


def require_backend(family):
    p=backend_for(family)
    if not p.is_file():raise Blocked('BACKEND_NOT_IMPLEMENTED: '+str(p.relative_to(ROOT))+'; no success substituted')
    return p


def source_files():
    files={'automation/tasks.json','automation/checks.json','automation/gates.json','specification/content/acceptance_cases.json'}
    # Native C02 consumes these immutable contract goldens directly.
    if (ROOT/'specification/fixtures/runtime-golden-v1.json').is_file():
        files.add('specification/fixtures/runtime-golden-v1.json')
    for name in ('automation/resource_profiles.json','automation/local_operation_profiles.json','automation/runtime_operation_profiles.json','automation/resource_estimates.json'):
        if (ROOT/name).is_file(): files.add(name)
    # Source grows after F02: bind actual assemblies, .meta, package declarations,
    # settings and binary imported assets, never Library/generated caches.
    for folder in ('tools','game/Assets','game/Packages','game/ProjectSettings','content/runtime'):
        if (ROOT/folder).is_symlink():raise Invalid('Source symlink directory is not admitted: '+folder)
        for path in (ROOT/folder).rglob('*'):
            if '__pycache__' in path.parts or path.name=='.DS_Store' or path.name.startswith('.env'):
                continue
            if path.is_symlink():raise Invalid('Source symlink is not admitted: '+str(path))
            if not path.is_file():
                continue
            if not path.resolve().is_relative_to(ROOT.resolve()):
                raise Invalid('Source symlink/escape is not admitted: '+str(path))
            if folder=='tools' and path.suffix not in ('.py','.cs','.json','.sh'):
                continue
            files.add(str(path.relative_to(ROOT)))
    return sorted(files)


def combine_check_results(parts,task):
    if not parts:raise Invalid('No executed check results')
    base=dict(parts[0]);checks=[c for r in parts for c in r['checks']]
    if len(checks)!=len(task['acceptance_checks']) or {c['check_id'] for c in checks}!=set(task['acceptance_checks']):
        raise Invalid('Aggregate task result lacks exact required check set')
    for part in parts:
        for field in ('task_id','identity','output_bindings','artifacts','prerequisite_results','gate_results','proof_records'):
            if part[field]!=base[field]:raise Invalid('Mixed identity/outputs across required task checks: '+field)
    base['checks']=checks;base['status']='passed' if all(p['status']=='passed' for p in parts) else 'failed'
    base['blockers']=[b for p in parts for b in p['blockers']]
    return base
