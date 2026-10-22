"""Real subprocess and parser regressions; fixtures do not qualify Unity/device builds."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
from runner_core import (GIB, LockBusy, ReportError, ResourceLock, RunnerError,
                         StorageBlocked, check_storage, crosscheck_status,
                         parse_build_report, parse_nunit_xml, resource_locks,
                         run_process, sha256_file, write_json_exclusive)


class RunnerCoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="resq-runner-tests-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, value):
        path = self.root / name
        path.write_text(value, encoding="utf-8")
        return path

    def execute(self, code, **kwargs):
        argv = [sys.executable, "-B", "-c", code]
        return run_process(argv, cwd=self.root, stdout_path=self.root / "stdout.log",
                           stderr_path=self.root / "stderr.log", allowed_commands=[argv],
                           timeout_seconds=kwargs.pop("timeout_seconds", 5), owner="unit-test",
                           outcome_path=self.root / "outcome.json", **kwargs)

    def native(self, leaves, attributes=""):
        return self.write("native.xml", '<test-run ' + attributes + '>' + leaves + '</test-run>')

    def build(self, **changes):
        apk = self.root / "actual.apk"
        apk.write_bytes(b"fixture APK bytes; no Unity claim")
        data = {"result": "Succeeded", "total_errors": 0, "total_size": apk.stat().st_size,
                "output_path": str(apk), "apk_sha256": sha256_file(apk),
                "apk_byte_length": apk.stat().st_size}
        data.update(changes)
        return self.write("build.json", json.dumps(data))

    def test_lock_records_actual_owner_and_releases_on_exception(self):
        lock = ResourceLock(self.root, "unity-project", "specialist")
        with self.assertRaisesRegex(RuntimeError, "deliberate"):
            with lock:
                record = json.loads(lock.path.read_text())
                self.assertEqual(record["pid"], os.getpid())
                self.assertEqual(record["cwd"], str(Path.cwd().resolve()))
                self.assertEqual(record["owner"], "specialist")
                raise RuntimeError("deliberate")
        self.assertFalse(lock.path.exists())

    def test_lock_contends_across_real_processes(self):
        with ResourceLock(self.root, "heavy-operation", "parent"):
            code = ("import sys; sys.path.insert(0,sys.argv[1]); "
                    "from runner_core import ResourceLock,LockBusy\n"
                    "try:\n ResourceLock(sys.argv[2], 'heavy-operation', 'child').acquire()\n"
                    "except LockBusy:\n sys.exit(19)\n"
                    "sys.exit(0)\n")
            argv = [sys.executable, "-B", "-c", code, str(Path(__file__).resolve().parent), str(self.root)]
            result = run_process(argv, cwd=self.root, stdout_path=self.root / "child.out",
                                 stderr_path=self.root / "child.err", allowed_commands=[argv],
                                 timeout_seconds=5, owner="lock-test")
            self.assertEqual(result["exit_code"], 19)

    def test_stale_lock_is_preserved(self):
        lock = ResourceLock(self.root, "stale", "new-owner")
        lock.path.write_text('{"pid":99999999,"owner":"foreign"}')
        before = lock.path.read_bytes()
        with self.assertRaises(LockBusy):
            lock.acquire()
        self.assertEqual(lock.path.read_bytes(), before)

    def test_replaced_ownership_is_never_removed(self):
        lock = ResourceLock(self.root, "replace", "owner").acquire()
        lock.path.unlink()
        lock.path.write_text('{"pid":1,"token":"foreign"}')
        with self.assertRaises(LockBusy):
            lock.release()
        self.assertTrue(lock.path.exists())

    def test_multi_lock_rolls_back_own_acquisitions_on_contention(self):
        with ResourceLock(self.root, "z-occupied", "foreign") as foreign:
            with self.assertRaises(LockBusy):
                with resource_locks(self.root, ["a-free", "z-occupied"], "caller"):
                    self.fail("contention should prevent entry")
            self.assertTrue(foreign.path.exists())
            self.assertFalse(ResourceLock(self.root, "a-free", "check").path.exists())

    def test_output_and_actual_failure_retained(self):
        result = self.execute("import sys; print('real stdout'); print('real stderr',file=sys.stderr); sys.exit(7)",
                              process_record_path=self.root / "process.json")
        self.assertEqual(result["exit_code"], 7)
        self.assertEqual(result["status"], "failed")
        self.assertEqual((self.root / "stdout.log").read_text(), "real stdout\n")
        self.assertEqual((self.root / "stderr.log").read_text(), "real stderr\n")
        self.assertEqual(json.loads((self.root / "process.json").read_text())["pid"], result["pid"])
        self.assertEqual(json.loads((self.root / "outcome.json").read_text()), result)

    def test_shell_metacharacters_are_literal_argv(self):
        code = "import sys; print(sys.argv[1])"
        literal = "$(touch sentinel); `touch sentinel2`"
        argv = [sys.executable, "-B", "-c", code, literal]
        result = run_process(argv, cwd=self.root, stdout_path=self.root / "out",
                             stderr_path=self.root / "err", allowed_commands=[argv],
                             timeout_seconds=5, owner="literal-test")
        self.assertEqual(result["status"], "passed")
        self.assertEqual((self.root / "out").read_text().strip(), literal)
        self.assertFalse((self.root / "sentinel").exists())
        self.assertFalse((self.root / "sentinel2").exists())

    def test_exact_allowlist_rejects_extra_argument_before_launch(self):
        argv = [sys.executable, "-c", "print('not admitted')"]
        with self.assertRaises(RunnerError):
            run_process(argv + ["extra"], cwd=self.root, stdout_path=self.root / "out",
                        stderr_path=self.root / "err", allowed_commands=[argv],
                        timeout_seconds=5, owner="test")
        self.assertFalse((self.root / "out").exists())

    def test_timeout_kills_own_group_preserves_unrelated_process(self):
        unrelated = subprocess.Popen([sys.executable, "-B", "-c", "import time; time.sleep(30)"],
                                     start_new_session=True)
        def cleanup():
            if unrelated.poll() is None:
                unrelated.terminate()
            unrelated.wait(timeout=5)
        self.addCleanup(cleanup)
        code = ("import subprocess,sys,time; "
                "p=subprocess.Popen([sys.executable,'-B','-c',"
                "\"import time; from pathlib import Path; time.sleep(1.5); Path('escaped').write_text('bad')\"]); "
                "print(p.pid,flush=True); time.sleep(30)")
        result = self.execute(code, timeout_seconds=0.25)
        self.assertTrue(result["timed_out"])
        self.assertNotEqual(result["exit_code"], 0)
        self.assertEqual(result["status"], "failed")
        self.assertIsNone(unrelated.poll())
        # This wait proves the owned child cannot perform its delayed action.
        time.sleep(1.6)
        self.assertFalse((self.root / "escaped").exists())

    def test_output_redaction_and_writer_error_fail_closed(self):
        result = self.execute("print('secret-token')", redactor=lambda text: text.replace("secret-token", "REDACTED"))
        self.assertEqual(result["status"], "passed")
        self.assertEqual((self.root / "stdout.log").read_text(), "REDACTED\n")

    def test_redactor_error_cannot_return_pass(self):
        def broken(text):
            raise ValueError("redaction failed")
        result = self.execute("print('text')", redactor=broken)
        self.assertEqual(result["exit_code"], 0)
        self.assertEqual(result["status"], "failed")
        self.assertTrue(result["output_errors"])

    def test_launch_error_is_not_a_success_exit(self):
        argv = [str(self.root / "does-not-exist")]
        result = run_process(argv, cwd=self.root, stdout_path=self.root / "out",
                             stderr_path=self.root / "err", allowed_commands=[argv],
                             timeout_seconds=5, owner="test")
        self.assertIsNone(result["exit_code"])
        self.assertEqual(result["status"], "failed")
        self.assertIsNotNone(result["launch_error"])

    def test_existing_outputs_are_never_overwritten(self):
        path = self.write("stdout.log", "preserved")
        with self.assertRaises(FileExistsError):
            self.execute("print('new')")
        self.assertEqual(path.read_text(), "preserved")
        record = self.root / "record.json"
        write_json_exclusive(record, {"first": True})
        with self.assertRaises(FileExistsError):
            write_json_exclusive(record, {"second": True})
        self.assertEqual(json.loads(record.read_text()), {"first": True})

    def test_native_cases_and_counters_are_derived(self):
        path = self.native('<test-case fullname="a" result="Passed"/><test-case fullname="b" result="Passed"/>',
                           'total="2" passed="2" failed="0" skipped="0" result="Passed"')
        data = parse_nunit_xml(path)
        self.assertEqual(data["total"], 2)
        self.assertEqual(data["executed"], 2)
        self.assertEqual(data["status"], "passed")
        self.assertEqual([row["case_id"] for row in data["cases"]], ["a", "b"])

    def test_native_skip_and_failure_prevent_pass(self):
        data = parse_nunit_xml(self.native('<test-case name="a" result="Passed"/>'
                                          '<test-case name="b" result="Skipped"/>'
                                          '<test-case name="c" result="Failed"/>'))
        self.assertEqual((data["executed"], data["passed"], data["failed"], data["skipped"]), (2, 1, 1, 1))
        self.assertEqual(data["status"], "failed")

    def test_native_filtered_discovery_count_preserves_selected_totals(self):
        path = self.native('<test-suite testcasecount="18" total="2" passed="2" result="Passed">'
                           '<test-case name="a" result="Passed"/><test-case name="b" result="Passed"/>'
                           '</test-suite>', 'testcasecount="18" total="2" passed="2" result="Passed"')
        data = parse_nunit_xml(path)
        self.assertEqual((data['total'], data['executed'], data['passed']), (2, 2, 2))

    def test_native_discovery_cannot_hide_missing_selected_cases(self):
        for attributes in ('testcasecount="18" total="3"', 'testcasecount="1" total="2"', 'testcasecount="18"'):
            with self.subTest(attributes=attributes), self.assertRaises(ReportError):
                parse_nunit_xml(self.native('<test-suite '+attributes+'><test-case name="a" result="Passed"/>'
                                           '<test-case name="b" result="Passed"/></test-suite>'))

    def test_native_filtered_discovery_still_rejects_failed_leaves(self):
        data = parse_nunit_xml(self.native('<test-suite testcasecount="18" total="1" failed="1" result="Failed">'
                                          '<test-case name="a" result="Failed"/></test-suite>',
                                          'testcasecount="18" total="1" failed="1" result="Failed"'))
        self.assertEqual(data['status'], 'failed')
        self.assertEqual(data['failed'], 1)

    def test_native_malformed_zero_duplicate_and_counter_lies_rejected(self):
        invalid = ['<broken', '<test-run/>',
                   '<test-run total="2"><test-case name="a" result="Passed"/></test-run>',
                   '<test-run><test-case name="a" result="Passed" executed="False"/></test-run>',
                   '<test-run><test-case name="a" result="Passed"/><test-case name="a" result="Passed"/></test-run>',
                   '<test-run result="Passed"><test-case name="a" result="Failed"/></test-run>',
                   '<test-run result="Failed"><test-case name="a" result="Passed"/></test-run>',
                   '<test-run><test-suite passed="2"><test-case name="a" result="Passed"/></test-suite></test-run>',
                   '<test-run><test-case name="a" result="Unknown"/></test-run>']
        for source in invalid:
            with self.subTest(source=source), self.assertRaises(ReportError):
                parse_nunit_xml(self.write("native.xml", source))

    def test_native_nunit2_flags_and_explicit_case_property(self):
        source = ('<test-results total="1" errors="0" failures="0" not-run="0">'
                  '<test-suite result="Success" executed="True"><results><test-case name="fixture" result="Success" executed="True">'
                  '<properties><property name="case_id" value="required-case"/></properties>'
                  '</test-case></results></test-suite></test-results>')
        data = parse_nunit_xml(self.write("native.xml", source))
        self.assertEqual(data["cases"][0]["case_id"], "required-case")
        self.assertEqual(data["status"], "passed")

    def test_actual_build_bytes_are_rehashed(self):
        data = parse_build_report(self.build(), artifact_root=self.root)
        self.assertEqual(data["status"], "passed")
        self.assertEqual(data["apk_sha256"], sha256_file(self.root / "actual.apk"))

    def test_fake_build_failure_empty_missing_and_stale_hash_rejected(self):
        variants = [{"result": "Failed"}, {"total_errors": 1}, {"total_size": 0},
                    {"output_path": str(self.root / "missing.apk")}, {"apk_sha256": "0" * 64},
                    {"apk_byte_length": 0}, {"total_errors": False}]
        for fields in variants:
            with self.subTest(fields=fields), self.assertRaises(ReportError):
                parse_build_report(self.build(**fields))
        path = self.build()
        (self.root / "actual.apk").write_bytes(b"")
        with self.assertRaises(ReportError):
            parse_build_report(path)

    def test_build_cannot_escape_artifact_root(self):
        inside = self.root / "inside"
        inside.mkdir()
        with self.assertRaises(ReportError):
            parse_build_report(self.build(), artifact_root=inside)

    def test_status_crosscheck_rejects_false_wrapper_or_report_pass(self):
        process = {"exit_code": 0, "status": "passed"}
        self.assertEqual(crosscheck_status(process, {"status": "failed"}), "failed")
        with self.assertRaises(ReportError):
            crosscheck_status(process, {"status": "failed"}, reported_status="passed")
        with self.assertRaises(ReportError):
            crosscheck_status({"exit_code": 3, "status": "passed"}, {"status": "passed"})
        with self.assertRaises(ReportError):
            crosscheck_status({"exit_code": None, "status": "failed"}, {"status": "passed"})
        self.assertEqual(crosscheck_status(process, {"status": "passed"}, reported_status="passed"), "passed")

    def test_storage_exact_floor_and_growth_formulas(self):
        usage = type("Usage", (), {"free": 200 * GIB})()
        scenarios = [("unity", {"peak_bytes": 1}, 40), ("unity", {"peak_bytes": 45 * GIB}, 55),
                     ("art", {"new_output_bytes": 4 * GIB}, 22),
                     ("web", {"peak_bytes": 9 * GIB}, 14),
                     ("capture", {"raw_bytes": 7 * GIB, "export_bytes": 2 * GIB}, 30)]
        with mock.patch("runner_core.shutil.disk_usage", return_value=usage):
            for operation, estimates, required_gib in scenarios:
                data = check_storage(operation, [self.root], **estimates)
                self.assertEqual(data["required_bytes"], required_gib * GIB)
                self.assertEqual(data["status"], "passed")

    def test_storage_unknown_estimates_block_and_every_volume_checked(self):
        with self.assertRaises(StorageBlocked):
            check_storage("unity", [self.root])
        with self.assertRaises(StorageBlocked):
            check_storage("light", [self.root], peak_bytes=0)
        other = self.root / "other"
        other.mkdir()
        high = type("Usage", (), {"free": 100 * GIB})()
        low = type("Usage", (), {"free": 39 * GIB})()
        with mock.patch("runner_core.shutil.disk_usage", side_effect=[high, low]) as measurement:
            data = check_storage("unity", [self.root, other], peak_bytes=0)
        self.assertEqual(measurement.call_count, 2)
        self.assertEqual(data["status"], "blocked")

    def test_running_operation_stops_at_critical_storage_floor(self):
        low = type("Usage", (), {"free": 4 * GIB})()
        with mock.patch("runner_core.shutil.disk_usage", return_value=low):
            result = self.execute("import time; time.sleep(30)", monitored_volumes=[self.root])
        self.assertEqual(result["status"], "failed")
        self.assertIn("5 GiB", result["stop_reason"])
        self.assertNotEqual(result["exit_code"], 0)

    def test_operation_growth_guard_cancels_owned_process(self):
        result = self.execute("import time; time.sleep(30)",
                              stop_check=lambda: "owned diagnostic growth limit reached")
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["stop_reason"], "owned diagnostic growth limit reached")
        self.assertNotEqual(result["exit_code"], 0)
        self.assertFalse(result["timed_out"])

    def test_operation_growth_guard_exception_retains_terminal_outcome(self):
        def broken():
            raise ValueError("measurement failed")
        with self.assertRaisesRegex(ValueError, "measurement failed"):
            self.execute("import time; time.sleep(30)", stop_check=broken)
        outcome = json.loads((self.root / "outcome.json").read_text())
        self.assertEqual(outcome["status"], "failed")
        self.assertEqual(outcome["interrupted"], "ValueError")
        self.assertNotEqual(outcome["exit_code"], 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
