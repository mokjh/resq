"""Adapter security tests. Temporary synthetic fixtures are never F06 evidence."""
import hashlib
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

from backends import art


class ArtBackendTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        self.native = self.root / "evidence/runs/F06/synthetic-test/attempt-01/backend-cases.json"
        self.argv = ["audit", "--kit", "calibration", "--native-cases", str(self.native)]

    def prepare_synthetic_inlet(self):
        inlet = self.root / "evidence/calibration/qualification-input.json"
        inlet.parent.mkdir(parents=True)
        inlet.write_text(json.dumps({"fixture": "synthetic adapter test only"}))
        contract = art.ROOT / "specification/content/acceptance_cases.json"
        target = self.root / contract.relative_to(art.ROOT)
        target.parent.mkdir(parents=True)
        target.write_bytes(contract.read_bytes())
        return inlet

    def result(self):
        record = self.root / "evidence/calibration/synthetic-native.txt"
        record.parent.mkdir(parents=True, exist_ok=True)
        record.write_bytes(b"Synthetic adapter fixture; never native game evidence.\n")
        return {"status": "complete_structural_validation", "accepted_task": False,
                "assertions": {"check-f06-a0" + str(i): {"fixture": "synthetic assertion " + str(i)}
                               for i in range(1, 5)}, "artifacts": [
                    {"path": str(record.relative_to(self.root)), "sha256": hashlib.sha256(record.read_bytes()).hexdigest(),
                     "size_bytes": record.stat().st_size}]}

    def module(self, result=None, error=None):
        module = types.ModuleType("art.qualify_calibration")
        def qualify(root, packet):
            self.assertEqual(root, self.root)
            self.assertEqual(packet, {"fixture": "synthetic adapter test only"})
            if error:
                raise error
            return result
        module.qualify = qualify
        return patch.dict("sys.modules", {"art.qualify_calibration": module})

    def test_unregistered_operations_do_not_write(self):
        for argv in (["generate"], self.argv + ["--pretend"],
                     ["audit", "--kit", "village", *self.argv[3:]]):
            with self.assertRaises(ValueError):
                art.execute(self.root, argv)
        self.assertFalse(self.native.exists())

    def test_output_escape_and_wrong_task_are_rejected(self):
        for value in (str(self.root.parent / "backend-cases.json"),
                      str(self.root / "evidence/runs/F05/test/attempt/backend-cases.json"),
                      str(self.native.parent / "other.json"),
                      str(self.root / "evidence/../backend-cases.json")):
            with self.assertRaises(ValueError):
                art.execute(self.root, self.argv[:-1] + [value])

    def test_symlinked_parent_is_rejected(self):
        (self.root / "evidence/runs").mkdir(parents=True)
        actual = self.root / "other"
        actual.mkdir()
        (self.root / "evidence/runs/F06").symlink_to(actual, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Linked"):
            art.execute(self.root, self.argv)

    def test_missing_actual_input_creates_no_native_success(self):
        with self.assertRaisesRegex(FileNotFoundError, "Missing actual"):
            art.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root / "evidence/calibration/audit.json").exists())

    def test_qualification_failure_creates_no_native_success(self):
        self.prepare_synthetic_inlet()
        with self.module(error=ValueError("Missing real Unity pixels")):
            with self.assertRaisesRegex(ValueError, "Unity pixels"):
                art.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root / "evidence/calibration/audit.json").exists())

    def test_incomplete_assertions_cannot_be_promoted(self):
        self.prepare_synthetic_inlet()
        value = self.result()
        value["assertions"].pop("check-f06-a04")
        with self.module(value), self.assertRaisesRegex(ValueError, "Complete exact"):
            art.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())

    def test_adapter_does_not_accept_tasks_or_rewrite_existing_native_evidence(self):
        self.prepare_synthetic_inlet()
        with self.module(self.result()):
            self.assertEqual(art.execute(self.root, self.argv), self.native)
            packet = json.loads(self.native.read_text())
            self.assertFalse(packet["accepted_task"])
            self.assertEqual(len(packet["case_outcomes"]), 4)
            self.assertEqual({r["dimensions"]["assertion_id"] for r in packet["case_outcomes"]},
                             set(self.result()["assertions"]))
            retention = json.loads((self.root / packet["retained_evidence"]["path"]).read_text())
            self.assertEqual(len(retention["immutable_inputs"]), 2)
            for reference in retention["immutable_inputs"]:
                self.assertEqual(hashlib.sha256((self.root / reference["path"]).read_bytes()).hexdigest(), reference["sha256"])
            before = self.native.read_bytes()
            with self.assertRaises(FileExistsError):
                art.execute(self.root, self.argv)
            self.assertEqual(self.native.read_bytes(), before)
        self.assertFalse((self.root / "automation/task_state.json").exists())

    def test_conflicting_retained_audit_is_preserved(self):
        self.prepare_synthetic_inlet()
        audit = self.root / "evidence/calibration/audit.json"
        audit.write_text('{"fixture":"older synthetic audit"}\n')
        before = audit.read_bytes()
        with self.module(self.result()), self.assertRaisesRegex(FileExistsError, "Retained"):
            art.execute(self.root, self.argv)
        self.assertEqual(audit.read_bytes(), before)
        self.assertFalse(self.native.exists())

    def test_changed_validated_artifact_is_rejected_before_success(self):
        self.prepare_synthetic_inlet()
        value = self.result()
        (self.root / value["artifacts"][0]["path"]).write_bytes(b"tampered after qualification")
        with self.module(value), self.assertRaisesRegex(ValueError, "changed before retention"):
            art.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())
        self.assertFalse((self.root / "evidence/calibration/audit.json").exists())

    def test_no_retained_native_inputs_cannot_produce_success(self):
        self.prepare_synthetic_inlet()
        value = self.result()
        value["artifacts"] = []
        with self.module(value), self.assertRaisesRegex(ValueError, "must be retained"):
            art.execute(self.root, self.argv)
        self.assertFalse(self.native.exists())

    def test_identical_audit_can_be_reverified_in_a_fresh_attempt(self):
        self.prepare_synthetic_inlet()
        value = self.result()
        with self.module(value):
            art.execute(self.root, self.argv)
            prior = (self.root / "evidence/calibration/audit.json").read_bytes()
            other = self.native.parent.parent / "attempt-02/backend-cases.json"
            art.execute(self.root, self.argv[:-1] + [str(other)])
            self.assertTrue(other.is_file())
            self.assertEqual((self.root / "evidence/calibration/audit.json").read_bytes(), prior)
            first = json.loads(self.native.read_text())["retained_evidence"]["path"]
            second = json.loads(other.read_text())["retained_evidence"]["path"]
            self.assertNotEqual(first, second)


if __name__ == "__main__":
    unittest.main()
