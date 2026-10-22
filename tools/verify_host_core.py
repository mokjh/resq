#!/usr/bin/env python3
"""Compile/run pure C# with installed Unity tools, without importing the project."""
from pathlib import Path
import argparse
import json
import os
import shutil
from datetime import datetime, timezone
from runner_core import resource_locks,run_process,sha256_file,write_json_exclusive

ROOT=Path(__file__).resolve().parents[1]
CONTENTS=Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents')


def run(output):
    output=output.resolve()
    if not output.is_relative_to(ROOT/'evidence') or output.exists():
        raise ValueError('Fresh evidence directory inside workspace required.')
    # This is a bounded compiler probe, not an Editor import or Android build.
    # Keep the runner's 5GiB emergency margin and cap source/output sizes.
    if shutil.disk_usage(ROOT).free < 5*1024**3+2*1024**2:
        raise RuntimeError('Insufficient margin even for the bounded C# probe.')
    scripting=CONTENTS/'Resources/Scripting'
    dotnet=scripting/'NetCoreRuntime/dotnet';compiler=scripting/'DotNetSdkRoslyn/csc.dll'
    mono=scripting/'MonoBleedingEdge/bin/mono';references=scripting/'UnityReferenceAssemblies/unity-4.8-api'
    required=[dotnet,compiler,mono]+[references/n for n in ('mscorlib.dll','System.dll','System.Core.dll')]
    if not all(p.is_file() for p in required):
        raise FileNotFoundError('Installed compiler/runtime/reference bundle incomplete.')
    sources=sorted((ROOT/'game/Assets/Resq/Runtime/Core').glob('*.cs'))
    shared=ROOT/'game/Assets/Resq/Tests/Shared/CoreSmokeChecks.cs';harness=ROOT/'tools/HostCoreHarness.cs'
    if not sources or sum(p.stat().st_size for p in sources+[shared,harness])>1024**2:
        raise ValueError('Unexpected source set/size: review required.')
    owner={'task':'F03-source-probe','pid':os.getpid(),'cwd':str(ROOT)}
    with resource_locks(ROOT/'automation/locks',['heavy-job'],owner):
        output.mkdir(parents=True)
        common=[str(dotnet),str(compiler),'-nologo','-noconfig','-nostdlib+','-langversion:8.0','-deterministic+','-optimize+']
        common += ['-reference:'+str(p) for p in required[3:]]
        commands=[common+['-target:library','-out:'+str(output/'Resq.Core.dll')]+[str(p) for p in sources],
                  common+['-target:exe','-out:'+str(output/'HostCoreHarness.exe'),'-reference:'+str(output/'Resq.Core.dll'),str(shared),str(harness)],
                  [str(mono),str(output/'HostCoreHarness.exe')]]
        processes=[]
        for index,command in enumerate(commands):
            result=run_process(command,cwd=ROOT,stdout_path=output/(str(index)+'.stdout.txt'),stderr_path=output/(str(index)+'.stderr.txt'),
                allowed_commands=[command],timeout_seconds=90,owner=owner,process_record_path=output/(str(index)+'.process.json'),
                outcome_path=output/(str(index)+'.outcome.json'),monitored_volumes=[ROOT])
            processes.append(result)
            if result['status']!='passed' or result['exit_code']!=0:
                raise RuntimeError('Actual compiler/runtime failed; logs retained: '+str(index))
        native=json.loads((output/'2.stdout.txt').read_text())
        if native['editor_execution'] is not False or native['failed_count'] or native['core_engine_reference_count']:
            raise RuntimeError('Native C# probe failed.')
        if len(native['case_records'])!=7 or any(c['status']!='passed' for c in native['case_records']):
            raise RuntimeError('Missing/failed native C# cases.')
        artifacts=[{'path':str(p.relative_to(ROOT)),'sha256':sha256_file(p),'bytes':p.stat().st_size}
            for p in sources+[shared,harness,output/'Resq.Core.dll',output/'HostCoreHarness.exe']]
        write_json_exclusive(output/'verification.json',{'kind':'bounded_actual_csharp_probe','status':'passed',
            'observed_utc':datetime.now(timezone.utc).isoformat(),'processes':processes,'artifacts':artifacts,'native':native,
            'toolchain':[{'path':str(p),'sha256':sha256_file(p)} for p in required],
            'f03_accepted':False,'limits':['Real compiled Core/shared-check execution using installed Unity Roslyn/Mono, no separate dependency installed.',
                'Not Editor execution/licensed compilation, Unity import, Android, gameplay or device evidence.']})
        print(json.dumps({'status':'passed','actual_csharp_cases':7,'core_engine_references':0,'f03_accepted':False}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    run(parser.parse_args().output)
