#!/usr/bin/env python3
"""F05 reviewed physical-evidence adapter; never operates or discovers devices."""
from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from backends.art import owned_file
from foundation_probe import MODELS, canonical_hash, instant, validate_probe
from runner_core import write_json_exclusive

INLET = "evidence/toolchain/device/probe-input.json"
OUTPUT = "evidence/toolchain/device/probe.json"
SESSION_ARTIFACTS = ("device_authorization", "consent", "capture", "runtime_log", "device_identity")
REVIEW_CHECKS = ("actual_physical_device", "standalone_apk", "capture_inspected",
                 "runtime_log_inspected", "device_identity_inspected",
                 "authorization_scope_confirmed", "consent_scope_confirmed",
                 "all_observations_correlated", "exact_package_lock_after_probe")


class UnsupportedOperation(ValueError):
    pass


class MissingActualInput(FileNotFoundError):
    pass


class EvidenceStorageBlocked(RuntimeError):
    pass


def current_utc():
    return datetime.now(timezone.utc)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def reference(root, ref):
    require(isinstance(ref, dict), "Artifact reference must be an object")
    name = ref.get("path")
    require(isinstance(name, str) and name and not Path(name).is_absolute(), "Artifact must be workspace-relative")
    path = owned_file(root, name)
    size = ref.get("size_bytes", ref.get("byte_length"))
    require(type(size) is int and size == path.stat().st_size and size >= 0, "Artifact size mismatch: " + name)
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    require(ref.get("sha256") == digest, "Artifact hash mismatch: " + name)
    return {"path": name, "sha256": ref["sha256"], "size_bytes": size}


def load(root, ref):
    return json.loads(owned_file(root, reference(root, ref)["path"]).read_bytes())


def accepted_f04(root, probe):
    """Current acceptance and root recursive verification are both mandatory."""
    from validate_spec_freeze import validate_task_result
    ref = reference(root, probe.get("f04_result"))
    state = json.loads(owned_file(root, "automation/task_state.json").read_bytes())
    head = state["task_states"]["F04"]
    require(head.get("status") == "passed" and head.get("result_path") == ref["path"]
            and head.get("result_sha256") == ref["sha256"], "F04 accepted head differs from probe")
    result = load(root, ref)
    require(result.get("task_id") == "F04" and result.get("status") == "passed", "F04 result did not pass")
    validate_task_result(root, result, expected_task="F04", verify_current_gates=False)
    for name in ("source_manifest", "content_manifest", "toolchain_lock"):
        require(reference(root, result["identity"][name]) == reference(root, probe["identity"][name]),
                "F04 accepted " + name + " differs")
    source = load(root, probe["identity"]["source_manifest"])
    for name, suffix in (("project_version", "/game/ProjectSettings/ProjectVersion.txt"),
                         ("package_manifest", "/game/Packages/manifest.json"),
                         ("package_lock", "/game/Packages/packages-lock.json")):
        matches = [entry for entry in source["files"] if ("/" + entry["path"]).endswith(suffix)]
        require(len(matches) == 1, "F04 source snapshot lacks exact " + name)
        actual, recorded = reference(root, probe["identity"][name]), reference(root, matches[0])
        require(actual["sha256"] == recorded["sha256"] and actual["size_bytes"] == recorded["size_bytes"],
                "F04 source snapshot differs: " + name)
    lock_bindings = [binding for binding in result["output_bindings"]
                     if binding["expected_path"] == "automation/toolchain.lock.json"]
    require(len(lock_bindings) == 1, "F04 accepted package lock output absent")
    outputs = {entry["path"]: entry for entry in result["artifacts"]}
    lock = reference(root, outputs[lock_bindings[0]["artifact_path"]])
    require(lock["sha256"] == probe["identity"]["toolchain_lock"]["sha256"], "F04 output toolchain lock differs")
    return result


