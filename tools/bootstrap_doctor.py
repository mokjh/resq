#!/usr/bin/env python3
"""F01: read-only host discovery and real immutable bootstrap evidence (stdlib only)."""
from __future__ import annotations
import argparse
from datetime import datetime,timezone
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tomllib
from urllib.parse import urlsplit
import uuid

ROOT=Path(__file__).resolve().parents[1]


def now():return datetime.now(timezone.utc).isoformat()
def read(path):return json.loads(Path(path).read_text())
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def relative(path):return str(Path(path).resolve().relative_to(ROOT.resolve()))


def write_once(path,data):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    payload=data if isinstance(data,bytes) else data.encode() if isinstance(data,str) else (json.dumps(data,indent=2,ensure_ascii=False,allow_nan=False)+'\n').encode()
    temporary=path.parent/('.'+path.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with temporary.open('xb') as f:f.write(payload);f.flush();os.fsync(f.fileno())
        os.link(temporary,path)
        directory=os.open(path.parent,os.O_RDONLY)
        try:os.fsync(directory)
        finally:os.close(directory)
    finally:temporary.unlink(missing_ok=True)
    return path


def artifact(path,kind='execution_evidence'):
    p=Path(path)
    return {'path':relative(p),'sha256':digest(p),'kind':kind,'byte_length':p.stat().st_size}


def run_readonly(argv,timeout=15):
    """No shell, dependency downloads, credential dumps, device writes or Editor launches."""
    try:
        p=subprocess.run(argv,cwd=ROOT,capture_output=True,text=True,timeout=timeout,check=False)
        return {'argv':argv,'exit_code':p.returncode,'stdout':p.stdout[:16000],'stderr':p.stderr[:4000]}
    except (OSError,subprocess.TimeoutExpired) as exc:
        return {'argv':argv,'exit_code':None,'stdout':'','stderr':type(exc).__name__}


def protected_files():
    # Existing source and planning inputs outside the task's admitted write areas.
    paths=[]
    for p in ROOT.rglob('*'):
        if not p.is_file() or p.is_symlink():continue
        parts=p.relative_to(ROOT).parts
        if parts[0] in ('evidence','automation','game','docs','.git') or '__pycache__' in parts:continue
        paths.append(p)
    return {relative(p):digest(p) for p in paths}


def baseline_checks():
    baseline=read(ROOT/'specification/baseline_manifest.json')
    failures=[]
    for entry in baseline['files']:
        p=ROOT/baseline['root']/entry['path']
        if not p.is_file() or digest(p)!=entry['sha256']:failures.append(entry['path'])
    return {'files_checked':len(baseline['files']),'mismatches':failures}


def inspect_host():
    unity=Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app')
    info={}
    if (unity/'Contents/Info.plist').is_file():
        with (unity/'Contents/Info.plist').open('rb') as f:
            raw=plistlib.load(f);info={k:raw.get(k) for k in ('CFBundleShortVersionString','CFBundleVersion')}
    licenses=[Path('/Library/Application Support/Unity/Unity_lic.ulf'),Path.home()/'Library/Application Support/Unity/Unity_lic.ulf']
    commands={'python':[sys.executable,'--version'],'node':['node','--version'],'ffmpeg':['ffmpeg','-version'],
      'git':['git','--version'],'git_lfs':['git','lfs','version'],'blender':['blender','--version'],'metavr':['metavr','--version']}
    tools={k:run_readonly(v) for k,v in commands.items()}
    git=run_readonly(['git','rev-parse','--show-toplevel'])
    if git['exit_code']==0:
        git.update(head=run_readonly(['git','rev-parse','HEAD']),status=run_readonly(['git','status','--porcelain=v1','--untracked-files=all']))
    disk=shutil.disk_usage(ROOT)
    processes=run_readonly(['ps','-axo','pid=,ppid=,comm='])
    relevant=[]
    for line in processes['stdout'].splitlines():
        if re.search(r'Unity|Blender|blender|Claude|claude|metavr',line):
            pieces=line.strip().split(None,2)
            if len(pieces)==3:
                row={'pid':int(pieces[0]),'ppid':int(pieces[1]),'executable':pieces[2]}
                # Only cwd; never process environments or arguments which can contain credentials.
                if shutil.which('lsof'):
                    cwd=run_readonly(['lsof','-a','-p',pieces[0],'-d','cwd','-Fn'],timeout=5)
                    row['cwd']=[s[1:] for s in cwd['stdout'].splitlines() if s.startswith('n')]
                relevant.append(row)
    browser=run_readonly(['node','-e',"try { console.log(require.resolve('playwright')); console.log(require('playwright/package.json').version) } catch { process.exit(2) }"])
    bridge=[]
    config=Path.home()/'.codex/config.toml'
    if config.is_file():
        raw=tomllib.loads(config.read_text())
        for name,server in raw.get('mcp_servers',{}).items():
            if not re.search('unity|blender|meta',name,re.I):continue
            url=urlsplit(server.get('url',''))
            bridge.append({'name':name,'command_executable':Path(server.get('command','')).name,
              'endpoint_host':url.hostname,'endpoint_port':url.port,'state':'configured_only_not_connection_proof'})
    return {'schema_version':1,'observed_utc':now(),'cwd':str(ROOT),'evidence_level':'host_discovery_only',
      'git':git,'disk':{'total_bytes':disk.total,'free_bytes':disk.free,'free_gib':round(disk.free/2**30,2)},
      'hardware':{'memory_bytes':run_readonly(['sysctl','-n','hw.memsize']), 'cpu':run_readonly(['sysctl','-n','machdep.cpu.brand_string'])},
      'unity':{'application_path':str(unity),'application_exists':unity.exists(),'metadata':info,
        'android_player_module_exists':(unity/'Contents/PlaybackEngines/AndroidPlayer').is_dir(),
        'license_file_present':any(p.is_file() for p in licenses),'licensed_compilation':'not_executed',
        'resq_project_exists':(ROOT/'game/ProjectSettings/ProjectVersion.txt').is_file(),
        'bridge':'not_installed_or_redirected; existing unrelated processes preserved'},
      'tools':tools,'playwright_default_node_resolution':browser,
      'adb_path':shutil.which('adb') or str(Path.home()/'Library/Android/sdk/platform-tools/adb'),
      'related_processes':relevant,'process_discovery_exit_code':processes['exit_code'],
      'process_listing_may_be_truncated':len(processes['stdout'])>=16000,'configured_relevant_bridges':bridge,
      'device_qualification':'not_executed','integration_authentication':'see separate real connector observations; presence is not authorization',
      'paid_operations':0,'external_writes':0}


def clone_source_snapshot(source,destination):
    """Independent APFS copy-on-write snapshot, never a hard link to live source."""
    import ctypes
    if sys.platform!='darwin':raise RuntimeError('F03 clone admission requires macOS APFS')
    source,destination=Path(source),Path(destination)
    if source.is_symlink() or not source.is_file():raise ValueError('Ordinary source file required')
    destination.parent.mkdir(parents=True,exist_ok=True)
    temporary=destination.parent/('.'+destination.name+'.'+uuid.uuid4().hex+'.clone')
    clone=ctypes.CDLL(None,use_errno=True).clonefile
    clone.argtypes=[ctypes.c_char_p,ctypes.c_char_p,ctypes.c_uint32];clone.restype=ctypes.c_int
    try:
        if clone(os.fsencode(source),os.fsencode(temporary),1)!=0:
            error=ctypes.get_errno();raise OSError(error,os.strerror(error))
        if digest(source)!=digest(temporary):raise RuntimeError('Source changed during clone')
        with temporary.open('rb') as stream:os.fsync(stream.fileno())
        os.link(temporary,destination)
        directory=os.open(destination.parent,os.O_RDONLY)
        try:os.fsync(directory)
        finally:os.close(directory)
    finally:temporary.unlink(missing_ok=True)
    return destination


def snapshot_identity(run_dir,source_paths,tool_observation,clone_sources=False,before_write=None):
    inputs=run_dir/'inputs';entries=[]
    def retain(path,value):
        if before_write is not None:
            size=len(value) if isinstance(value,bytes) else len(value.encode()) if isinstance(value,str) else len((json.dumps(value,indent=2,ensure_ascii=False,allow_nan=False)+'\n').encode())
            before_write(size)
        return write_once(path,value)
    for value in sorted(set(source_paths)):
        p=ROOT/value;dest=inputs/'source'/value
        if clone_sources:
            if before_write is not None:before_write(p.stat().st_size)
            clone_source_snapshot(p,dest)
        else:retain(dest,p.read_bytes())
        entries.append(artifact(dest,'source_input_snapshot'))
    source=retain(inputs/'source-manifest.json',{'schema_version':1,'status':'observed_source_snapshot','files':entries})
    payload=retain(inputs/'authored-content.json',read(ROOT/'specification/content/mission_contracts.json'))
    content=retain(inputs/'content-manifest.json',{'schema_version':1,'status':'authored_uncompiled',
      'payload_path':relative(payload),'payload_sha256':digest(payload),'files':[artifact(payload,'authored_content_snapshot')]})
    tool=retain(inputs/'observed-toolchain.json',{'schema_version':1,'status':'observed_unqualified','observations':tool_observation})
    git_head=None
    if (ROOT/'.git').exists():
        r=run_readonly(['git','rev-parse','HEAD']);git_head=r['stdout'].strip() if r['exit_code']==0 else None
    return {'freeze_id':'resq-spec-freeze-1.1.0','rules_version':'resq-rules-1','source_identity':'sha256:'+digest(source),
      'content_hash':digest(payload),'toolchain_hash':digest(tool),'source_manifest':artifact(source,'source_manifest'),
      'content_manifest':artifact(content,'authored_content_manifest'),'toolchain_lock':artifact(tool,'observed_unqualified_toolchain'),
      'git_head':git_head,'dirty':True,'build_sha256':None}


def assemble_evidence(task_id,report_path,run_dir,run_id,attempt_id,identity,case_outcomes,started,outputs,command,
                      prerequisites=None,gates=None,proofs=None,limitations=None,coverage=None,emit_result=True,output_directory=None):
    """Serialize measured case outcomes; this function never decides they passed."""
    check_id=case_outcomes[0]['case_id'].split('/')[0]
    check=next(c for c in read(ROOT/'automation/checks.json')['checks'] if c['id']==check_id)
    case_rows=[]
    for n,row in enumerate(case_outcomes):
        payload=dict(row,identity=identity)
        p=write_once(run_dir/'cases'/f'{n:05}.json',payload)
        case_rows.append(dict({k:v for k,v in row.items() if k!='observations'},evidence=artifact(p,'individual_native_case')))
    failed=sum(r['status']!='passed' for r in case_rows);finished=now();status='passed' if not failed else 'failed';exit_code=0 if not failed else 1
    native=write_once(run_dir/'native-summary.json',{'schema_version':1,'status':status,'executed_count':len(case_rows),
      'failed_count':failed,'required_skipped_count':0,'case_records':[{'case_id':r['case_id'],'status':r['status']} for r in case_rows]})
    stdout=write_once(run_dir/'stdout.txt','\n'.join(r['case_id']+': '+r['status'] for r in case_rows)+'\n')
    stderr=write_once(run_dir/'stderr.txt','')
    bindings=[];artifacts=[]
    for expected in outputs:
        dest=(output_directory or run_dir/'artifacts')/expected;src=ROOT/expected
        if dest.exists():
            if digest(dest)!=digest(src):raise ValueError('Output changed between required checks: '+expected)
        else:write_once(dest,src.read_bytes())
        bindings.append({'expected_path':expected,'artifact_path':relative(dest)});artifacts.append(artifact(dest,'immutable_output_copy'))
    by_assertion={r['dimensions'].get('assertion_id'):r for r in case_rows if r['kind']=='assertion'}
    report={'schema_version':1,'check_id':check_id,'status':status,'exit_code':exit_code,'evidence_kind':check['required_evidence_kind'],
      'identity':identity,'command':command,'coverage':coverage or {},
      'execution_context':{'run_id':run_id,'attempt_id':attempt_id,'task_id':task_id,'started_utc':started,'finished_utc':finished,
        'actor':'real_local_command','cwd':str(ROOT),'stdout':artifact(stdout,'stdout'),'stderr':artifact(stderr,'stderr')},
      'runner':{'executed':True,'exit_code':exit_code,'native_report':artifact(native,'native_json_summary'),
        'executed_count':len(case_rows),'failed_count':failed,'required_skipped_count':0},
      'assertions':[{'id':a['id'],'status':by_assertion[a['id']]['status'],
        'evidence_path':by_assertion[a['id']]['evidence']['path'],'evidence_sha256':by_assertion[a['id']]['evidence']['sha256']} for a in check['assertions']],
      'case_records':case_rows,'artifacts':artifacts,'proof_records':proofs or [],'limitations':limitations or []}
    write_once(report_path,report)
    result={'schema_version':2,'task_id':task_id,'status':status,'evidence_kind':check['required_evidence_kind'],'identity':identity,
      'prerequisite_results':prerequisites or [],'gate_results':gates or [],'output_bindings':bindings,'artifacts':artifacts,
      'checks':[{'check_id':check_id,'status':status,'exit_code':exit_code,'report_path':relative(report_path),
        'report_sha256':digest(report_path),'started_utc':started,'finished_utc':finished}],
      'proof_records':proofs or [],'costs':[],'limitations':limitations or [],'blockers':[] if not failed else ['Native bootstrap/adapter assertions failed; see retained case observations.']}
    if not emit_result:return result,exit_code
    result_path=ROOT/'evidence/results'/task_id/run_id/(attempt_id+'.json');write_once(result_path,result)
    return result_path,exit_code


def report_context(value,task_id,check_id=None):
    p=(ROOT/value).resolve()
    if not p.is_relative_to(ROOT):raise ValueError('report path escapes workspace')
    run_id=p.parent.name;attempt_id=p.stem
    for x in (run_id,attempt_id):
        if not re.fullmatch('[a-z0-9][a-z0-9_-]{0,63}',x):raise ValueError('unsafe run/attempt ID')
    task=next(t for t in read(ROOT/'automation/tasks.json')['tasks'] if t['id']==task_id)
    c=next(c for c in read(ROOT/'automation/checks.json')['checks'] if c['id']==(check_id or task['acceptance_checks'][0]))
    if c['id'] not in task['acceptance_checks']:raise ValueError('check is not owned by this task')
    expected=c['report_path'].format(run_id=run_id,attempt_id=attempt_id)
    if relative(p)!=expected:raise ValueError('report path does not match registered destination')
    run_dir=ROOT/'evidence/runs'/task_id/run_id/attempt_id
    if p.exists() or run_dir.exists():raise FileExistsError('attempt destination already exists; never overwrite history')
    return p,run_dir,run_id,attempt_id,task,c


def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--report',required=True);args=ap.parse_args()
    started=now();report_path,run_dir,run_id,attempt_id,task,check=report_context(args.report,'F01')
    protected=protected_files();baseline=baseline_checks();initial=read(ROOT/'automation/task_state.json')
    observations=inspect_host();write_once(run_dir/'observations.json',observations)
    foundation=ROOT/'evidence/foundation'
    snapshots={'initial-preflight.json':dict(observations,baseline=baseline), 'initial-state.json':initial,
      'initial-status.md':'# Initial implementation state\n\nF01 actual host discovery. No Unity compilation, game/device/human acceptance, paid operation or external write.\n',
      'initial-blockers.md':f'# Initial execution constraints\n\nFree storage: {observations["disk"]["free_gib"]} GiB; Unity admission floor40GiB. Android module present: {observations["unity"]["android_player_module_exists"]}. Licensed compile/device/human proof absent. Continue F02 and read-only research independently.\n'}
    for name,data in snapshots.items():
        if not (foundation/name).exists():write_once(foundation/name,data)
    original_state=read(foundation/'initial-state.json')
    pending=all(s['status']=='pending' for s in original_state['task_states'].values())
    after=protected_files();unrelated_ok=protected==after
    measurements=[(not baseline['mismatches'] and baseline['files_checked']==41,baseline),
      (observations['git']['exit_code'] in (0,128) and observations['disk']['free_bytes']>0,{'observation_path':relative(run_dir/'observations.json'),'credential_content_read':False}),
      (pending and len(original_state['task_states'])==229,{'initial_task_count':len(original_state['task_states']),'all_initially_pending':pending}),
      (unrelated_ok,{'protected_existing_files':len(protected),'changed':[k for k,v in protected.items() if after.get(k)!=v],'external_writes':0,'processes_stopped':0})]
    sources=['tools/bootstrap_doctor.py','automation/tasks.json','automation/checks.json','automation/gates.json',
      'specification/content/acceptance_cases.json','specification/baseline_manifest.json','specification/freeze_manifest.json']
    identity=snapshot_identity(run_dir,sources,observations)
    contract=next(c for c in read(ROOT/'specification/content/acceptance_cases.json')['contracts'] if c['id']=='check-f01')
    outcomes=[]
    for expected,(passed,details) in zip(contract['required_cases'],measurements,strict=True):
        outcomes.append({'case_id':expected['case_id'],'kind':expected['kind'],'status':'passed' if passed else 'failed',
          'dimensions':expected['dimensions'],'metrics':{},'participant_alias':None,'proof_index':None,'observations':details})
    path,code=assemble_evidence('F01',report_path,run_dir,run_id,attempt_id,identity,outcomes,started,task['expected_artifacts'],
      ['python3','tools/bootstrap_doctor.py','--report',relative(report_path)],
      limitations=['Host discovery only; no qualified game/toolchain/device/human evidence.','Unavailable setup is a downstream blocker, not a successful compatibility claim.'])
    print(json.dumps({'status':'passed' if code==0 else 'failed','result_path':relative(path),'preflight':observations['disk'],'baseline':baseline},indent=2));return code


if __name__=='__main__':
    try:raise SystemExit(main())
    except (ValueError,OSError,KeyError) as exc:print(type(exc).__name__+': '+str(exc),file=sys.stderr);raise SystemExit(1)
