#!/usr/bin/env python3
"""Read-only XR inventory and supplemental physical-probe record validation.

Neither command executes Unity/devices, installs anything, changes task state,
nor accepts F04/F05. A structurally valid record still needs independent review.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import plistlib
import shutil
import tarfile
import tempfile
import zipfile
from runner_core import ReportError, parse_build_report

ROOT = Path(__file__).resolve().parents[1]
MODELS = {"quest3": "Quest 3", "quest3s": "Quest 3S"}
EVENTS = (
    "passthrough_visible", "hands_near_button", "hands_ray_button",
    "one_hand_left_button", "one_hand_right_button", "room_permission_denied",
    "virtual_fallback", "placement_confirmed", "pause_resume", "suspend_resume",
)
IDENTITY_FILES = ("source_manifest", "content_manifest", "toolchain_lock",
                  "project_version", "package_manifest", "package_lock", "apk", "build_report")
PACKAGE_PREFIXES = ("com.unity.xr.", "com.unity.inputsystem", "com.unity.render-pipelines.", "com.meta.xr.")


class Invalid(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise Invalid(message)


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def now():
    return datetime.now(timezone.utc).isoformat()


def canonical_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text())


def exclusive_json(path, value):
    """Publish complete bytes without replacing an existing historical record."""
    import os
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def metadata(path):
    path = Path(path)
    return {"path": str(path), "sha256": sha(path), "size_bytes": path.stat().st_size}


def installed_packages(contents):
    """Read bounded package metadata, never extract archives or resolve packages."""
    rows, errors = [], []
    base = contents / "Resources/PackageManager"
    for file in sorted((base / "BuiltInPackages").glob("*/package.json")):
        if file.parent.name.startswith(PACKAGE_PREFIXES):
            try:
                data = read(file)
                rows.append({**metadata(file), "kind": "editor_builtin_metadata", **{
                    key: data.get(key) for key in ("name", "version", "unity", "dependencies")}})
            except (OSError, ValueError) as exc:
                errors.append({"path": str(file), "error": str(exc)})
    for file in sorted((base / "Editor").glob("*.tgz")):
        if not file.name.startswith(PACKAGE_PREFIXES):
            continue
        try:
            with tarfile.open(file, "r:gz") as archive:
                member = archive.getmember("package/package.json")
                require(member.isfile() and member.size <= 2 * 1024 * 1024, "oversized package metadata")
                data_bytes = archive.extractfile(member).read()
                data = json.loads(data_bytes)
            rows.append({"path": str(file), "archive_size_bytes": file.stat().st_size,
                         "kind": "editor_bundled_archive_metadata", "member": member.name,
                         "metadata_sha256": hashlib.sha256(data_bytes).hexdigest(),
                         **{key: data.get(key) for key in ("name", "version", "unity", "dependencies")}})
        except (OSError, ValueError, KeyError, tarfile.TarError) as exc:
            errors.append({"path": str(file), "error": str(exc)})
    return rows, errors


def audit(root, editor_parent=Path("/Applications/Unity"), home=None):
    root = Path(root).resolve()
    home = Path.home() if home is None else Path(home)
    editors = []
    candidates = set(editor_parent.glob("Unity-*/Unity.app")) | set(editor_parent.glob("Hub/Editor/*/Unity.app"))
    for app in sorted(candidates):
        contents = app / "Contents"
        info_file = contents / "Info.plist"
        info = plistlib.loads(info_file.read_bytes()) if info_file.is_file() else {}
        packages, errors = installed_packages(contents)
        android = contents / "PlaybackEngines/AndroidPlayer"
        android_metadata = []
        for pattern in ("NDK/source.properties", "OpenJDK/release", "SDK/tools/source.properties",
                        "SDK/platform-tools/source.properties", "SDK/build-tools/*/source.properties",
                        "SDK/platforms/*/source.properties"):
            android_metadata.extend(metadata(p) for p in sorted(android.glob(pattern)) if p.is_file())
        editors.append({"application_path": str(app), "executable_exists": (contents / "MacOS/Unity").is_file(),
                        "info": {key: info.get(key) for key in ("CFBundleShortVersionString", "CFBundleVersion")},
                        "info_file": metadata(info_file) if info_file.is_file() else None,
                        "playback_engines": sorted(p.name for p in (contents / "PlaybackEngines").glob("*") if p.is_dir()),
                        "android_player_present": android.is_dir(), "android_metadata": android_metadata,
                        "package_metadata": packages, "metadata_errors": errors})
    cache_roots = [home / "Library/Unity/cache/packages", home / "Library/Caches/Unity/upm/cache",
                   root / "game/Library/PackageCache"]
    caches = []
    for cache in cache_roots:
        files = sorted(set(cache.glob("*/package.json")) | set(cache.glob("*/*/package.json"))) if cache.is_dir() else []
        packages = []
        for file in files:
            if not file.parent.name.startswith(PACKAGE_PREFIXES):
                continue
            try:
                data = read(file)
                packages.append({**metadata(file), "name": data.get("name"), "version": data.get("version"),
                                 "dependencies": data.get("dependencies")})
            except (OSError, ValueError) as exc:
                packages.append({"path": str(file), "error": str(exc)})
        caches.append({"path": str(cache), "exists": cache.is_dir(), "scope": "two-level metadata only", "packages": packages})
    volumes = [{"path": str(p), "free_bytes": shutil.disk_usage(p).free,
                "unity_floor_bytes": 40 * 2 ** 30} for p in (root, Path(tempfile.gettempdir()))]
    return {"schema_version": 1, "observed_utc": now(), "status": "preparation_only",
            "evidence_kind": "host_discovery_only", "cwd": str(root), "auditor": metadata(Path(__file__)),
            "editors": editors, "package_cache_observations": caches, "volumes": volumes,
            "project_metadata": [{"path": str(root / path), "exists": (root / path).is_file(),
                                   "artifact": metadata(root / path) if (root / path).is_file() else None}
                                  for path in ("game/ProjectSettings/ProjectVersion.txt", "game/Packages/manifest.json",
                                               "game/Packages/packages-lock.json", "automation/toolchain.lock.json")],
            "limits": ["No Editor/license/device/bridge connection test or installation executed.",
                       "Bundled/cache metadata is not the project's resolved or qualified package set.",
                       "Missing files are observations only; absent searched cache roots do not prove global absence.",
                       "Storage observations are not reservations; an operation peak estimate is additionally required."]}


def artifact(root, reference):
    require(isinstance(reference, dict), "artifact reference must be an object")
    relative = reference.get("path")
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute(), "artifact path must be relative")
    path = (root / relative).resolve()
    require(path.is_relative_to(root) and ".." not in Path(relative).parts, "artifact escapes authorized root")
    require(path.is_file() and path.stat().st_size > 0, "missing/empty artifact: " + relative)
    require(type(reference.get("size_bytes")) is int and path.stat().st_size == reference["size_bytes"], "artifact size mismatch: " + relative)
    require(reference.get("sha256") == sha(path), "artifact hash mismatch: " + relative)
    return path


def instant(value):
    require(isinstance(value, str), "timestamp missing")
    try:
        stamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise Invalid("invalid timestamp") from exc
    require(stamp.utcoffset() is not None and stamp.utcoffset().total_seconds() == 0, "timestamp must be UTC")
    return stamp


def text_field(record, key):
    require(isinstance(record.get(key), str) and bool(record[key].strip()), "missing text field: " + key)


def validate_probe(root, packet):
    """Check identity/artifacts/observations; never establish physical authenticity."""
    root = Path(root).resolve()
    require(isinstance(packet, dict), "probe record must be an object")
    require(type(packet.get("schema_version")) is int and packet["schema_version"] == 1 and packet.get("record_type") == "resq-xr-probe-observations", "wrong probe record type/version")
    require(packet.get("evidence_kind") == "physical_device", "physical-device evidence required")
    require(packet.get("freeze_id") == "resq-spec-freeze-1.1.0" and packet.get("rules_version") == "resq-rules-1", "specification identity mismatch")
    identity = packet.get("identity")
    require(isinstance(identity, dict), "candidate identity missing")
    files = {key: artifact(root, identity.get(key)) for key in IDENTITY_FILES}
    for key in ("build_id", "package_id", "version_code"):
        text_field(identity, key)
    candidate = canonical_hash(identity)
    require(packet.get("candidate_sha256") == candidate, "candidate envelope changed")
    # Native build output remains required; a text APK or arbitrary file is insufficient.
    native = parse_build_report(files["build_report"], artifact_root=root)
    require(native.get("apk_sha256") == identity["apk"]["sha256"], "build report/APK mismatch")
    require(native.get("platform") == "Android", "native build is not Android")
    require(native.get("build_guid") == identity["build_id"], "native build GUID mismatch")
    try:
        with zipfile.ZipFile(files["apk"]) as apk:
            names = apk.namelist()
            require("AndroidManifest.xml" in names and "lib/arm64-v8a/libil2cpp.so" in names, "ARM64 IL2CPP APK payload missing")
            require(not any(n.startswith("lib/") and n.split("/")[1] != "arm64-v8a" for n in names if len(n.split("/")) > 2), "non-ARM64 native library in APK")
            with apk.open("lib/arm64-v8a/libil2cpp.so") as library:
                header = library.read(20)
            require(len(header) == 20 and header[:6] == b"\x7fELF\x02\x01" and header[18:20] == b"\xb7\x00", "IL2CPP library is not ARM64 ELF")
    except zipfile.BadZipFile as exc:
        raise Invalid("APK is not a ZIP package") from exc
    require(read(files["package_manifest"]).get("dependencies"), "package manifest has no resolved inputs")
    require(read(files["package_lock"]).get("dependencies"), "package lock has no dependencies")
    require("m_EditorVersion:" in files["project_version"].read_text(), "Editor identity absent")
    prerequisite_path = artifact(root, packet.get("f04_result"))
    prerequisite = read(prerequisite_path)
    require(prerequisite.get("task_id") == "F04" and prerequisite.get("status") == "passed", "F04 prerequisite did not pass")
    prior_identity = prerequisite.get("identity", {})
    require(prior_identity.get("build_sha256") == identity["apk"]["sha256"] and
            prior_identity.get("toolchain_hash") == identity["toolchain_lock"]["sha256"], "F04 candidate identity mismatch")
    require(prior_identity.get("source_identity") == "sha256:" + identity["source_manifest"]["sha256"], "F04 source identity mismatch")
    source, content = read(files["source_manifest"]), read(files["content_manifest"])
    require(source.get("files") and content.get("files"), "source/content snapshot inventory absent")
    for entry in source["files"] + content["files"]:
        artifact(root, {**entry, "size_bytes": entry.get("byte_length")})
    require(content.get("payload_sha256") == prior_identity.get("content_hash"), "F04 content identity mismatch")
    require(any(e.get("sha256") == content["payload_sha256"] for e in content["files"]), "content payload not present in snapshot")
    sessions = packet.get("sessions")
    require(isinstance(sessions, list) and len(sessions) == 2, "exactly two physical model sessions required")
    require(all(isinstance(s, dict) for s in sessions), "session must be an object")
    require({s.get("model") for s in sessions} == set(MODELS), "Quest 3 and Quest 3S are both required")
    aliases, session_ids = set(), set()
    for session in sessions:
        require(session.get("evidence_kind") == "physical_device" and session.get("runtime") == "standalone_apk", "simulator/Link/Editor does not qualify")
        require(session.get("candidate_sha256") == candidate, "session candidate differs")
        for key in ("session_id", "device_alias", "os_build", "observer_alias"):
            text_field(session, key)
        require(session["session_id"] not in session_ids and session["device_alias"] not in aliases, "duplicate session/device identity")
        session_ids.add(session["session_id"]); aliases.add(session["device_alias"])
        start, end = instant(session.get("started_utc")), instant(session.get("ended_utc"))
        require(start < end, "empty/reversed session interval")
        for key in ("device_authorization", "consent", "capture", "runtime_log", "device_identity"):
            path = artifact(root, session.get(key))
            require(path != prerequisite_path and path not in files.values(), "session proof reuses candidate metadata")
        device = read(root / session["device_identity"]["path"])
        require(device.get("model") == MODELS[session["model"]] and device.get("os_build") == session["os_build"] and
                device.get("device_alias") == session["device_alias"] and device.get("evidence_kind") == "physical_device", "device identity mismatch")
        require(device.get("installed_package_id") == identity["package_id"] and
                device.get("installed_version_code") == identity["version_code"] and
                device.get("installed_apk_sha256") == identity["apk"]["sha256"], "installed build identity mismatch")
        log = read(root / session["runtime_log"]["path"])
        require(log.get("candidate_sha256") == candidate and log.get("session_id") == session["session_id"] and
                log.get("device_alias") == session["device_alias"], "runtime log identity mismatch")
        observations, events = session.get("observations"), log.get("events")
        require(isinstance(observations, list) and isinstance(events, list), "observations/runtime events absent")
        require(len(observations) == len(EVENTS) and {o.get("event_id") for o in observations} == set(EVENTS), "missing/duplicate probe observation")
        require(len(events) == len(EVENTS) and {e.get("event_id") for e in events} == set(EVENTS), "missing/duplicate runtime event")
        by_event = {e["event_id"]: e for e in events}
        offsets, sequence = [], []
        for observation in observations:
            event = by_event[observation["event_id"]]
            require(observation.get("result") == "observed_pass" and event.get("result") == "passed", "failed/unknown probe event")
            text_field(observation, "observed_behavior")
            require(start <= instant(observation.get("observed_utc")) <= end, "observation outside session")
            require(type(observation.get("capture_offset_ms")) is int and observation["capture_offset_ms"] >= 0, "capture offset absent")
            offsets.append(observation["capture_offset_ms"])
            require(type(event.get("sequence")) is int and type(event.get("monotonic_ms")) is int and event["monotonic_ms"] >= 0, "runtime sequence/time missing")
            sequence.append(event["sequence"])
            require(start <= instant(event.get("observed_utc")) <= end, "runtime event outside session")
        require(len(set(sequence)) == len(EVENTS) and min(sequence) >= 0, "duplicate/invalid runtime sequence")
        ordered = sorted(events, key=lambda e: e["sequence"])
        require([e["monotonic_ms"] for e in ordered] == sorted(e["monotonic_ms"] for e in ordered), "runtime clock reversed")
        require(max(offsets) <= (end - start).total_seconds() * 1000, "capture offset outside session")
        order = {e["event_id"]: e["sequence"] for e in events}
        require(order["room_permission_denied"] < order["virtual_fallback"] < order["placement_confirmed"], "denial/fallback/placement order invalid")
    return {"status": "structural_validation_only", "accepted_task": False, "models": sorted(MODELS),
            "candidate_sha256": candidate, "observations_checked": len(EVENTS) * 2,
            "limits": ["Physical authenticity, consent scope and observed behavior require independent authorized review.",
                       "Run the registered F05 check and root task verifier; this supplemental protocol never accepts F04/F05."]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    subs = parser.add_subparsers(dest="command", required=True)
    inventory = subs.add_parser("audit"); inventory.add_argument("--report", type=Path, required=True)
    probe = subs.add_parser("validate-probe"); probe.add_argument("record", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "audit":
            exclusive_json(args.report, audit(args.root))
            print("Preparation audit recorded: " + str(args.report))
        else:
            print(json.dumps(validate_probe(args.root, read(args.record)), indent=2))
    except (Invalid, OSError, KeyError, TypeError, ValueError, ReportError) as exc:
        print("BLOCKED/INVALID: " + str(exc))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
