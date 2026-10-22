#!/usr/bin/env python3
"""F06 evidence adapter. Never launches Blender/Unity or invents observations."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from runner_core import write_json_exclusive


class UnsupportedOperation(ValueError):
    pass


def owned_file(root, path):
    """Resolve only regular owned files, with no symlink in the path."""
    root = root.resolve()
    value = Path(path)
    if not value.is_absolute():
        value = root / value
    if ".." in value.parts or not value.is_relative_to(root):
        raise ValueError("Path escapes the owned workspace")
    current = root
    for component in value.relative_to(root).parts:
        current = current / component
        if current.is_symlink():
            raise ValueError("Linked evidence destinations are not admitted")
    if not value.resolve().is_relative_to(root):
        raise ValueError("Resolved path escapes the owned workspace")
    return value


def execute(root, argv):
    root = Path(root).resolve()
    if len(argv) != 5 or argv[:4] != ["audit", "--kit", "calibration", "--native-cases"]:
        raise UnsupportedOperation("Only the registered calibration audit operation is implemented")
    destination = owned_file(root, argv[4])
    run_root = root / "evidence/runs/F06"
    if not destination.is_relative_to(run_root) or destination.name != "backend-cases.json":
        raise ValueError("Use the F06 runner's owned native-case destination")
    if destination.exists():
        raise FileExistsError("Native evidence destination already exists")
    inlet = owned_file(root, "evidence/calibration/qualification-input.json")
    if not inlet.is_file():
        raise FileNotFoundError("Missing actual calibration qualification-input.json; native Unity and inspection evidence are required")
    from art.qualify_calibration import qualify
    inlet_bytes = inlet.read_bytes()
    packet = json.loads(inlet_bytes)
    qualified = qualify(root, packet)
    required = {"check-f06-a0" + str(i) for i in range(1, 5)}
    if (qualified.get("status") != "complete_structural_validation"
            or qualified.get("accepted_task") is not False
            or set(qualified.get("assertions", {})) != required):
        raise ValueError("Complete exact F06 qualification observations are required")
    contract_path = root / "specification/content/acceptance_cases.json"
    contracts = json.loads(contract_path.read_text())["contracts"]
    contract = next(row for row in contracts if row["id"] == "check-f06")
    cases = contract["required_cases"]
    if len(cases) != 4 or {case["dimensions"].get("assertion_id") for case in cases} != required:
        raise ValueError("F06 case contract changed; review adapter compatibility")
    outcomes = []
    for case in cases:
        details = qualified["assertions"][case["dimensions"]["assertion_id"]]
        if not isinstance(details, dict) or not details:
            raise ValueError("Required assertion observation missing")
        outcomes.append({"case_id": case["case_id"], "kind": case["kind"],
                         "dimensions": case["dimensions"], "status": "passed",
                         "metrics": {}, "participant_alias": None, "proof_index": None,
                         "observations": details})
    references = qualified.get("artifacts")
    if not isinstance(references, list) or not references:
        raise ValueError("Validated native input artifacts must be retained")
    references = references + [{"path": str(inlet.relative_to(root)),
                               "sha256": hashlib.sha256(inlet_bytes).hexdigest(),
                               "size_bytes": len(inlet_bytes)}]
    retained = []
    seen = set()
    for reference in references:
        if not isinstance(reference, dict) or not isinstance(reference.get("path"), str):
            raise ValueError("Invalid validated artifact reference")
        original = Path(reference["path"])
        if original.is_absolute() or ".." in original.parts:
            raise ValueError("Qualification artifacts must be workspace-relative")
        source = owned_file(root, original)
        data = source.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if type(reference.get("size_bytes")) is not int or reference["size_bytes"] != len(data) or reference.get("sha256") != digest:
            raise ValueError("Validated input changed before retention: " + str(original))
        if str(original) in seen:
            continue
        seen.add(str(original))
        target = owned_file(root, destination.parent / "qualification-inputs" / original)
        if target.exists():
            raise FileExistsError("Retained native input already exists")
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(data)
        retained.append({"original_path": str(original), "path": str(target.relative_to(root)),
                         "sha256": digest, "size_bytes": len(data)})
    retention_path = owned_file(root, destination.parent / "qualification-retention.json")
    write_json_exclusive(retention_path, {"accepted_task": False, "immutable_inputs": retained})
    retention_bytes = retention_path.read_bytes()
    retention_reference = {"path": str(retention_path.relative_to(root)),
                           "sha256": hashlib.sha256(retention_bytes).hexdigest(),
                           "size_bytes": len(retention_bytes)}
    for outcome in outcomes:
        outcome["observations"] = dict(outcome["observations"], retained_evidence=retention_reference)
    audit_path = owned_file(root, "evidence/calibration/audit.json")
    if audit_path.exists():
        if json.loads(audit_path.read_text()) != qualified:
            raise FileExistsError("Retained calibration audit differs; preserve/review the old evidence before a new qualification")
    else:
        write_json_exclusive(audit_path, qualified)
    write_json_exclusive(destination, {"schema_version": 1, "status": "passed",
                                      "case_outcomes": outcomes, "proof_records": [],
                                      "retained_evidence": retention_reference,
                                      "accepted_task": False,
                                      "limits": ["Evidence validation only. Root runner and independent verifier decide task acceptance."]})
    return destination


def main(argv=None):
    try:
        result = execute(ROOT, list(sys.argv[1:] if argv is None else argv))
    except (FileNotFoundError, UnsupportedOperation) as error:
        print(json.dumps({"status": "blocked", "accepted_task": False, "reason": str(error)}))
        return 75
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(json.dumps({"status": "failed", "accepted_task": False, "reason": str(error)}))
        return 1
    print(json.dumps({"status": "passed", "accepted_task": False,
                      "native_cases": str(result.relative_to(ROOT))}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
