#!/usr/bin/env python3
"""Bounded Roslyn type-check against installed assemblies; not Editor execution."""
from pathlib import Path
import argparse,json,os,shutil
from runner_core import resource_locks,run_process,sha256_file,write_json_exclusive

ROOT=Path(__file__).resolve().parents[1]
SCRIPTING=Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/Resources/Scripting')


def run(output):
    output=output.resolve()
    if not output.is_relative_to(ROOT/'evidence') or output.exists():
        raise ValueError('Fresh workspace evidence directory required.')
    if shutil.disk_usage(ROOT).free<5*1024**3+2*1024**2:
        raise RuntimeError('Bounded compiler emergency reserve unavailable.')
    refs=SCRIPTING/'NetStandard/ref/2.1.0'
    managed=SCRIPTING/'Managed/UnityEngine'
    packages=SCRIPTING.parent/'PackageManager'
    cache=packages/'ProjectTemplates/libcache/com.unity.template.3d-high-end-17.0.7/ScriptAssemblies'
    reference_files=list(sorted(refs.glob('*.dll')))
    reference_files+=list(sorted((SCRIPTING/'NetStandard/compat/2.1.0/shims/netfx').glob('*.dll')))
    reference_files+=list(sorted(managed.glob('*.dll')))
    reference_files+=[packages/'BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll',
                      cache/'UnityEngine.TestRunner.dll',cache/'UnityEditor.TestRunner.dll']
    if not all(p.is_file() for p in reference_files):
        raise FileNotFoundError('Installed type-check references incomplete.')
    base=[str(SCRIPTING/'NetCoreRuntime/dotnet'),str(SCRIPTING/'DotNetSdkRoslyn/csc.dll'),
          '-nologo','-noconfig','-nostdlib+','-langversion:8.0','-deterministic+','-target:library']
    base+=['-reference:'+str(p) for p in reference_files]
    groups=[('Resq.Core',list(sorted((ROOT/'game/Assets/Resq/Runtime/Core').glob('*.cs'))),[]),
            ('Resq.Tests.Shared',list(sorted((ROOT/'game/Assets/Resq/Tests/Shared').glob('*.cs'))),['Resq.Core']),
            ('Resq.Editor.Automation',list(sorted((ROOT/'game/Assets/Resq/Editor/Automation').glob('*.cs'))),[]),
            ('Resq.Tests',list(sorted((ROOT/'game/Assets/Resq/Tests/EditMode').glob('*.cs'))),['Resq.Core','Resq.Tests.Shared','Resq.Editor.Automation']),
            ('Resq.Art.Calibration.Editor',list(sorted((ROOT/'game/Assets/Resq/Art/Calibration/Editor').glob('*.cs'))),[]),
            ('Resq.Probe',list(sorted((ROOT/'game/Assets/Resq/Runtime/Probe').glob('*.cs'))),[]),
            ('Resq.Probe.Unity',list(sorted((ROOT/'game/Assets/Resq/Runtime/Probe/Unity').glob('*.cs'))),['Resq.Probe']),
            ('Resq.Probe.Checks',list(sorted((ROOT/'game/Assets/Resq/Tests/Probe/Shared').glob('*.cs'))),['Resq.Probe']),
            ('Resq.Probe.Tests',list(sorted((ROOT/'game/Assets/Resq/Tests/Probe/EditMode').glob('*.cs'))),['Resq.Probe','Resq.Probe.Checks'])]
    if any(not sources for _,sources,_ in groups):
        raise ValueError('Missing source group.')
    owner={'task':'F03-F06-source-typecheck','pid':os.getpid()}
    results=[]
    with resource_locks(ROOT/'automation/locks',['heavy-job'],owner):
        output.mkdir(parents=True)
        for name,sources,dependencies in groups:
            command=base+['-out:'+str(output/(name+'.dll'))]+['-reference:'+str(output/(dep+'.dll')) for dep in dependencies]+[str(p) for p in sources]
            process=run_process(command,cwd=ROOT,stdout_path=output/(name+'.stdout.txt'),stderr_path=output/(name+'.stderr.txt'),
                allowed_commands=[command],timeout_seconds=90,owner=owner,process_record_path=output/(name+'.process.json'),outcome_path=output/(name+'.outcome.json'),monitored_volumes=[ROOT])
            if process['status']!='passed' or process['exit_code']!=0:
                raise RuntimeError('Actual type-check failed; preserved logs: '+name)
            results.append({'assembly':name,'process':process,'sources':[{'path':str(p.relative_to(ROOT)),'sha256':sha256_file(p)} for p in sources],
                'output':{'path':str((output/(name+'.dll')).relative_to(ROOT)),'sha256':sha256_file(output/(name+'.dll'))}})
        write_json_exclusive(output/'verification.json',{'kind':'installed_reference_csharp_typecheck','status':'passed','assemblies':results,
            'references':[{'path':str(p),'sha256':sha256_file(p)} for p in reference_files],
            'editor_execution':False,'f03_accepted':False,'f06_accepted':False,
            'limits':['Type-check only with installed reference modules and bundled template TestRunner assemblies; not Unity assembly import/package resolution.',
                      'No licensed Editor test, native Unity render, Android build or physical proof.']})
    print(json.dumps({'status':'passed','typechecked_assemblies':len(results),'editor_execution':False}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    run(parser.parse_args().output)
