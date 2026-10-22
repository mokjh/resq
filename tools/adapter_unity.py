"""Real batch-Editor dispatch; unavailable project/estimates/case emitters block."""
from pathlib import Path
import json
import sys
import tempfile
from bootstrap_doctor import ROOT,read,digest,relative,write_once
from runner_core import check_storage,run_process,parse_nunit_xml,parse_build_report,crosscheck_status
from task_execution import Blocked


def editor_for_version(version):
    return Path('/Applications/Unity')/('Unity-'+str(version))/'Unity.app/Contents/MacOS/Unity'


def verify_runtime_inputs(identity):
    """Bind live inputs to the shared task snapshot across sequential checks."""
    source_manifest = ROOT/identity['source_manifest']['path']
    manifest=read(source_manifest)
    prefix=str((source_manifest.parent/'source').relative_to(ROOT))+'/'
    for item in manifest['files']:
        if not item['path'].startswith(prefix):raise RuntimeError('Runtime source snapshot prefix mismatch')
        live=ROOT/item['path'][len(prefix):]
        if not live.is_file() or digest(live)!=item['sha256']:
            raise RuntimeError('Runtime input changed during native execution: '+str(live.relative_to(ROOT)))


def execute(argv,context):
    project=ROOT/'game';version_file=project/'ProjectSettings/ProjectVersion.txt'
    if not version_file.is_file():raise Blocked('UNITY_PROJECT_NOT_CREATED: F03 host project is required')
    estimates=ROOT/'automation/resource_estimates.json'
    if not estimates.is_file():raise Blocked('RESOURCE_ESTIMATE_MISSING: record peak bytes before Unity admission')
    peak=read(estimates).get(context['task']['id'],{}).get('peak_bytes')
    volumes=[project,Path(tempfile.gettempdir())]
    host_only=context['task']['id']=='F03' and argv[1]=='test' and '--suite' in argv and argv[argv.index('--suite')+1]=='host-bootstrap'
    local_stage = ('runtime-content' if context['task']['id']=='C01' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-content'
                   else 'runtime-commands' if context['task']['id']=='C02' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-commands'
                   else 'runtime-water' if context['task']['id']=='C03' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-water'
                   else 'runtime-transport' if context['task']['id']=='C04' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-transport'
                   else 'runtime-equipment' if context['task']['id']=='C05' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-equipment'
                   else 'runtime-objectives' if context['task']['id']=='C06' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1] in ('runtime-objectives','runtime-units')
                   else 'runtime-persistence' if context['task']['id']=='C07' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1]=='runtime-persistence'
                   else 'runtime-forecast' if context['task']['id']=='C08' and argv[1]=='test'
                   and '--suite' in argv and argv[argv.index('--suite')+1] in ('runtime-forecast','runtime-ordering')
                   else 'android-probe-build' if context['task']['id']=='F04' and argv[1]=='build' else None)
    if host_only:
        from run_f03_bounded import preflight
        storage=preflight(project,peak)
    elif local_stage:
        from run_unity_bounded import preflight, STAGES
        if type(peak) is not int or peak!=STAGES[local_stage]['growth']:
            raise Blocked('LOCAL_RESOURCE_PROFILE_ESTIMATE_MISMATCH')
        storage=preflight(local_stage)
    else:storage=check_storage('unity',volumes,peak_bytes=peak)
    write_once(context['run_dir']/'operation-storage.json',storage)
    if storage['status']!='passed':
        raise Blocked('WORKING_STORAGE_BELOW_FLOOR: fresh output/temp observation blocks Editor launch; see operation-storage.json')
    version=next((line.split(':',1)[1].strip() for line in version_file.read_text().splitlines() if line.startswith('m_EditorVersion:')),None)
    editor=editor_for_version(version)
    if not editor.is_file():raise Blocked('EXACT_EDITOR_NOT_INSTALLED: '+str(version))
    if (project/'Temp/UnityLockfile').exists():raise Blocked('UNITY_PROJECT_ALREADY_OPEN: use its supported explicit connection; do not seize it')
    directory=context['run_dir'];packet_file=directory/'unity-cases.json'
    command=[str(editor),'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(directory/'editor.log'),
      '-resqCaseEvidence',str(packet_file),'-resqOwnerPid',str(context['owner']['pid'])]
    native=None
    if argv[1]=='test':
        output=directory/'nunit.xml';method='RunEditModeTests'
        command+=['-executeMethod','Resq.Editor.Automation.EntryPoint.'+method,'-resqTestResults',str(output)]
        if '--suite' in argv:command+=['-resqTestCategory',argv[argv.index('--suite')+1]]
    elif argv[1]=='build':
        output=directory/'build-report.json';apk=directory/'resq.apk'
        command+=['-executeMethod','Resq.Editor.Automation.EntryPoint.BuildAndroid','-resqBuildReport',str(output),'-resqApk',str(apk)]
    else:raise Blocked('UNITY_OPERATION_NOT_IMPLEMENTED: '+argv[1])
    executable=editor.resolve(strict=True)
    context['launched_editor']={'path':str(executable),'sha256':digest(executable),'byte_length':executable.stat().st_size}
    write_once(directory/'editor-executable.json',context['launched_editor'])
    context['backend_started']=True
    if host_only:
        from run_f03_bounded import execute as execute_host
        # Existing coordinator owns both locks; preserve its PID in native tests.
        arguments=command[1:]
        for flag in ('-batchmode','-nographics'):arguments.remove(flag)
        at=arguments.index('-logFile');del arguments[at:at+2]
        process=execute_host(directory/'native-host',arguments,
            [project,directory,ROOT/'evidence/foundation/f03-bounded-20261016/upm-cache'],already_locked=True)
        if process.get('stop_reason'):raise Blocked('F03_HOST_RESOURCE_CANCELLED: '+process['stop_reason'])
    elif local_stage:
        from run_unity_bounded import execute as execute_local
        arguments=command[1:]
        for flag in ('-batchmode','-nographics'):arguments.remove(flag)
        for flag in ('-logFile','-projectPath'):
            at=arguments.index(flag);del arguments[at:at+2]
        try:
            process=execute_local(local_stage,directory/'native-local',arguments,already_locked=True)
        except RuntimeError as error:
            if str(error).startswith('BOUNDED_ADMISSION_BLOCKED:') or str(error).startswith('Existing Unity project lock;'):
                raise Blocked(str(error)) from error
            raise
        if process.get('stop_reason'):raise Blocked('LOCAL_RESOURCE_CANCELLED: '+process['stop_reason'])
    else:
        process=run_process(command,cwd=ROOT,stdout_path=directory/'editor.stdout.txt',stderr_path=directory/'editor.stderr.txt',
          allowed_commands=[command],timeout_seconds=3600,owner=context['owner'],process_record_path=directory/'process.json',
          outcome_path=directory/'process-outcome.json',monitored_volumes=volumes)
    if not output.is_file():raise RuntimeError('Actual Editor emitted no native report')
    native=parse_nunit_xml(output) if argv[1]=='test' else parse_build_report(output,artifact_root=directory)
    crosscheck_status(process,native,reported_status=native['status'])
    if process['status']!='passed' or native['status']!='passed':raise RuntimeError('Unity process/native report failed')
    if local_stage in ('runtime-content','runtime-commands','runtime-water','runtime-transport','runtime-equipment','runtime-objectives','runtime-persistence','runtime-forecast'):
        # Tests consume runtime fixtures/schemas as well as C# source. They must
        # match the immutable inputs captured before this actual Editor launch.
        verify_runtime_inputs(context['identity'])
    if not packet_file.is_file():raise RuntimeError('MISSING_NATIVE_CASE_PACKET: implement suite-specific case emitter; XML alone cannot fill frozen cases')
    packet=read(packet_file)
    if packet.get('status')!='passed':raise RuntimeError('Unity case packet did not pass')
    if argv[1]=='build':
        from native_build_cases import validate_native_build_cases
        validate_native_build_cases(packet,directory,native)
        from build_candidate import bind_candidate
        bind_candidate(context,packet)
    if argv[1]=='test':
        executed={c['case_id'] for c in native['cases'] if c['status']=='passed'}
        if not {c['case_id'] for c in packet['case_outcomes']}<=executed:raise RuntimeError('Case packet claims unexecuted NUnit cases')
    if local_stage=='runtime-content':
        from bootstrap_doctor import artifact
        audit_path=ROOT/'evidence/content/compiler.json'
        # This is the current working audit; per-attempt result artifacts retain
        # immutable copies. Preserve a prior working audit before replacing it.
        if audit_path.exists():
            write_once(directory/'prior-compiler-audit.json',audit_path.read_bytes())
            audit_path.rename(directory/'prior-compiler-audit-original.json')
        write_once(audit_path,{
            'schema_version':1,'evidence_kind':'automated','accepted_task':False,
            'status':'actual_native_content_tests_passed',
            'native_case_ids':sorted(executed),
            'native_xml':artifact(output,'actual_native_nunit_xml'),
            'native_cases':artifact(packet_file,'native_case_packet'),
            'process':artifact(directory/'native-local/execution.json','actual_editor_process'),
            'process_outcome':artifact(directory/'native-local/outcome.json','actual_editor_outcome'),
            'source_manifest':context['identity']['source_manifest'],
            'source_inputs_unchanged_during_execution':True,
            'scope':'Immutable typed content/schema/compiler validation; synthetic structural fixtures and all25 mission wire definitions.',
            'limits':['No authored25mission geometry, successful rescue replays, playable game, physical device or human acceptance inferred.']})
    return packet['case_outcomes']
