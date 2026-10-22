"""Retain exact approved SDK archives with bounded disk use; never extract/install."""
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import urllib.request

from runner_core import resource_locks, write_json_exclusive, utc_now
from run_unity_bounded import ROOT, owned, preflight

CANDIDATE = ROOT/'evidence/toolchain/preparation/20261015-222738Z-registry-resolution/candidate-dependency-set.json'
MEASURED = ROOT/'evidence/resource-reassessment-20261016/meta-archive-measurement/summary.json'
ARCHIVES = ROOT/'evidence/toolchain/approved-meta-207.0.1'
CHUNK = 1024**2


def check_archive(path, package, measured):
    sha1, sha512 = hashlib.sha1(), hashlib.sha512()
    count = 0
    with path.open('rb') as stream:
        while chunk := stream.read(CHUNK):
            count += len(chunk); sha1.update(chunk); sha512.update(chunk)
    integrity = 'sha512-'+base64.b64encode(sha512.digest()).decode()
    if (count != measured['compressed_bytes_read'] or
        sha1.hexdigest() != package['published_dist']['shasum'] or
        integrity != package['published_dist']['integrity']):
        raise ValueError('Exact approved archive bytes mismatch: '+package['name'])
    return {'path':str(path.relative_to(ROOT)), 'byte_length':count,
            'sha1':sha1.hexdigest(), 'integrity':integrity, 'verified':True}


def fetch(directory):
    directory=owned(directory)
    if not directory.is_relative_to(ROOT/'evidence') or directory.exists():
        raise ValueError('Fresh owned receipt directory required')
    owned(ARCHIVES)
    packages=[p for p in json.loads(CANDIDATE.read_text())['packages'] if p['name'].startswith('com.meta.')]
    measured={p['name']:p for p in json.loads(MEASURED.read_text())['archives']}
    if len(packages)!=4 or len(measured)!=4 or any(p['version']!='207.0.1' for p in packages):
        raise ValueError('Exact four approved measured packages required')
    pending=0; targets=[]
    for package in packages:
        name=package['name']; dist=package['published_dist']; row=measured[name]
        url=dist['tarball']
        if url != row['url'] or not url.startswith('https://npm.developer.oculus.com/'+name+'/-/'):
            raise ValueError('Exact approved official URL required')
        if row['sha1']!=dist['shasum'] or row['integrity']!=dist['integrity']:
            raise ValueError('Measurement integrity binding mismatch')
        path=owned(ARCHIVES/(name+'-207.0.1.tgz'))
        if path.exists():check_archive(path,package,row)
        else:pending+=row['compressed_bytes_read']
        targets.append((package,row,path))
    admission=preflight('xr-packages')
    required=admission['required_free_bytes']+pending+16*CHUNK
    admission.update(operation='approved_archive_retention', pending_archive_bytes=pending,
                     required_free_bytes=required, observed_utc=utc_now(), task_accepted=False)
    admission['status']='passed' if admission['memory_pressure_level']<4 and all(v['free_bytes']>=required for v in admission['volumes']) else 'blocked'
    directory.mkdir(parents=True)
    write_json_exclusive(directory/'admission.json',admission)
    if admission['status']!='passed':
        write_json_exclusive(directory/'outcome.json',{'status':'blocked','reason':'Reserve, complete archive bytes and subsequent XR stage budget unavailable','installed':False,'task_accepted':False})
        return 75
    ARCHIVES.mkdir(parents=True,exist_ok=True)
    results=[]; added=0
    for package,row,path in targets:
        reused=path.exists()
        if not reused:
            # Failed partials remain in this attempt's receipt folder, never replace
            # a verified archive or another attempt's partial.
            partial=directory/(package['name']+'.part')
            with urllib.request.urlopen(row['url'],timeout=30) as response, partial.open('xb') as stream:
                if not response.geturl().startswith('https://npm.developer.oculus.com/'):
                    raise ValueError('Unexpected archive redirect')
                while chunk:=response.read(CHUNK):
                    if added+len(chunk)>pending:
                        raise ValueError('Archive download exceeded measured total')
                    # Retain the full native-stage admission while streaming.
                    if shutil.disk_usage(ROOT).free < admission['required_free_bytes']-pending+len(chunk):
                        raise RuntimeError('Archive retention stopped: native-stage reserve unavailable')
                    stream.write(chunk);added+=len(chunk)
            check_archive(partial,package,row)
            # Both endpoints owned; exclusive hard link avoids replacing targets.
            os.link(partial,path);partial.unlink()
        result=dict(check_archive(path,package,row),name=package['name'],version=package['version'],reused=reused)
        write_json_exclusive(directory/(package['name']+'.json'),result);results.append(result)
    write_json_exclusive(directory/'outcome.json',{'status':'passed','archives':results,'added_bytes':added,
        'installed':False,'extracted':False,'task_accepted':False,'limits':['Verified package inputs only; no native resolution, import, build or task acceptance.']})
    return 0


if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--receipt-dir',type=Path,required=True)
    args=parser.parse_args()
    with resource_locks(ROOT/'automation/locks',('unity-project','heavy-job'),
            {'task':'F04-approved-archive-inputs','pid':os.getpid(),'cwd':str(ROOT)}):
        raise SystemExit(fetch(args.receipt_dir))
