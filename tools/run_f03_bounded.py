"""User-directed F03 host-only measurements. Not Android/render/device admission."""
import os
import json
import hashlib
from pathlib import Path
import shutil
import time

from runner_core import GIB, resource_locks, run_process, utc_now, write_json_exclusive, sha256_file

ROOT = Path(__file__).resolve().parents[1]
EDITOR = Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity')
RESERVE = 2 * GIB
LIMIT = 64 * 1024**2
BUFFER = 64 * 1024**2


def preflight(project, peak_bytes):
    """A separate F03 admission; never substitutes for general working_storage."""
    profile=json.loads((ROOT/'automation/resource_profiles.json').read_text())['profiles'][0]
    if profile['task_id']!='F03' or profile['reserve_bytes']!=RESERVE or profile['growth_limit_bytes']!=LIMIT or profile['overshoot_buffer_bytes']!=BUFFER:
        raise ValueError('F03 host profile/code mismatch')
    if type(peak_bytes) is not int or peak_bytes!=LIMIT:
        raise ValueError('F03 requires its explicit measured/enforced peak bound')
    dependencies=json.loads((project/'Packages/manifest.json').read_text())['dependencies']
    expected=profile['exact_packages']
    if any(dependencies.get(name)!=version for name,version in expected.items()):
        raise ValueError('F03 exact host package set missing')
    for name,version in dependencies.items():
        if name not in expected and not (name.startswith(profile['permitted_builtin_prefix']) and version==profile['builtin_version']):
            raise ValueError('Non-host package refused: '+name)
    assets=sizes([project/'Assets'])
    if sum(assets.values())>profile['max_assets_bytes']:
        raise ValueError('F03 host asset envelope exceeded')
    required=RESERVE+LIMIT+BUFFER
    volumes=[{'path':str(p),'free_bytes':shutil.disk_usage(p).free} for p in (project,Path('/tmp'))]
    return {'profile':profile['id'],'task_id':'F03','status':'passed' if all(v['free_bytes']>=required for v in volumes) else 'blocked',
        'required_free_bytes':required,'reserve_bytes':RESERVE,'positive_growth_limit_bytes':LIMIT,
        'overshoot_buffer_bytes':BUFFER,'volumes':volumes,'assets_bytes':sum(assets.values()),
        'manifest_sha256':sha256_file(project/'Packages/manifest.json'),'observed_utc':utc_now(),
        'general_Unity_Android_render_gate_passed':False}


def sizes(roots):
    result = {}
    for root in roots:
        for directory, dirs, files in os.walk(root, followlinks=False):
            dirs[:] = [n for n in dirs if not (Path(directory)/n).is_symlink()]
            for name in files:
                p = Path(directory)/name
                try:
                    if not p.is_symlink(): result[str(p)] = p.stat().st_size
                except FileNotFoundError: pass
    return result


def execute(directory, arguments, watched, *, limit=LIMIT, owned_locks=('heavy-job',), already_locked=False):
    directory = Path(directory).resolve()
    if directory.exists() or not directory.is_relative_to(ROOT/'evidence'):
        raise ValueError('Fresh owned evidence destination required')
    volumes = [ROOT, Path('/tmp')]
    required = RESERVE + limit + BUFFER
    if any(shutil.disk_usage(p).free < required for p in volumes):
        raise RuntimeError('F03_HOST_BOUND_UNAVAILABLE: needs2GiBreserve + explicitgrowthbound +64MiBovershoot')
    directory.mkdir(parents=True)
    owner = {'task':'F03-bounded-host-preparation','pid':os.getpid(),'cwd':str(ROOT)}
    roots = tuple(Path(p) for p in watched)+(directory,)
    initial = sizes(roots)
    measurement = {'peak_positive_growth_bytes':0, 'lowest_free_bytes':min(shutil.disk_usage(p).free for p in volumes)}
    write_json_exclusive(directory/'admission.json', {
        'observed_utc':utc_now(),'authority':'Direct user16October2026: reassessF03reserve and use safelyboundedworkflow',
        'scope':'F03host-only; noAndroidmodules/build/render/device; no task acceptance from preparation',
        'reserve_bytes':RESERVE,'growth_limit_bytes':limit,'overshoot_buffer_bytes':BUFFER,
        'required_free_bytes':required,'free_bytes':[shutil.disk_usage(p).free for p in volumes],
        'editor_sha256':sha256_file(EDITOR),
        'watched_roots':[str(p) for p in roots]})
    def guard():
        free = min(shutil.disk_usage(p).free for p in volumes)
        measurement['lowest_free_bytes'] = min(measurement['lowest_free_bytes'],free)
        current = sizes(roots)
        growth = sum(max(0,size-initial.get(p,0)) for p,size in current.items())
        measurement['peak_positive_growth_bytes'] = max(measurement['peak_positive_growth_bytes'],growth)
        if free < RESERVE: return 'F03 host volume below2GiB reserve'
        if growth >= limit: return 'F03 host positive growth bound reached'
        return None
    cache=ROOT/'evidence/foundation/f03-bounded-20261016/upm-cache'
    cache.mkdir(exist_ok=True)
    command = ['/usr/bin/env','UPM_CACHE_ROOT='+str(cache),'UPM_MAX_CACHE_SIZE=67108864',
               str(EDITOR),'-batchmode','-nographics','-refreshImportMode','InProcess',
               '-job-worker-count','2','-logFile',str(directory/'editor.log')]+list(arguments)
    def launch():
        return run_process(command,cwd=ROOT,stdout_path=directory/'stdout.txt',stderr_path=directory/'stderr.txt',
            allowed_commands=[command],timeout_seconds=360,owner=owner,
            process_record_path=directory/'execution.json',outcome_path=directory/'outcome.json',
            monitored_volumes=[],stop_check=guard)
    if already_locked:
        for resource in ('heavy-job','unity-project'):
            key=hashlib.sha256(resource.encode()).hexdigest()+'.lock.json'
            record=json.loads((ROOT/'automation/locks'/key).read_text())
            if record['resource']!=resource or record['pid']!=os.getpid() or record['cwd']!=str(ROOT):
                raise RuntimeError('F03 native coordinator lock mismatch')
        outcome=launch()
    else:
        with resource_locks(ROOT/'automation/locks',owned_locks,owner): outcome=launch()
    guard()
    write_json_exclusive(directory/'measurements.json',dict(measurement,observed_utc=utc_now(),task_accepted=False))
    return outcome


if __name__=='__main__':
    base=ROOT/'evidence/foundation/f03-bounded-20261016'
    project=base/'bootstrap-project'
    if project.exists(): raise RuntimeError('Creation destination exists')
    result=execute(base/'create-empty', ['-createProject',str(project),'-noUpm','-quit'],[base])
    print({k:result[k] for k in ('status','exit_code','stop_reason')})