def reviewed_provenance(root, probe, refs):
    require(isinstance(refs, list) and len(refs) == 2, "Two independent provenance reviews are required")
    reviews = {}
    for ref in refs:
        record = load(root, ref)
        require(isinstance(record, dict), "Provenance review must be an object")
        require(record.get("schema_version") == 1 and type(record["schema_version"]) is int
                and record.get("record_type") == "resq-physical-probe-provenance-review", "Wrong provenance review type/version")
        require(record.get("decision") == "accepted_actual_provenance"
                and record.get("review_basis") == "direct_inspection_of_original_records", "Actual provenance must be explicitly reviewed")
        require(record.get("probe_sha256") == canonical_hash(probe)
                and record.get("candidate_sha256") == probe["candidate_sha256"]
                and record.get("f04_result") == probe["f04_result"], "Provenance review candidate/packet/F04 mismatch")
        model = record.get("model")
        require(model in MODELS and model not in reviews, "Duplicate/unknown review model")
        session = next(s for s in probe["sessions"] if s["model"] == model)
        for field in ("session_id", "device_alias", "os_build", "started_utc", "ended_utc"):
            require(record.get(field) == session[field], "Provenance review session identity mismatch: " + field)
        reviewer = record.get("reviewer_alias")
        require(isinstance(reviewer, str) and reviewer.strip() and reviewer != session["observer_alias"],
                "Independent provenance reviewer required")
        require(record.get("reviewer_kind") in ("human", "authorized_independent_reviewer"), "Reviewer kind required")
        reviewed = instant(record.get("reviewed_utc"))
        require(reviewed >= instant(session["ended_utc"]), "Review precedes completed session")
        require(reviewed <= current_utc(), "Review/session is in the future")
        require(record.get("reviewed_artifacts") == {name: session[name] for name in SESSION_ARTIFACTS},
                "Review must bind capture/log/device/authorization/consent exactly")
        require(isinstance(record.get("checks"), dict) and set(record["checks"]) == set(REVIEW_CHECKS)
                and all(record["checks"][key] is True for key in REVIEW_CHECKS), "Incomplete actual provenance checks")
        notes = record.get("review_notes")
        require(isinstance(notes, str) and notes.strip(), "Provenance review notes absent")
        reviews[model] = {"artifact": reference(root, ref), "reviewer_alias": reviewer,
                          "reviewed_utc": record["reviewed_utc"]}
    return reviews


def artifact_closure(root, values):
    """Collect validated bytes, including recursive accepted result/report inputs."""
    found = {}
    documents = set()

    def add(ref):
        if isinstance(ref.get("path"), str) and Path(ref["path"]).is_absolute():
            absolute = Path(ref["path"])
            # Toolchain inventories can name installed SDK binaries outside the
            # checkout. Retain their locked metadata, not external binary bodies.
            if not absolute.is_relative_to(root):
                return
            ref = dict(ref, path=str(absolute.relative_to(root)))
        checked = reference(root, ref)
        previous = found.get(checked["path"])
        require(previous is None or previous == checked, "Conflicting artifact identity")
        found[checked["path"]] = checked
        path = owned_file(root, checked["path"])
        if path.suffix == ".json" and checked["path"] not in documents:
            documents.add(checked["path"])
            try:
                walk(json.loads(path.read_bytes()))
            except (UnicodeDecodeError, json.JSONDecodeError):
                pass

    def pointer(value, path_key, sha_key):
        if isinstance(value.get(path_key), str) and isinstance(value.get(sha_key), str):
            if Path(value[path_key]).is_absolute() and not Path(value[path_key]).is_relative_to(root):
                return
            path = owned_file(root, value[path_key])
            add({"path": str(path.relative_to(root)), "sha256": value[sha_key], "size_bytes": path.stat().st_size})

    def walk(value):
        if isinstance(value, dict):
            if "path" in value and "sha256" in value and ("size_bytes" in value or "byte_length" in value):
                add(value)
            for path_key, sha_key in (("result_path", "result_sha256"), ("report_path", "report_sha256"),
                                     ("evidence_path", "evidence_sha256"), ("capture_path", "capture_sha256"),
                                     ("payload_path", "payload_sha256")):
                pointer(value, path_key, sha_key)
            for item in value.values():
                walk(item)
        elif isinstance(value, list):
            for item in value:
                walk(item)
    for value in values:
        walk(value)
    return list(found.values())


