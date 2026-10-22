"""Bind F04 to retained actual build inputs; never execute or qualify a build.

Called only after the Unity adapter has cross-checked its owned process, native
BuildReport and complete native assertion packet. This does not supply those
assertions, choose packages, inspect hardware or accept any task.
"""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

from bootstrap_doctor import ROOT, artifact, digest, read, snapshot_identity, write_once
from runner_core import GIB, StorageBlocked, parse_build_report
from task_execution import source_files


class CandidateError(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise CandidateError(message)


def owned_file(path):
    path = Path(path)
    path = path if path.is_absolute() else ROOT / path
    require(path.is_relative_to(ROOT), 'Candidate path is outside workspace')
    for part in [path, *path.parents]:
        if part == ROOT.parent:
            break
        require(not part.is_symlink(), 'Candidate symlink is not admitted')
    require(path.resolve().is_relative_to(ROOT.resolve()), 'Candidate path escapes workspace')
    require(path.is_file() and path.stat().st_size > 0, 'Candidate input missing/empty: ' + str(path))
    return path


def arm64_il2cpp(apk):
    try:
        with zipfile.ZipFile(apk) as archive:
            names = archive.namelist()
            require(len(names) == len(set(names)), 'APK contains duplicate ZIP entries')
            require('AndroidManifest.xml' in names and 'lib/arm64-v8a/libil2cpp.so' in names,
                    'APK requires Android manifest and ARM64 IL2CPP payload')
            require(all(not name.startswith('lib/') or len(name.split('/')) < 3
                        or name.split('/')[1] == 'arm64-v8a' for name in names),
                    'APK contains a non-ARM64 native library')
            with archive.open('lib/arm64-v8a/libil2cpp.so') as library:
                header = library.read(20)
            require(len(header) == 20 and header[:6] == b'\x7fELF\x02\x01'
                    and header[18:20] == b'\xb7\x00', 'IL2CPP payload is not ARM64 ELF')
    except (OSError, zipfile.BadZipFile) as error:
        raise CandidateError('APK is not a readable ZIP package') from error


def bound_reference(reference):
    require(isinstance(reference, dict), 'Missing toolchain input reference')
    path = owned_file(reference.get('path', ''))
    require(reference.get('sha256') == digest(path)
            and type(reference.get('byte_length')) is int
            and reference['byte_length'] == path.stat().st_size, 'Stale toolchain input reference')
    return path


def validate_lock(data, native, required, directory, launched_editor):
    require(isinstance(data, dict) and data.get('schema_version') == 1
            and data.get('status') == 'resolved_local_candidate',
            'Actual resolved local candidate lock required; observed/physically-qualified labels rejected')
    inputs = data.get('inputs')
    require(isinstance(inputs, dict) and set(inputs) == set(required), 'Exact toolchain project inputs required')
    for name in required:
        retained = bound_reference(inputs[name])
        require(retained == directory / 'resolved-inputs' / name, 'Toolchain project inputs must be retained in the owned run')
        current = owned_file(name)
        require(digest(retained) == digest(current) and retained.stat().st_size == current.stat().st_size,
                'Retained project input differs from actual resolution')
    version_lines = (ROOT / required[0]).read_text().splitlines()
    versions = [line.split(':', 1)[1].strip() for line in version_lines if line.startswith('m_EditorVersion:')]
    require(len(versions) == 1 and versions[0] == native.get('unity_version'), 'Native/ProjectVersion mismatch')
    editor = data.get('editor')
    require(isinstance(editor, dict) and editor.get('version') == versions[0], 'Toolchain Editor version mismatch')
    require(isinstance(launched_editor, dict) and isinstance(editor.get('executable'),dict)
            and {key: editor['executable'].get(key) for key in ('path','sha256','byte_length')} == launched_editor,
            'Toolchain lock differs from the actual launched Editor')
    resolution = read(ROOT / required[2]).get('dependencies')
    manifest = read(ROOT / required[1]).get('dependencies')
    packages = data.get('packages')
    require(isinstance(resolution, dict) and bool(resolution) and isinstance(manifest, dict) and bool(manifest),
            'Resolved package metadata missing')
    require(set(manifest) <= set(resolution), 'Project package missing from actual resolution')
    require(isinstance(packages, dict) and set(packages) == set(resolution), 'Exact resolved package inventory required')
    for name, entry in packages.items():
        require(isinstance(entry, dict) and isinstance(resolution[name], dict)
                and entry.get('version') == resolution[name].get('version')
                and isinstance(entry.get('version'), str) and bool(entry['version']), 'Resolved package version mismatch')
        manifest_path = bound_reference(entry.get('manifest'))
        require(manifest_path == directory / 'package-metadata' / (name + '.json'),
                'Package metadata must be retained in the owned run, not a mutable cache')
        metadata = read(manifest_path)
        require(metadata.get('name') == name and metadata.get('version') == entry['version'], 'Package manifest identity mismatch')
    tools = data.get('android_tools')
    require(isinstance(tools, dict) and set(tools) == {'sdk', 'ndk', 'jdk'}, 'Exact Android tool inventory required')
    # Hash actual tool bytes without copying installations. Version/compatibility
    # observations still belong to the native a02 inspector, not this binder.
    for label, entry in [('editor', editor), *sorted(tools.items())]:
        require(isinstance(entry, dict) and isinstance(entry.get('version'), str) and bool(entry['version'].strip()),
                'Tool version missing: ' + label)
        reference = entry.get('executable')
        require(isinstance(reference, dict) and isinstance(reference.get('path'), str), 'Tool executable metadata missing')
        path = Path(reference['path'])
        require(path.is_absolute() and path.resolve() == path and path.is_file(), 'Canonical actual tool executable required')
        require(type(reference.get('byte_length')) is int and reference['byte_length'] > 0
                and path.stat().st_size == reference['byte_length'] and digest(path) == reference.get('sha256'),
                'Actual tool executable hash/size mismatch: ' + label)


def prepare_candidate(context, packet):
    """Retain and validate without changing the caller's current identity."""
    require(context['task']['id'] == 'F04', 'Resolved probe binding is restricted to F04')
    directory = Path(context['run_dir'])
    require(directory.is_relative_to(ROOT / 'evidence/runs/F04'), 'F04 owned run directory required')
    require(len(directory.relative_to(ROOT / 'evidence/runs/F04').parts) == 2,
            'F04 exact run/attempt directory required')
    require(packet.get('status') == 'passed', 'Native case packet did not pass')
    cases = packet.get('case_outcomes')
    expected = {f'check-f04/assertion/assertion_id=check-f04-a{i:02}' for i in range(1, 5)}
    require(isinstance(cases, list) and len(cases) == 4
            and {row.get('case_id') for row in cases} == expected,
            'Exact four native F04 assertions required')
    for row in cases:
        require(row.get('status') == 'passed' and row.get('kind') == 'assertion'
                and row.get('dimensions') == {'assertion_id': row['case_id'].split('=')[1]}
                and isinstance(row.get('observations'), dict) and row.get('metrics') == {}
                and row.get('participant_alias') is None and row.get('proof_index') is None,
                'Malformed/nonpassing native F04 assertion')

    build_report = owned_file(directory / 'build-report.json')
    native = parse_build_report(build_report, artifact_root=directory)
    apk = owned_file(native['output_path'])
    require(apk == directory / 'resq.apk' and native.get('platform') == 'Android',
            'Exact owned Android probe APK required')
    require(isinstance(native.get('build_guid'), str) and bool(native['build_guid'].strip()),
            'Native build GUID required')
    arm64_il2cpp(apk)
    lock = owned_file('automation/toolchain.lock.json')
    lock_bytes = lock.read_bytes()
    before_lock = hashlib.sha256(lock_bytes).hexdigest()
    lock_data = json.loads(lock_bytes)
    required = ('game/ProjectSettings/ProjectVersion.txt', 'game/Packages/manifest.json',
                'game/Packages/packages-lock.json')
    for name in required:
        owned_file(name)
    validate_lock(lock_data, native, required, directory, context.get('launched_editor'))
    require(packet.get('build_binding') == {'build_guid': native['build_guid'],
            'apk_sha256': native['apk_sha256'], 'build_report_sha256': native['sha256'],
            'toolchain_lock_sha256': before_lock}, 'Native assertion packet refers to a different build/lock')

    # Build/import may add .meta and resolve packages-lock, but cannot silently
    # rewrite code, authored assets or settings captured at admission.
    original = context['identity']
    before_manifest = owned_file(original['source_manifest']['path'])
    require(digest(before_manifest) == original['source_manifest']['sha256'],
            'Admission source manifest changed')
    before = {}
    prefix = directory / 'inputs/source'
    for ref in read(before_manifest)['files']:
        retained = owned_file(ref['path'])
        require(retained.is_relative_to(prefix), 'Admission input is outside its owned snapshot')
        require(digest(retained) == ref['sha256'] and retained.stat().st_size == ref['byte_length'],
                'Admission input bytes changed')
        before[str(retained.relative_to(prefix))] = ref['sha256']
    selected = source_files()
    after = {name: digest(owned_file(name)) for name in selected}
    require(set(before) <= set(after), 'Build removed admitted source inputs')
    changes = []
    for name in sorted(set(before) | set(after)):
        if before.get(name) == after.get(name):
            continue
        allowed = name == 'game/Packages/packages-lock.json' or (
            name not in before and name.startswith('game/Assets/') and name.endswith('.meta'))
        require(allowed, 'Unexpected source change during build: ' + name)
        changes.append({'path': name, 'before_sha256': before.get(name), 'after_sha256': after[name]})

    # Small metadata/source copy only. The APK stays in its original owned run;
    # no large native binary or external installation is recopied here.
    profile=read(ROOT/'automation/local_operation_profiles.json')
    require(profile['reserve_bytes']==2*GIB and profile['stages']['android-probe-build']['task']=='F04',
            'Exact scoped F04 retention policy required')
    reserve=profile['reserve_bytes'];margin=profile['stages']['android-probe-build']['buffer']
    # Retain exact source bytes plus manifests/content metadata. The margin is
    # kept throughout copying; no APK or installed Android tools are recopied.
    needed = sum((ROOT / name).stat().st_size for name in selected) + lock.stat().st_size
    needed += (ROOT/'specification/content/mission_contracts.json').stat().st_size + 1024**2
    if needed>profile['stages']['android-probe-build']['growth']:
        raise StorageBlocked('Resolved-candidate retention exceeds scoped F04 growth cap')
    if shutil.disk_usage(ROOT).free < reserve + needed + margin:
        raise StorageBlocked('Resolved-candidate snapshot would cross scoped 2 GiB reserve plus exact bytes and margin')
    retained_bytes=0
    def guard_copy(size):
        nonlocal retained_bytes
        if retained_bytes+size>profile['stages']['android-probe-build']['growth']:
            raise StorageBlocked('Resolved-candidate actual copy reached scoped F04 growth cap')
        if shutil.disk_usage(ROOT).free < reserve+size+margin:
            raise StorageBlocked('Resolved-candidate copying stopped at scoped 2 GiB reserve')
        retained_bytes+=size
    destination = directory / 'resolved-candidate'
    executor = write_once(directory / 'executor-identity.json', deepcopy(original))
    candidate = snapshot_identity(destination, selected,
                                  {'status': 'source_after_native_resolution_not_physical_qualification'},before_write=guard_copy)
    guard_copy(len(lock_bytes))
    retained_lock = write_once(destination / 'inputs/toolchain.lock.json', lock_bytes)
    require(digest(lock) == before_lock == digest(retained_lock), 'Toolchain lock changed during retention')
    for name in selected:
        require(digest(ROOT / name) == after[name]
                and digest(destination / 'inputs/source' / name) == after[name],
                'Source changed during candidate retention: ' + name)
    require(source_files() == selected, 'Source inventory changed during candidate retention')
    validate_lock(read(retained_lock), native, required, directory, context.get('launched_editor'))
    require(parse_build_report(build_report, artifact_root=directory) == native, 'Native report changed during retention')
    require(digest(apk) == native['apk_sha256'] and digest(build_report) == native['sha256'],
            'Build artifacts changed during candidate retention')
    require(candidate['content_hash'] == original['content_hash'], 'Authored content changed during native build')
    candidate['toolchain_lock'] = artifact(retained_lock, 'resolved_local_candidate_toolchain')
    candidate['toolchain_hash'] = digest(retained_lock)
    candidate['build_sha256'] = native['apk_sha256']
    bindings = {'executor_identity': artifact(executor, 'pre_build_executor_identity'),
                'apk': artifact(apk, 'actual_native_apk'),
                'build_report': artifact(build_report, 'actual_native_build_report'),
                'toolchain_lock': candidate['toolchain_lock'],
                'project_version': artifact(destination / 'inputs/source' / required[0], 'resolved_project_version'),
                'package_manifest': artifact(destination / 'inputs/source' / required[1], 'resolved_package_manifest'),
                'package_lock': artifact(destination / 'inputs/source' / required[2], 'resolved_package_lock'),
                'build_guid': native['build_guid'], 'resolution_changes': changes,
                'physical_qualification': 'not_executed'}
    write_once(destination / 'bindings.json', bindings)
    return candidate, bindings


def bind_candidate(context, packet):
    candidate, bindings = prepare_candidate(context, packet)
    # Preserve the identity alias used by resq.main. No caller-selected identity
    # override is accepted; every value above comes from retained local bytes.
    context['identity'].clear()
    context['identity'].update(candidate)
    for row in packet['case_outcomes']:
        row['observations'] = dict(row['observations'], resolved_build=bindings)
