"""Scoped, cancellable RESQ native work. A process receipt never accepts a task.

The user authorized the F04/F06 reserve reassessment and C01 continuation.
Budgets below bound attempts; they do not pretend unknown build peaks are known.
F03's separate qualified host route remains unchanged.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
import uuid
import subprocess
import time

from runner_core import GIB, StorageBlocked, resource_locks, run_process, utc_now, write_json_exclusive, sha256_file
from run_f03_bounded import sizes, EDITOR, ROOT

MIB = 1024**2
RESERVE = 2 * GIB
PROFILE_PATH = ROOT/'automation/local_operation_profiles.json'
PROFILE = json.loads(PROFILE_PATH.read_text())
if PROFILE['reserve_bytes'] != RESERVE:
    raise ValueError('Local profile hard reserve mismatch')
STAGES = PROFILE['stages']
RUNTIME_PROFILE_PATH = ROOT/'automation/runtime_operation_profiles.json'
RUNTIME_PROFILE = json.loads(RUNTIME_PROFILE_PATH.read_text())
if RUNTIME_PROFILE['reserve_bytes'] != RESERVE or set(STAGES) & set(RUNTIME_PROFILE['stages']):
    raise ValueError('Runtime profile reserve or stage ownership mismatch')
STAGES = dict(STAGES, **RUNTIME_PROFILE['stages'])


def memory_pressure_level():
    """Read-only macOS signal: 1 normal, 2 warning, 4 critical."""
    result=subprocess.run(['/usr/sbin/sysctl','-n','kern.memorystatus_vm_pressure_level'],
                          capture_output=True,text=True,timeout=1)
    if result.returncode or result.stdout.strip() not in ('1','2','4'):
        raise RuntimeError('Native memory-pressure observation unavailable')
    return int(result.stdout.strip())


def owned(path):
    value = Path(path).absolute()
    if not value.is_relative_to(ROOT) or '..' in value.parts:
        raise ValueError('Only RESQ-owned paths are admitted')
    current = ROOT
    for component in value.relative_to(ROOT).parts:
        current /= component
        if current.is_symlink():
            raise ValueError('Linked owned path refused: '+str(current))
    return value


def preflight(stage):
    profile = STAGES[stage]
    required = RESERVE + profile['growth'] + profile['buffer']
    volumes = [{'path': str(p), 'free_bytes': shutil.disk_usage(p).free}
               for p in (ROOT/'game', Path(tempfile.gettempdir()))]
    pressure=memory_pressure_level()
    return dict(profile, stage=stage, observed_utc=utc_now(), reserve_bytes=RESERVE,
                resource_profile_sha256=sha256_file(RUNTIME_PROFILE_PATH if stage in RUNTIME_PROFILE['stages'] else PROFILE_PATH),
                required_free_bytes=required, volumes=volumes,
                memory_pressure_level=pressure,
                status='passed' if pressure<4 and all(v['free_bytes'] >= required for v in volumes) else 'blocked',
                task_accepted=False, full_peak_known=False,
                authority=(RUNTIME_PROFILE if stage in RUNTIME_PROFILE['stages'] else PROFILE)['authority'])


def _with_final_measurements(launch, directory, measurements, guard):
    """Retain sampled resource facts even when process execution/finalization fails."""
    launch_failed = False
    process = None
    try:
        process = launch()
        return process
    except BaseException:
        launch_failed = True
        raise
    finally:
        final_reason = None
        observation_error = None
        try:
            final_reason = guard()
        except Exception as error:
            observation_error = type(error).__name__ + ": " + str(error)
        write_json_exclusive(directory/'measurements.json', dict(measurements,
            observed_utc=utc_now(), task_accepted=False, full_peak_known=False,
            final_guard_reason=final_reason,
            final_observation_error=observation_error))
        # Keep an in-flight primary exception; a failed final observation must
        # also fail a previously successful call after its receipt is retained.
        if observation_error is not None and not launch_failed:
            raise RuntimeError('Final resource observation failed: ' + observation_error)
        if final_reason is not None and not launch_failed:
            # Preserve an already observed resource stop so the adapter can
            # classify it as blocked. The actual failed process receipt remains
            # unchanged; a post-exit limit must also never qualify a success.
            if not isinstance(process, dict) or not process.get('stop_reason'):
                raise StorageBlocked('Final resource guard failed: ' + final_reason)


def execute(stage, directory, arguments, *, already_locked=False, setup_project=None):
    profile = STAGES[stage]
    directory = owned(directory)
    if not directory.is_relative_to(ROOT/'evidence') or directory.exists():
        raise ValueError('Fresh owned evidence directory required')
    admission = preflight(stage)
    if admission['status'] != 'passed':
        # Preserve the fresh denial without starting Unity or creating caches.
        directory.mkdir(parents=True)
        write_json_exclusive(directory/'admission.json', admission)
        write_json_exclusive(directory/'outcome.json', {
            'status':'blocked','process_started':False,'task_accepted':False,
            'reason':'Stage reserve + growth budget + cancellation margin unavailable'})
        raise RuntimeError('BOUNDED_ADMISSION_BLOCKED: '+json.dumps(admission))
    project = ROOT/'game'
    if setup_project is not None:
        expected = ROOT/'evidence/foundation/f03-bounded-20261016/bootstrap-project'
        if stage not in ('render-packages','xr-packages','android-tools-inspect','android-probe-build') or owned(setup_project) != expected:
            raise ValueError('Only the retained RESQ bootstrap project may prepare packages or an isolated Android smoke build')
        project = expected
    if (project/'Temp/UnityLockfile').exists():
        raise RuntimeError('Existing Unity project lock; inspect ownership before any launch')
    arguments = list(arguments)
    forbidden = {'-projectPath','-createProject','-logFile','-batchmode','-nographics'}
    if any(arg in forbidden for arg in arguments):
        raise ValueError('Project/graphics/log ownership flags are fixed by this runner')
    directory.mkdir(parents=True)
    cache = ROOT/'evidence/resource-reassessment-20261016/upm-cache'
    # Bee creates Unix sockets beneath TMPDIR. macOS limits socket paths to
    # ~104 bytes, so the long evidence path is not a usable temp directory.
    temporary = ROOT/'game/Temp'/('rb-'+uuid.uuid4().hex[:12])
    if len(os.fsencode(temporary)) > 64:
        raise RuntimeError('Owned TMPDIR too long for native Bee socket suffix')
    cache.mkdir(parents=True, exist_ok=True)
    temporary.mkdir(parents=True)
    roots = (project, directory, cache, temporary)
    initial = sizes(roots)
    measurements = {'peak_positive_growth_bytes': 0,
                    'lowest_free_bytes': min(v['free_bytes'] for v in admission['volumes']),
                    'highest_memory_pressure_level':admission['memory_pressure_level']}
    pressure_state={'next_observation':0.0,'critical_since':None}
    write_json_exclusive(directory/'admission.json', dict(admission,
        editor_sha256=sha256_file(EDITOR), watched_roots=[str(p) for p in roots]))

    def guard():
        current_time=time.monotonic()
        if current_time>=pressure_state['next_observation']:
            pressure=memory_pressure_level()
            measurements['highest_memory_pressure_level']=max(measurements['highest_memory_pressure_level'],pressure)
            pressure_state['next_observation']=current_time+1
            if pressure==4:
                if pressure_state['critical_since'] is None:pressure_state['critical_since']=current_time
                elif current_time-pressure_state['critical_since']>=5:
                    return stage+' sustained critical memory pressure'
            else:pressure_state['critical_since']=None
        free = min(shutil.disk_usage(v['path']).free for v in admission['volumes'])
        measurements['lowest_free_bytes'] = min(measurements['lowest_free_bytes'], free)
        growth = sum(max(0, size-initial.get(path, 0)) for path, size in sizes(roots).items())
        measurements['peak_positive_growth_bytes'] = max(measurements['peak_positive_growth_bytes'], growth)
        if free < RESERVE:
            return stage+' volume below2GiB hard reserve'
        if growth >= profile['growth']:
            return stage+' positive file growth budget reached'
        return None

    owner = {'task': profile['task']+'-'+stage, 'pid': os.getpid(), 'cwd': str(ROOT)}
    environment = ['UPM_CACHE_ROOT='+str(cache),
               'UPM_MAX_CACHE_SIZE='+str(256*MIB), 'TMPDIR='+str(temporary),
               'GRADLE_USER_HOME='+str(directory/'gradle-home')]
    # Process scope only: never use AndroidExternalToolsSettings setters, which
    # would persist preferences shared with other Unity projects.
    if stage in ('xr-packages','android-probe-build','android-tools-inspect'):
        sdk = Path('/Users/dev/Library/Android/sdk')
        ndk = sdk/'ndk/27.2.12479018'
        jdk = Path('/Library/Java/JavaVirtualMachines/openjdk-17.jdk/Contents/Home')
        if stage == 'android-probe-build':
            jdk = Path('/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home')
        for path in (sdk,ndk,jdk):
            if not path.is_dir(): raise RuntimeError('Existing Android tool root missing: '+str(path))
        environment += ['ANDROID_SDK_ROOT='+str(sdk),'ANDROID_HOME='+str(sdk),
            'ANDROID_NDK_ROOT='+str(ndk),'ANDROID_NDK_HOME='+str(ndk),'JAVA_HOME='+str(jdk)]
    if stage == 'android-probe-build':
        # The installed Android module resolves the default debug key from
        # ANDROID_SDK_HOME/.android. Keep both Unity and Android tooling in the
        # same owned run directory without replacing HOME or signing settings.
        android_user = directory/'android-user'
        environment += ['ANDROID_SDK_HOME='+str(android_user),
                        'ANDROID_USER_HOME='+str(android_user/'.android')]
    command = ['/usr/bin/env']+environment+[str(EDITOR), '-batchmode']
    if not profile['graphics']:
        command += ['-nographics']
    command += ['-refreshImportMode','InProcess','-job-worker-count','2',
                '-projectPath',str(project),'-logFile',str(directory/'editor.log')]+arguments

    def launch():
        return run_process(command, cwd=ROOT, stdout_path=directory/'stdout.txt',
            stderr_path=directory/'stderr.txt', allowed_commands=[command], timeout_seconds=1800,
            owner=owner, process_record_path=directory/'execution.json',
            outcome_path=directory/'outcome.json', monitored_volumes=[], stop_check=guard)

    def measured_launch():
        return _with_final_measurements(launch, directory, measurements, guard)

    if already_locked:
        for resource in ('unity-project','heavy-job'):
            key=hashlib.sha256(resource.encode()).hexdigest()+'.lock.json'
            record=json.loads((ROOT/'automation/locks'/key).read_text())
            if record['resource'] != resource or record['pid'] != os.getpid() or record['cwd'] != str(ROOT):
                raise RuntimeError('Native coordinator lock mismatch')
        outcome=measured_launch()
    else:
        with resource_locks(ROOT/'automation/locks',('unity-project','heavy-job'),owner):
            outcome=measured_launch()
    return outcome