def retain(root, destination, references):
    retained = []
    reserve = 5 * 1024 ** 3
    if shutil.disk_usage(root).free < reserve + sum(ref['size_bytes'] for ref in references) + 2 * 1024 ** 2:
        raise EvidenceStorageBlocked("Insufficient space to retain reviewed evidence while preserving the5GiB reserve")
    for ref in references:
        checked = reference(root, ref)
        if shutil.disk_usage(root).free < reserve + checked['size_bytes']:
            raise EvidenceStorageBlocked("Evidence-copy reserve changed before retention")
        source = owned_file(root, checked["path"])
        target = owned_file(root, destination.parent / "probe-inputs" / checked["path"])
        target.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as incoming, target.open("xb") as stream:
            shutil.copyfileobj(incoming, stream, length=1024 * 1024)
        retained.append({"original_path": checked["path"], "path": str(target.relative_to(root)),
                         "sha256": checked["sha256"], "size_bytes": checked["size_bytes"]})
    # The bytes used by proofs must be checked after every copy, before success.
    for ref in retained:
        reference(root, ref)
    return retained


def execute(root, argv):
    root = Path(root).resolve()
    if len(argv) != 7 or argv[:6] != ["qualify", "--scope", "xr-probe", "--models", "quest3,quest3s", "--native-cases"]:
        raise UnsupportedOperation("Only the registered both-model xr-probe qualification operation is implemented")
    destination = owned_file(root, argv[6])
    require(destination.is_relative_to(root / "evidence/runs/F05") and destination.name == "backend-cases.json"
            and len(destination.relative_to(root / "evidence/runs/F05").parts) == 3,
            "Use the F05 runner's owned run/attempt native-case destination")
    if destination.exists():
        raise FileExistsError("Native evidence destination already exists")
    inlet = owned_file(root, INLET)
    if not inlet.is_file():
        raise MissingActualInput("Missing actual probe-input.json; both physical sessions and independent provenance reviews are required")
    inlet_bytes = inlet.read_bytes()
    packet = json.loads(inlet_bytes)
    require(isinstance(packet, dict), "Reviewed probe inlet must be an object")
    require(packet.get("schema_version") == 1 and type(packet["schema_version"]) is int
            and packet.get("record_type") == "resq-reviewed-xr-probe-input", "Wrong reviewed probe inlet type/version")
    probe = packet.get("probe")
    validated = validate_probe(root, probe)
    require(validated.get("status") == "structural_validation_only" and validated.get("accepted_task") is False,
            "Supplemental validation did not complete")
    f04 = accepted_f04(root, probe)
    reviews = reviewed_provenance(root, probe, packet.get("provenance_reviews"))
    contract = next(row for row in json.loads(owned_file(root, "specification/content/acceptance_cases.json").read_bytes())["contracts"]
                    if row["id"] == "check-f05")
    required = {"check-f05-a0" + str(i) for i in range(1, 5)}
    cases = contract["required_cases"]
    require(len(cases) == 4 and {c["dimensions"].get("assertion_id") for c in cases} == required,
            "F05 frozen case contract changed; review adapter compatibility")
    summary = {"schema_version": 1, "status": "complete_structural_validation", "accepted_task": False,
               "candidate_identity": f04["identity"], "candidate_sha256": probe["candidate_sha256"],
               "probe_sha256": canonical_hash(probe), "f04_result": probe["f04_result"],
               "models": sorted(MODELS), "probe": probe, "provenance_reviews": reviews,
               "limitations": ["Reviewed record consistency cannot authenticate invented records.",
                               "Physical authenticity and task acceptance still require independent root review."]}
    output = owned_file(root, OUTPUT)
    if output.exists() and json.loads(output.read_bytes()) != summary:
        raise FileExistsError("Retained probe output conflicts; preserve and review earlier evidence")
    inlet_ref = {"path": INLET, "sha256": hashlib.sha256(inlet_bytes).hexdigest(), "size_bytes": len(inlet_bytes)}
    contract_file = owned_file(root, "specification/content/acceptance_cases.json")
    contract_ref = {"path": str(contract_file.relative_to(root)), "sha256": hashlib.sha256(contract_file.read_bytes()).hexdigest(),
                    "size_bytes": contract_file.stat().st_size}
    references = artifact_closure(root, [packet, inlet_ref, f04, contract_ref])
    retained = retain(root, destination, references)
    retention = destination.parent / "probe-retention.json"
    write_json_exclusive(retention, {"accepted_task": False, "immutable_inputs": retained})
    retention_ref = {"path": str(retention.relative_to(root)), "sha256": hashlib.sha256(retention.read_bytes()).hexdigest(),
                     "size_bytes": retention.stat().st_size}
    copies = {ref["original_path"]: ref for ref in retained}
    proofs, sessions = [], []
    for index, session in enumerate(sorted(probe["sessions"], key=lambda s: s["model"])):
        capture = copies[session["capture"]["path"]]
        proofs.append({"record_type": "physical_device", "model": session["model"], "os_version": session["os_build"],
                       "local_device_alias": session["device_alias"], "build_sha256": f04["identity"]["build_sha256"],
                       "session_id": session["session_id"], "capture_path": capture["path"],
                       "capture_sha256": capture["sha256"], "observed_utc": session["ended_utc"]})
        sessions.append({"model": session["model"], "session_id": session["session_id"], "proof_index": index,
                         "observations": session["observations"], "artifacts": {key: copies[session[key]["path"]] for key in SESSION_ARTIFACTS},
                         "provenance_review": copies[reviews[session["model"]]["artifact"]["path"]]})
    scopes = {"check-f05-a01": "Both authorized device/OS/installed-build identities and consent scopes reviewed",
              "check-f05-a02": "All ten actual hand/passthrough/fallback/placement/pause/suspend observations on both models",
              "check-f05-a03": "Both sessions reviewed as physical standalone APK execution, with no simulator substitution",
              "check-f05-a04": "Exact F04 source/package/toolchain/APK identity reviewed after both completed probes"}
    outcomes = [{"case_id": case["case_id"], "kind": case["kind"], "dimensions": case["dimensions"],
                 "status": "passed", "metrics": {}, "participant_alias": None, "proof_index": i % 2,
                 "observations": {"assertion_id": case["dimensions"]["assertion_id"],
                                  "assertion_scope": scopes[case["dimensions"]["assertion_id"]], "both_model_sessions": sessions,
                                  "package_lock": copies[probe["identity"]["package_lock"]["path"]],
                                  "toolchain_lock": copies[probe["identity"]["toolchain_lock"]["path"]],
                                  "f04_result": probe["f04_result"], "retained_evidence": retention_ref,
                                  "candidate_identity": f04["identity"]}}
                for i, case in enumerate(cases)]
    for ref in retained:
        reference(root, ref)
    require(accepted_f04(root, probe)["identity"] == f04["identity"], "F04 accepted identity changed during retention")
    if not output.exists():
        write_json_exclusive(output, summary)
    write_json_exclusive(destination, {"schema_version": 1, "status": "passed", "case_outcomes": outcomes,
                                      "proof_records": proofs, "candidate_identity": f04["identity"],
                                      "retained_evidence": retention_ref, "accepted_task": False,
                                      "limits": summary["limitations"]})
    return destination


def main(argv=None):
    try:
        destination = execute(ROOT, list(sys.argv[1:] if argv is None else argv))
    except (MissingActualInput, UnsupportedOperation, EvidenceStorageBlocked) as error:
        print(json.dumps({"status": "blocked", "accepted_task": False, "reason": str(error)}))
        return 75
    except (OSError, ValueError, KeyError, TypeError, StopIteration) as error:
        print(json.dumps({"status": "failed", "accepted_task": False, "reason": str(error)}))
        return 1
    print(json.dumps({"status": "passed", "accepted_task": False, "native_cases": str(destination.relative_to(ROOT))}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
