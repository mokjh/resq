#!/usr/bin/env python3
"""Temporary synthetic F05 security fixtures; never physical or task evidence.

Only the accepted-F04 root verifier is mocked: these fixtures intentionally cannot
satisfy an actual root acceptance record. The supplemental validator runs normally.
"""
import hashlib
import io
import json
from pathlib import Path
import tempfile
from datetime import datetime, timezone
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from backends import device
import test_foundation_probe as supplemental
from validate_spec_freeze import schema_check


class DeviceBackendTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="resq-synthetic-device-adapter-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        # All fixture sessions are synthetic; use an explicit fixture clock.
        clock = patch.object(device, 'current_utc', return_value=datetime(2026, 10, 16, 1, 0, tzinfo=timezone.utc))
        clock.start()
        self.addCleanup(clock.stop)
        # Synthetic data has a synthetic capacity model. The dedicated low-space
        # test overrides this and verifies the real production guard rejects it.
        capacity = patch.object(device.shutil, 'disk_usage', return_value=SimpleNamespace(free=10*1024**3))
        capacity.start()
        self.addCleanup(capacity.stop)
        self.native = self.root / "evidence/runs/F05/synthetic-test/attempt-01/backend-cases.json"
        self.argv = ["qualify", "--scope", "xr-probe", "--models", "quest3,quest3s", "--native-cases", str(self.native)]
        self.helper = supplemental.ProbeTests("test_valid_structure_never_accepts_task")
        self.helper.root = self.root
        self.store = self.helper.store
        self.probe = self.helper.fixture()
        identity = self.probe["identity"]
        source = json.loads((self.root / identity["source_manifest"]["path"]).read_bytes())
        for name, relative in (("project_version", "ProjectSettings/ProjectVersion.txt"),
                               ("package_manifest", "Packages/manifest.json"),
                               ("package_lock", "Packages/packages-lock.json")):
            original = identity[name]
            ref = self.store("synthetic/snapshot/game/" + relative, (self.root / original["path"]).read_bytes())
            source["files"].append({"path": ref["path"], "sha256": ref["sha256"], "byte_length": ref["size_bytes"]})
        identity["source_manifest"] = self.store(identity["source_manifest"]["path"], source)
        self.f04 = json.loads((self.root / self.probe["f04_result"]["path"]).read_bytes())
        self.f04["identity"]["source_identity"] = "sha256:" + identity["source_manifest"]["sha256"]
        for name in ("source_manifest", "content_manifest", "toolchain_lock"):
            self.f04["identity"][name] = self.root_ref(identity[name])
        self.f04["fixture_label"] = "SYNTHETIC: root verifier mocked; cannot be accepted F04 evidence"
        lock = self.root_ref(identity["toolchain_lock"])
        self.f04.update(output_bindings=[{"expected_path": "automation/toolchain.lock.json", "artifact_path": lock["path"]}],
                        artifacts=[lock])
        self.probe["f04_result"] = self.store(self.probe["f04_result"]["path"], self.f04)
        self.probe["candidate_sha256"] = device.canonical_hash(identity)
        for session in self.probe["sessions"]:
            session["candidate_sha256"] = self.probe["candidate_sha256"]
            ref = session["runtime_log"]
            log = json.loads((self.root / ref["path"]).read_bytes())
            log["candidate_sha256"] = self.probe["candidate_sha256"]
            session["runtime_log"] = self.store(ref["path"], log)
        state = {"task_states": {"F04": {"status": "passed", "result_path": self.probe["f04_result"]["path"],
                                       "result_sha256": self.probe["f04_result"]["sha256"]}}}
        self.store("automation/task_state.json", state)
        contract = device.ROOT / "specification/content/acceptance_cases.json"
        frozen = json.loads(contract.read_bytes())
        self.store("specification/content/acceptance_cases.json", {"contracts": [row for row in frozen["contracts"] if row["id"] == "check-f05"]})
        self.packet = {"schema_version": 1, "record_type": "resq-reviewed-xr-probe-input", "probe": self.probe,
                       "provenance_reviews": []}
        self.refresh_reviews()
        self.verifier = patch("validate_spec_freeze.validate_task_result")
        self.mock_verifier = self.verifier.start()
        self.addCleanup(self.verifier.stop)
        self.save()

    @staticmethod
    def root_ref(ref):
        return {"path": ref["path"], "sha256": ref["sha256"], "byte_length": ref["size_bytes"], "role": "synthetic_fixture"}

    def refresh_reviews(self):
        self.packet["provenance_reviews"] = []
        for session in self.probe["sessions"]:
            review = {"schema_version": 1, "record_type": "resq-physical-probe-provenance-review",
                      "decision": "accepted_actual_provenance", "review_basis": "direct_inspection_of_original_records",
                      "reviewer_kind": "authorized_independent_reviewer", "reviewer_alias": "SYNTHETIC-INDEPENDENT",
                      "reviewed_utc": "2026-10-16T00:02:00Z", "review_notes": "SYNTHETIC TEST ONLY; no actual inspection occurred",
                      "probe_sha256": device.canonical_hash(self.probe), "candidate_sha256": self.probe["candidate_sha256"],
                      "f04_result": self.probe["f04_result"],
                      "reviewed_artifacts": {key: session[key] for key in device.SESSION_ARTIFACTS},
                      "checks": {key: True for key in device.REVIEW_CHECKS}}
            for field in ("model", "session_id", "device_alias", "os_build", "started_utc", "ended_utc"):
                review[field] = session[field]
            self.packet["provenance_reviews"].append(self.store("synthetic/" + session["model"] + "/review.json", review))

    def save(self):
        self.store(device.INLET, self.packet)

    def review_mutation(self, field, value):
        ref = self.packet["provenance_reviews"][0]
        review = json.loads((self.root / ref["path"]).read_bytes())
        review[field] = value
        self.packet["provenance_reviews"][0] = self.store(ref["path"], review)
        self.save()

    def reject(self, message=None):
        self.save()
        with self.assertRaises((ValueError, OSError, KeyError, TypeError)) as caught:
            device.execute(self.root, self.argv)
        if message:
            self.assertIn(message, str(caught.exception))
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root / device.OUTPUT).exists())

    def test_missing_actual_inlet_blocks_without_success(self):
        (self.root / device.INLET).unlink()
        with self.assertRaises(device.MissingActualInput):
            device.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root / device.OUTPUT).exists())

    def test_malformed_json_and_wrong_record_rejected(self):
        for data in (b"{", b"[]", b'{"schema_version":1,"record_type":"wrong"}'):
            (self.root / device.INLET).write_bytes(data)
            with self.assertRaises((ValueError, AttributeError)):
                device.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())

    def test_unsupported_operation_does_not_write(self):
        for argv in (["discover"], self.argv + ["--pretend"], [*self.argv[:4], "quest3", *self.argv[5:]]):
            with self.assertRaises(device.UnsupportedOperation):
                device.execute(self.root, argv)
        self.assertFalse(self.native.exists())

    def test_destination_wrong_task_escape_or_wrong_name_rejected(self):
        for path in (self.root.parent / "backend-cases.json", self.root / "evidence/runs/F06/run/attempt/backend-cases.json",
                     self.native.parent / "other.json", self.root / "evidence/runs/F05/backend-cases.json"):
            with self.assertRaises(ValueError):
                device.execute(self.root, self.argv[:-1] + [str(path)])

    def test_symlinked_artifact_rejected_even_inside_workspace(self):
        capture = self.root / self.probe["sessions"][0]["capture"]["path"]
        target = self.root / "synthetic/alternate-capture.txt"
        target.write_bytes(capture.read_bytes())
        capture.unlink()
        capture.symlink_to(target)
        self.reject("Linked")

    def test_stale_f04_accepted_head_rejected(self):
        state = json.loads((self.root / "automation/task_state.json").read_bytes())
        state["task_states"]["F04"]["result_sha256"] = "0" * 64
        self.store("automation/task_state.json", state)
        self.reject("accepted head")

    def test_independent_f04_verifier_failure_rejected(self):
        self.mock_verifier.side_effect = ValueError("synthetic root verifier rejection")
        self.reject("root verifier rejection")

    def test_candidate_changed_rejected(self):
        self.probe["identity"]["version_code"] = "2"
        self.reject("candidate envelope changed")

    def test_stale_review_packet_binding_rejected(self):
        self.review_mutation("probe_sha256", "0" * 64)
        self.reject("candidate/packet/F04")

    def test_stale_review_candidate_rejected(self):
        self.review_mutation("candidate_sha256", "0" * 64)
        self.reject("candidate/packet/F04")

    def test_stale_review_f04_rejected(self):
        self.review_mutation("f04_result", dict(self.probe["f04_result"], sha256="0" * 64))
        self.reject("candidate/packet/F04")

    def test_stale_review_session_identity_rejected(self):
        self.review_mutation("session_id", "SYNTHETIC-OTHER")
        self.reject("session identity")

    def test_stale_review_capture_rejected(self):
        review = json.loads((self.root / self.packet["provenance_reviews"][0]["path"]).read_bytes())
        review["reviewed_artifacts"]["capture"]["sha256"] = "0" * 64
        self.review_mutation("reviewed_artifacts", review["reviewed_artifacts"])
        self.reject("bind capture")

    def test_self_review_rejected(self):
        self.review_mutation("reviewer_alias", self.probe["sessions"][0]["observer_alias"])
        self.reject("Independent")

    def test_incomplete_actual_provenance_checks_rejected(self):
        checks = {key: True for key in device.REVIEW_CHECKS}
        checks["consent_scope_confirmed"] = "true"
        self.review_mutation("checks", checks)
        self.reject("Incomplete actual")

    def test_review_before_completed_session_rejected(self):
        self.review_mutation("reviewed_utc", "2026-10-16T00:00:01Z")
        self.reject("precedes")

    def test_future_provenance_review_is_rejected(self):
        self.review_mutation('reviewed_utc', '2026-10-17T00:00:00Z')
        self.reject('future')

    def test_retention_closure_normalizes_owned_logs_and_skips_external_tools(self):
        log = self.store('synthetic/owned-native-log.txt', b'synthetic native log')
        absolute_log = dict(log, path=str(self.root/log['path']))
        external = self.root.parent/'outside-native-sdk'
        records = device.artifact_closure(self.root, [absolute_log, {'path':str(external),'sha256':'0'*64,'size_bytes':99}])
        self.assertEqual(records, [log])

    def test_evidence_copy_reserve_blocks_before_native_success(self):
        with patch.object(device.shutil, 'disk_usage', return_value=SimpleNamespace(free=4*1024**3)):
            with self.assertRaises(device.EvidenceStorageBlocked):
                device.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root/device.OUTPUT).exists())

    def test_missing_review_rejected(self):
        self.packet["provenance_reviews"].pop()
        self.reject("Two independent")

    def test_missing_consent_rejected(self):
        del self.probe["sessions"][0]["consent"]
        self.reject()

    def test_missing_capture_rejected(self):
        (self.root / self.probe["sessions"][0]["capture"]["path"]).unlink()
        self.reject()

    def test_simulator_cannot_qualify(self):
        self.probe["sessions"][0]["runtime"] = "simulator"
        self.reject("does not qualify")

    def test_both_models_required(self):
        self.probe["sessions"].pop()
        self.reject("two physical")

    def test_missing_observation_rejected(self):
        self.probe["sessions"][1]["observations"].pop()
        self.reject("missing/duplicate")

    def test_f04_package_snapshot_mismatch_rejected(self):
        source_ref = self.probe["identity"]["source_manifest"]
        source = json.loads((self.root / source_ref["path"]).read_bytes())
        source["files"][3]["path"] = source["files"][0]["path"]
        source["files"][3].update(sha256=source["files"][0]["sha256"], byte_length=source["files"][0]["byte_length"])
        self.probe["identity"]["source_manifest"] = self.store(source_ref["path"], source)
        self.reject()

    def test_retained_capture_tampering_cannot_emit_success(self):
        original = device.retain
        def tamper(root, destination, references):
            retained = original(root, destination, references)
            capture = next(row for row in retained if row["original_path"] == self.probe["sessions"][0]["capture"]["path"])
            (root / capture["path"]).write_bytes(b"tampered retained capture")
            return retained
        with patch.object(device, "retain", side_effect=tamper):
            self.reject("mismatch")

    def test_valid_synthetic_structure_emits_exact_proofs_and_both_session_assertions(self):
        state_before = (self.root / "automation/task_state.json").read_bytes()
        self.assertEqual(device.execute(self.root, self.argv), self.native)
        packet = json.loads(self.native.read_bytes())
        self.assertFalse(packet["accepted_task"])
        self.assertEqual(packet["candidate_identity"], self.f04["identity"])
        self.assertEqual(len(packet["proof_records"]), 2)
        proof_schema = json.loads((device.ROOT / "specification/schemas/task_result.schema.json").read_bytes())["properties"]["proof_records"]["items"]
        for proof in packet["proof_records"]:
            schema_check(proof, proof_schema)
            self.assertTrue(proof["capture_path"].startswith("evidence/runs/F05/synthetic-test/attempt-01/probe-inputs/"))
        self.assertEqual(len(packet["case_outcomes"]), 4)
        for case in packet["case_outcomes"]:
            self.assertIn(case["proof_index"], (0, 1))
            self.assertEqual({session["model"] for session in case["observations"]["both_model_sessions"]}, set(device.MODELS))
        retention = json.loads((self.root / packet["retained_evidence"]["path"]).read_bytes())
        names = {row["original_path"] for row in retention["immutable_inputs"]}
        self.assertIn(device.INLET, names)
        self.assertIn(self.probe["f04_result"]["path"], names)
        for row in retention["immutable_inputs"]:
            self.assertEqual(hashlib.sha256((self.root / row["path"]).read_bytes()).hexdigest(), row["sha256"])
        self.assertEqual((self.root / "automation/task_state.json").read_bytes(), state_before)
        self.mock_verifier.assert_called_with(self.root, self.f04, expected_task="F04", verify_current_gates=False)

    def test_conflicting_expected_output_preserved(self):
        output = self.root / device.OUTPUT
        output.write_bytes(b'{"synthetic_prior":true}')
        with self.assertRaises(FileExistsError):
            device.execute(self.root, self.argv)
        self.assertEqual(output.read_bytes(), b'{"synthetic_prior":true}')
        self.assertFalse(self.native.exists())

    def test_existing_native_output_preserved(self):
        self.native.parent.mkdir(parents=True)
        self.native.write_bytes(b"synthetic existing native bytes")
        with self.assertRaises(FileExistsError):
            device.execute(self.root, self.argv)
        self.assertEqual(self.native.read_bytes(), b"synthetic existing native bytes")

    def test_main_missing_actual_input_75_invalid_actual_artifact_1(self):
        with patch.object(device, "ROOT", self.root), patch("sys.stdout", new=io.StringIO()):
            (self.root / device.INLET).unlink()
            self.assertEqual(device.main(self.argv), 75)
            self.save()
            (self.root / self.probe["sessions"][0]["capture"]["path"]).unlink()
            self.assertEqual(device.main(self.argv), 1)
        self.assertFalse(self.native.exists())


if __name__ == "__main__":
    unittest.main()
