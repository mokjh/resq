#!/usr/bin/env python3
"""Synthetic, isolated validator tests; no test fixture is device evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from foundation_probe import EVENTS, Invalid, artifact, audit, canonical_hash, exclusive_json, metadata, validate_probe
from runner_core import ReportError


class ProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="resq-synthetic-probe-")
        self.root = Path(self.temp.name).resolve()
        self.addCleanup(self.temp.cleanup)
        self.packet = self.fixture()

    def store(self, name, data):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data if isinstance(data, bytes) else json.dumps(data).encode())
        ref = metadata(path)
        ref["path"] = name
        return ref

    def fixture(self):
        payload = self.store("synthetic/content.json", {"synthetic_fixture_only": True})
        source = self.store("synthetic/source.cs", b"// synthetic only")
        nested = lambda ref: {"path": ref["path"], "sha256": ref["sha256"], "byte_length": ref["size_bytes"]}
        identity = {
            "source_manifest": self.store("synthetic/source-manifest.json", {"files": [nested(source)]}),
            "content_manifest": self.store("synthetic/content-manifest.json", {"payload_sha256": payload["sha256"], "files": [nested(payload)]}),
            "toolchain_lock": self.store("synthetic/toolchain.json", {"synthetic_fixture_only": True}),
            "project_version": self.store("synthetic/ProjectVersion.txt", b"m_EditorVersion: synthetic-test\n"),
            "package_manifest": self.store("synthetic/manifest.json", {"dependencies": {"synthetic": "1"}}),
            "package_lock": self.store("synthetic/packages-lock.json", {"dependencies": {"synthetic": {"version": "1"}}}),
            "build_id": "synthetic-build", "package_id": "invalid.synthetic.test", "version_code": "1",
        }
        apk_path = self.root / "synthetic/test.apk"
        with zipfile.ZipFile(apk_path, "w") as apk:
            apk.writestr("AndroidManifest.xml", b"SYNTHETIC UNIT TEST ONLY")
            apk.writestr("lib/arm64-v8a/libil2cpp.so", b"\x7fELF\x02\x01" + bytes(12) + b"\xb7\x00")
        identity["apk"] = metadata(apk_path); identity["apk"]["path"] = "synthetic/test.apk"
        identity["build_report"] = self.store("synthetic/build-report.json", {
            "result": "Succeeded", "total_errors": 0, "total_size": apk_path.stat().st_size,
            "output_path": "test.apk", "apk_byte_length": apk_path.stat().st_size,
            "apk_sha256": identity["apk"]["sha256"], "platform": "Android", "build_guid": identity["build_id"]})
        candidate = canonical_hash(identity)
        packet = {"schema_version": 1, "record_type": "resq-xr-probe-observations", "evidence_kind": "physical_device",
                  "freeze_id": "resq-spec-freeze-1.1.0", "rules_version": "resq-rules-1",
                  "identity": identity, "candidate_sha256": candidate, "sessions": []}
        packet["f04_result"] = self.store("synthetic/f04.json", {"task_id": "F04", "status": "passed", "identity": {
            "build_sha256": identity["apk"]["sha256"], "toolchain_hash": identity["toolchain_lock"]["sha256"],
            "source_identity": "sha256:" + identity["source_manifest"]["sha256"], "content_hash": payload["sha256"]}})
        for model, label in (("quest3", "Quest 3"), ("quest3s", "Quest 3S")):
            events = [{"event_id": key, "result": "passed", "sequence": i, "monotonic_ms": i * 1000,
                       "observed_utc": "2026-10-16T00:00:01Z"} for i, key in enumerate(EVENTS)]
            session = {"model": model, "evidence_kind": "physical_device", "runtime": "standalone_apk",
                       "candidate_sha256": candidate, "session_id": "SYNTHETIC-" + model, "device_alias": "SYNTHETIC-" + model,
                       "os_build": "SYNTHETIC", "observer_alias": "SYNTHETIC",
                       "started_utc": "2026-10-16T00:00:00Z", "ended_utc": "2026-10-16T00:01:00Z",
                       "observations": [{"event_id": key, "result": "observed_pass", "observed_behavior": "SYNTHETIC UNIT TEST ONLY",
                                         "observed_utc": "2026-10-16T00:00:01Z", "capture_offset_ms": i * 1000}
                                        for i, key in enumerate(EVENTS)]}
            for role in ("device_authorization", "consent", "capture"):
                session[role] = self.store(f"synthetic/{model}/{role}.txt", b"SYNTHETIC UNIT TEST ONLY")
            session["runtime_log"] = self.store(f"synthetic/{model}/log.json", {"candidate_sha256": candidate,
                "session_id": session["session_id"], "device_alias": session["device_alias"], "events": events})
            session["device_identity"] = self.store(f"synthetic/{model}/device.json", {
                "model": label, "device_alias": session["device_alias"], "os_build": session["os_build"],
                "evidence_kind": "physical_device", "installed_package_id": identity["package_id"],
                "installed_version_code": identity["version_code"], "installed_apk_sha256": identity["apk"]["sha256"]})
            packet["sessions"].append(session)
        return packet

    def reject(self, packet=None):
        with self.assertRaises((Invalid, ValueError, ReportError)):
            validate_probe(self.root, self.packet if packet is None else packet)

    def test_valid_structure_never_accepts_task(self):
        result = validate_probe(self.root, self.packet)
        self.assertFalse(result["accepted_task"])
        self.assertEqual(result["status"], "structural_validation_only")
        self.assertEqual(result["observations_checked"], 20)

    def test_empty_packet_rejected(self):
        self.reject({})

    def test_missing_model_rejected(self):
        self.packet["sessions"].pop(); self.reject()

    def test_duplicate_model_rejected(self):
        self.packet["sessions"][1]["model"] = "quest3"; self.reject()

    def test_simulator_link_editor_rejected(self):
        for runtime in ("simulator", "link", "editor"):
            packet = copy.deepcopy(self.packet); packet["sessions"][0]["runtime"] = runtime; self.reject(packet)

    def test_changed_candidate_rejected(self):
        self.packet["sessions"][0]["candidate_sha256"] = "0" * 64; self.reject()

    def test_identity_envelope_tampering_rejected(self):
        self.packet["identity"]["version_code"] = "2"; self.reject()

    def test_f04_different_lock_rejected(self):
        reference = self.packet["f04_result"]; data = json.loads((self.root / reference["path"]).read_text())
        data["identity"]["toolchain_hash"] = "0" * 64
        self.packet["f04_result"] = self.store(reference["path"], data); self.reject()

    def test_failed_native_report_rejected(self):
        reference = self.packet["identity"]["build_report"]; data = json.loads((self.root / reference["path"]).read_text())
        data["result"] = "Failed"
        self.packet["identity"]["build_report"] = self.store(reference["path"], data)
        self.packet["candidate_sha256"] = canonical_hash(self.packet["identity"]); self.reject()

    def test_text_file_disguised_as_apk_rejected(self):
        reference = self.packet["identity"]["apk"]
        self.packet["identity"]["apk"] = self.store(reference["path"], b"NOT AN APK")
        report = self.packet["identity"]["build_report"]; data = json.loads((self.root / report["path"]).read_text())
        data.update(apk_sha256=self.packet["identity"]["apk"]["sha256"], apk_byte_length=10)
        self.packet["identity"]["build_report"] = self.store(report["path"], data)
        self.packet["candidate_sha256"] = canonical_hash(self.packet["identity"]); self.reject()

    def test_manifest_missing_snapshot_file_rejected(self):
        (self.root / "synthetic/source.cs").unlink(); self.reject()

    def test_installed_wrong_apk_rejected(self):
        session = self.packet["sessions"][0]
        data = json.loads((self.root / session["device_identity"]["path"]).read_text())
        data["installed_apk_sha256"] = "0" * 64
        session["device_identity"] = self.store(session["device_identity"]["path"], data); self.reject()

    def test_missing_observation_rejected(self):
        self.packet["sessions"][0]["observations"].pop(); self.reject()

    def test_unknown_observation_rejected(self):
        self.packet["sessions"][0]["observations"][0]["result"] = "unknown"; self.reject()

    def test_artifact_tampering_rejected(self):
        (self.root / self.packet["sessions"][0]["capture"]["path"]).write_bytes(b"TAMPERED"); self.reject()

    def test_runtime_model_binding_rejected(self):
        session = self.packet["sessions"][0]
        data = json.loads((self.root / session["device_identity"]["path"]).read_text()); data["model"] = "Quest 3S"
        session["device_identity"] = self.store(session["device_identity"]["path"], data); self.reject()

    def test_missing_native_event_rejected(self):
        session = self.packet["sessions"][0]; data = json.loads((self.root / session["runtime_log"]["path"]).read_text())
        data["events"].pop(); session["runtime_log"] = self.store(session["runtime_log"]["path"], data); self.reject()

    def test_reversed_fallback_order_rejected(self):
        session = self.packet["sessions"][0]; data = json.loads((self.root / session["runtime_log"]["path"]).read_text())
        data["events"][5]["event_id"], data["events"][6]["event_id"] = data["events"][6]["event_id"], data["events"][5]["event_id"]
        session["runtime_log"] = self.store(session["runtime_log"]["path"], data); self.reject()

    def test_missing_consent_rejected(self):
        del self.packet["sessions"][0]["consent"]; self.reject()

    def test_observation_outside_session_rejected(self):
        self.packet["sessions"][0]["observations"][0]["observed_utc"] = "2026-10-16T03:00:00Z"; self.reject()

    def test_absolute_traversal_and_symlink_rejected(self):
        for path in ("../escape", str(self.root / "synthetic/test.apk")):
            with self.assertRaises(Invalid): artifact(self.root, {"path": path})
        with tempfile.TemporaryDirectory() as outside:
            file = Path(outside) / "file"; file.write_bytes(b"outside")
            (self.root / "escape").symlink_to(file)
            with self.assertRaises(Invalid): artifact(self.root, {"path": "escape", "size_bytes": 7})

    def test_report_no_overwrite(self):
        path = self.root / "report.json"; exclusive_json(path, {"one": 1})
        with self.assertRaises(FileExistsError): exclusive_json(path, {"two": 2})
        self.assertEqual(json.loads(path.read_text()), {"one": 1})

    def test_audit_empty_environment_records_missing(self):
        result = audit(self.root, self.root / "no-editors", self.root / "no-home")
        self.assertEqual(result["editors"], [])
        self.assertEqual(result["status"], "preparation_only")
        self.assertTrue(all(not row["exists"] for row in result["package_cache_observations"]))


if __name__ == "__main__":
    unittest.main()
