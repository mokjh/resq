#!/usr/bin/env python3
"""Copy installed Unity template source only; never import, install or accept F03."""
from pathlib import Path, PurePosixPath
import argparse
from datetime import datetime, timezone
import hashlib
import json
import tarfile

ROOT = Path(__file__).resolve().parents[1]
EDITOR = Path('/Applications/Unity/Unity-6000.3.24f1/Unity.app')
TEMPLATE = EDITOR/'Contents/Resources/PackageManager/ProjectTemplates/com.unity.template.3d-cross-platform-17.0.14.tgz'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(report):
    baseline=json.loads((ROOT/'specification/baseline_manifest.json').read_text())
    def original_hashes():
        return {a['path']:sha((ROOT/'Resq_Production_Pack'/a['path']).read_bytes()) for a in baseline['files']}
    before=original_hashes()
    assert all(before[a['path']]==a['sha256'] for a in baseline['files'])
    if (ROOT/'game/Temp/UnityLockfile').exists():
        raise RuntimeError('Existing Editor lock: no source merge attempted.')
    prefix='package/ProjectData~/'
    planned={}
    with tarfile.open(TEMPLATE, 'r:gz') as archive:
        for member in archive.getmembers():
            if not member.name.startswith(prefix):
                continue
            name=member.name[len(prefix):]
            parts=PurePosixPath(name).parts
            if not parts or '..' in parts or PurePosixPath(name).is_absolute():
                raise ValueError('Unsafe archive member')
            # Library caches and the template's old resolution are never copied.
            if parts[0] not in ('Assets','ProjectSettings','Packages') or name=='Packages/packages-lock.json':
                continue
            if member.isdir():
                continue
            if not member.isfile():
                raise ValueError('Non-regular template source')
            planned[ROOT/'game'/name]=archive.extractfile(member).read()
    planned[ROOT/'game/ProjectSettings/ProjectVersion.txt']=b'm_EditorVersion: 6000.3.24f1\n'
    if sum(map(len,planned.values()))>1024*1024:
        raise RuntimeError('Unexpected source expansion: review before writing.')
    for path,data in planned.items():
        if not path.resolve().is_relative_to((ROOT/'game').resolve()) or path.is_symlink():
            raise ValueError('Unsafe destination')
        if path.exists() and path.read_bytes()!=data:
            raise FileExistsError('Existing source differs: '+str(path))
    rows=[]
    for path,data in sorted(planned.items()):
        if not path.exists():
            path.parent.mkdir(parents=True,exist_ok=True)
            with path.open('xb') as stream:
                stream.write(data)
        rows.append({'path':str(path.relative_to(ROOT)),'bytes':len(data),'sha256':sha(data)})
    assert before==original_hashes()
    payload={'kind':'source_preparation_only','observed_utc':datetime.now(timezone.utc).isoformat(),
        'template':str(TEMPLATE),'template_sha256':sha(TEMPLATE.read_bytes()),
        'template_name':'com.unity.template.urp-blank','template_version':'17.0.14',
        'candidate_editor':'6000.3.24f1','files':rows,'source_bytes':sum(map(len,planned.values())),
        'original_files_preserved':len(before),'editor_launched':False,'packages_installed':False,
        'f03_accepted':False,'limits':['Installed template manifest preserved byte-for-byte; declarations are not resolved packages.',
        'No Library/cache copied, no import, no external template download, no package resolution or native Editor test.']}
    report.parent.mkdir(parents=True,exist_ok=True)
    with report.open('x') as stream:
        json.dump(payload,stream,indent=2);stream.write('\n')
    print(json.dumps({'kind':payload['kind'],'source_files':len(rows),'source_bytes':payload['source_bytes']}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report',required=True,type=Path)
    prepare(parser.parse_args().report)
