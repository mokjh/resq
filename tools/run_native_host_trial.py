#!/usr/bin/env python3
"""One bounded, real Editor host diagnostic. Never accepts a task or changes gates."""
import os
from pathlib import Path
import shutil
import sys
import time

from runner_core import resource_locks, run_process, write_json_exclusive, sha256_file, utc_now, GIB

ROOT = Path(__file__).resolve().parents[1]
EDITOR = Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity')
LIMIT = 512 * 1024**2
BUFFER = 128 * 1024**2


def inventory(roots):
    found = {}
    for root in roots:
        if not root.exists(): continue
        for directory, dirs, files in os.walk(root, followlinks=False):
            dirs[:] = [n for n in dirs if not (Path(directory)/n).is_symlink()]
            for name in files:
                path = Path(directory)/name
                try:
                    if not path.is_symlink(): found[str(path)] = path.stat().st_size
                except FileNotFoundError: pass
    return found


def run(directory, reuse_cache=None):
    directory = directory.resolve()
    if directory.exists() or not directory.is_relative_to(ROOT/'evidence/foundation/native-host-20261016'):
        raise ValueError('Fresh native-host evidence destination required')
    project = ROOT/'game'
    if (project/'Temp/UnityLockfile').exists(): raise RuntimeError('Project already locked; no takeover')
    required = 5*GIB + LIMIT + BUFFER
    if shutil.disk_usage(ROOT).free < required: raise RuntimeError('Bounded native trial reserve unavailable')
    owner = {'task':'F03-bounded-native-diagnostic','pid':os.getpid(),'cwd':str(ROOT)}
    directory.mkdir(parents=True)
    cache = Path(reuse_cache).resolve() if reuse_cache else directory/'upm-cache'
    if reuse_cache:
        if not cache.is_relative_to(ROOT/'evidence/foundation/native-host-20261016') or cache.name!='upm-cache' or not cache.is_dir():
            raise ValueError('Only a retained task-owned native-host cache may be reused')
    else: cache.mkdir()
    before_manifest = (project/'Packages/manifest.json').read_bytes()
    write_json_exclusive(directory/'admission.json', {
        'observed_utc':utc_now(),'kind':'bounded_native_host_diagnostic_not_task_acceptance',
        'engineering_decision':'User requested actual needed Unity work; narrowly bounded native host measurement replaces blanket40GiB stopping for this diagnostic only.',
        'normal_unity_floor_unchanged_bytes':40*GIB,'required_bytes':required,
        'free_bytes':shutil.disk_usage(ROOT).free,'growth_limit_bytes':LIMIT,'overshoot_buffer_bytes':BUFFER,
        'source_project':str(project),'private_upm_cache':str(cache),
        'editor':{'path':str(EDITOR),'sha256':sha256_file(EDITOR)},
        'no_module_or_mcp_install':True,'no_device_or_release_action':True,
        'acceptance':'Actual native XML/cases only; diagnostic cannot accept F03 or pass generic working_storage.'})
    (directory/'manifest-before.json').write_bytes(before_manifest)
    watched = (project,directory) if not reuse_cache else (project,directory,cache)
    before = inventory(watched)
    measurements = {'peak_growth_bytes':0,'lowest_free_bytes':shutil.disk_usage(ROOT).free}
    checked_at = 0.0
    def stop_check():
        nonlocal checked_at
        if time.monotonic()-checked_at < 0.2: return None
        checked_at = time.monotonic()
        current = inventory(watched)
        growth = sum(max(0,size-before.get(path,0)) for path,size in current.items())
        free = shutil.disk_usage(ROOT).free
        measurements['peak_growth_bytes'] = max(measurements['peak_growth_bytes'],growth)
        measurements['lowest_free_bytes'] = min(measurements['lowest_free_bytes'],free)
        if growth >= LIMIT: return 'bounded native host growth limit reached'
        return None
    command = ['/usr/bin/env','UPM_CACHE_ROOT='+str(cache),'UPM_MAX_CACHE_SIZE=268435456',
        str(EDITOR),'-batchmode','-nographics','-projectPath',str(project),
        '-refreshImportMode','InProcess','-job-worker-count','2',
        '-logFile',str(directory/'editor.log'),'-upmLogFile',str(directory/'upm.log'),
        '-executeMethod','Resq.Editor.Automation.EntryPoint.RunEditModeTests',
        '-resqTestCategory','host-bootstrap','-resqTestResults',str(directory/'nunit.xml'),
        '-resqCaseEvidence',str(directory/'unity-cases.json'),'-resqOwnerPid',str(os.getpid())]
    with resource_locks(ROOT/'automation/locks',['heavy-job','unity-project'],owner):
        process = run_process(command,cwd=ROOT,stdout_path=directory/'stdout.txt',stderr_path=directory/'stderr.txt',
            allowed_commands=[command],timeout_seconds=360,owner=owner,
            process_record_path=directory/'execution.json',outcome_path=directory/'outcome.json',
            monitored_volumes=[ROOT,Path('/tmp')],stop_check=stop_check)
    stop_check()
    write_json_exclusive(directory/'measurements.json',dict(measurements,
        observed_utc=utc_now(),editor_executed=True,task_accepted=False,
        native_xml_exists=(directory/'nunit.xml').is_file(),native_cases_exist=(directory/'unity-cases.json').is_file(),
        manifest_unchanged=(project/'Packages/manifest.json').read_bytes()==before_manifest))
    print({'status':process['status'],'exit_code':process['exit_code'],'stop_reason':process['stop_reason'],
           'native_xml_exists':(directory/'nunit.xml').is_file(),'peak_growth_bytes':measurements['peak_growth_bytes']})


if __name__=='__main__': run(Path(sys.argv[1]),sys.argv[2] if len(sys.argv)>2 else None)
