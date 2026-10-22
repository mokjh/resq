#!/usr/bin/env python3
"""Run actual compiled C# recorder tests; all sample events remain synthetic."""
from pathlib import Path
import argparse,json,os,re,shutil
from runner_core import resource_locks,run_process,sha256_file,write_json_exclusive

ROOT=Path(__file__).resolve().parents[1]
SCRIPTING=Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/Resources/Scripting')


def run(output):
    output=output.resolve()
    if not output.is_relative_to(ROOT/'evidence') or output.exists():raise ValueError('Fresh owned evidence directory required.')
    if shutil.disk_usage(ROOT).free<5*1024**3+2*1024**2:raise RuntimeError('Bounded compiler emergency reserve unavailable.')
    refs=SCRIPTING/'UnityReferenceAssemblies/unity-4.8-api'
    compiler=SCRIPTING/'DotNetSdkRoslyn/csc.dll';dotnet=SCRIPTING/'NetCoreRuntime/dotnet';mono=SCRIPTING/'MonoBleedingEdge/bin/mono'
    reference_files=[refs/name for name in ['mscorlib.dll','System.dll','System.Core.dll']]
    sources=[ROOT/'game/Assets/Resq/Runtime/Probe/ProbeRecorder.cs',ROOT/'game/Assets/Resq/Tests/Probe/Shared/ProbeProtocolChecks.cs',ROOT/'tools/ProbeProtocolHarness.cs']
    if not all(p.is_file() for p in [compiler,dotnet,mono]+reference_files+sources):raise FileNotFoundError('Actual local sources/tools/references required.')
    base=[str(dotnet),str(compiler),'-nologo','-noconfig','-nostdlib+','-langversion:8.0','-deterministic+']+['-reference:'+str(p) for p in reference_files]
    commands=[base+['-target:library','-out:'+str(output/'Resq.Probe.dll'),str(sources[0])],
        base+['-target:exe','-out:'+str(output/'ProbeProtocolHarness.exe'),'-reference:'+str(output/'Resq.Probe.dll'),str(sources[1]),str(sources[2])],
        [str(mono),str(output/'ProbeProtocolHarness.exe')],
        [str(mono),str(output/'ProbeProtocolHarness.exe'),'--synthetic-sample-json']]
    owner={'task':'F04-F05-recorder-source-check','pid':os.getpid()};processes=[]
    with resource_locks(ROOT/'automation/locks',['heavy-job'],owner):
        output.mkdir(parents=True)
        for index,command in enumerate(commands):
            result=run_process(command,cwd=ROOT,stdout_path=output/(str(index)+'.stdout.txt'),stderr_path=output/(str(index)+'.stderr.txt'),
                allowed_commands=[command],timeout_seconds=90,owner=owner,process_record_path=output/(str(index)+'.process.json'),outcome_path=output/(str(index)+'.outcome.json'),monitored_volumes=[ROOT])
            processes.append(result)
            if result['status']!='passed' or result['exit_code']!=0:raise RuntimeError('Actual C# compiler/recorder process failed: '+str(index))
        passed=re.findall(r'^PASS synthetic protocol: ([a-z_]+)$',(output/'2.stdout.txt').read_text(),re.M)
        if len(passed)!=14 or len(set(passed))!=14:raise RuntimeError('Fourteen actual executed recorder checks required.')
        sample=json.loads((output/'3.stdout.txt').read_text())
        if sample.get('evidence_kind')!='unqualified_runtime_instrumentation' or sample.get('accepted_task') is not False:
            raise RuntimeError('Synthetic sample must remain explicitly unqualified.')
        if len(sample['events'])!=10 or sample['missing_event_ids'] or sample['protocol_complete'] is not True:
            raise RuntimeError('Synthetic serialization sample malformed.')
        from foundation_probe import EVENTS
        if {row['event_id'] for row in sample['events']}!=set(EVENTS):raise RuntimeError('C#/Python protocol drift.')
        write_json_exclusive(output/'verification.json',{'kind':'actual_compiled_recorder_tests_with_synthetic_inputs','status':'passed',
            'executed_count':len(passed),'case_records':[{'case_id':'probe-recorder/'+name,'status':'passed'} for name in passed],
            'processes':processes,'sources':[{'path':str(p.relative_to(ROOT)),'sha256':sha256_file(p)} for p in sources],
            'toolchain':[{'path':str(p),'sha256':sha256_file(p)} for p in [compiler,dotnet,mono]+reference_files],
            'outputs':[{'path':str(p.relative_to(ROOT)),'sha256':sha256_file(p),'bytes':p.stat().st_size} for p in [output/'Resq.Probe.dll',output/'ProbeProtocolHarness.exe']],
            'F04_accepted':False,'F05_accepted':False,'limits':['Real C# compilation/execution, synthetic protocol inputs only.',
                'No actual SDK callbacks, physical session, observer, capture, consent, Android APK or Editor execution.']})
    print(json.dumps({'status':'passed','actual_compiled_recorder_checks':14,'physical_sessions':0}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    run(parser.parse_args().output)
