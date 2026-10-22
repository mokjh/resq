"""Prepared source-candidate tests. No real processes or signals are used.
Run only under coordinator ownership, e.g. python3 -B <this file>.
These are not native Android, resource-budget, or task-acceptance evidence.
"""
import ast
import errno
import importlib.util
import io
import json
from pathlib import Path
import signal
import tempfile
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
CANDIDATES = HERE
spec = importlib.util.spec_from_file_location('review_runner_core', CANDIDATES / 'runner_core.py')
core = importlib.util.module_from_spec(spec)
spec.loader.exec_module(core)

# Load only the pure wrapper: importing the bounded runner also loads real
# resource profiles, which is outside this isolated failure-path test.
tree = ast.parse((CANDIDATES / 'run_unity_bounded.py').read_text())
function = next(node for node in tree.body
                if isinstance(node, ast.FunctionDef) and node.name == '_with_final_measurements')
namespace = {'write_json_exclusive': core.write_json_exclusive, 'utc_now': core.utc_now,
             'StorageBlocked': core.StorageBlocked}
exec(compile(ast.Module(body=[function], type_ignores=[]), 'candidate-finalizer', 'exec'), namespace)
with_measurements = namespace['_with_final_measurements']


class FakeProcess:
    pid = 424242
    def __init__(self, returncode=0):
        self.returncode = returncode
        self.stdout, self.stderr = io.StringIO(''), io.StringIO('')
    def poll(self):
        return self.returncode
    def wait(self, timeout=None):
        if self.returncode is None:
            raise core.subprocess.TimeoutExpired('fake-owned', timeout)
        return self.returncode


class CleanupCandidateTests(unittest.TestCase):
    def invoke(self, directory, process, cleanup, stop_check=None):
        argv = ['never-executed-owned-test']
        with mock.patch.object(core.subprocess, 'Popen', return_value=process), \
             mock.patch.object(core, '_stop_owned_group', side_effect=cleanup):
            result = core.run_process(argv, cwd=directory,
                stdout_path=directory/'stdout.txt', stderr_path=directory/'stderr.txt',
                allowed_commands=[argv], timeout_seconds=1, owner='source-test',
                process_record_path=directory/'execution.json', outcome_path=directory/'outcome.json',
                stop_check=stop_check)
        self.assertEqual(result, json.loads((directory/'outcome.json').read_text()))
        self.assertIsNotNone(result['ended_utc'])
        return result

    def test_eperm_retains_failed_outcome_even_with_zero_leader_exit(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = self.invoke(Path(temporary), FakeProcess(),
                PermissionError(errno.EPERM, 'simulated group denial'))
        self.assertEqual('failed', result['status'])
        self.assertEqual(0, result['exit_code'])
        self.assertIs(result['process_group_terminal'], False)
        self.assertEqual(errno.EPERM, result['cleanup_errors'][0]['errno'])
        self.assertIsNone(result['launch_error'])

    def test_resource_reason_and_unknown_exit_survive_cleanup_denial(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = self.invoke(Path(temporary), FakeProcess(None),
                PermissionError(errno.EPERM, 'simulated group denial'),
                lambda: 'sampled positive growth budget reached')
        self.assertEqual('sampled positive growth budget reached', result['stop_reason'])
        self.assertIsNone(result['exit_code'])
        self.assertEqual('failed', result['status'])
        self.assertIs(result['process_group_terminal'], False)

    def test_group_unverified_cannot_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = self.invoke(Path(temporary), FakeProcess(), lambda process: False)
        self.assertEqual('failed', result['status'])
        self.assertIs(result['process_group_terminal'], False)

    def test_confirmed_terminal_normal_exit_can_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = self.invoke(Path(temporary), FakeProcess(), lambda process: True)
        self.assertEqual('passed', result['status'])
        self.assertIs(result['process_group_terminal'], True)
        self.assertEqual([], result['cleanup_errors'])

    def test_monitor_oserror_is_not_mislabeled_as_launch_error(self):
        def monitor():
            raise PermissionError(errno.EPERM, 'simulated monitor read denial')
        with tempfile.TemporaryDirectory() as temporary:
            result = self.invoke(Path(temporary), FakeProcess(None), lambda process: True, monitor)
        self.assertIsNone(result['launch_error'])
        self.assertIn('PermissionError', result['execution_error'])
        self.assertEqual('failed', result['status'])

    def test_kill_success_requires_absence_probe(self):
        with mock.patch.object(core.os, 'killpg', side_effect=[None, ProcessLookupError()]) as send:
            self.assertIs(core._stop_owned_group(FakeProcess(), grace_seconds=0), True)
        self.assertEqual([mock.call(424242, signal.SIGKILL), mock.call(424242, 0)], send.call_args_list)

    def test_present_group_is_not_reported_terminal(self):
        with mock.patch.object(core.os, 'killpg', return_value=None):
            self.assertIs(core._stop_owned_group(FakeProcess(), grace_seconds=0), False)

    def test_eperm_probe_is_not_absence(self):
        with mock.patch.object(core.os, 'killpg', side_effect=[None, PermissionError(errno.EPERM, 'denied')]):
            with self.assertRaises(PermissionError):
                core._stop_owned_group(FakeProcess(), grace_seconds=0)

    def test_measurements_survive_primary_and_final_observation_errors(self):
        def launch():
            raise ValueError('primary execution failure')
        def final_guard():
            raise OSError('final observation unavailable')
        samples = {'peak_positive_growth_bytes': 123, 'lowest_free_bytes': 456}
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            with self.assertRaisesRegex(ValueError, 'primary execution failure'):
                with_measurements(launch, destination, samples, final_guard)
            receipt = json.loads((destination/'measurements.json').read_text())
        self.assertEqual(123, receipt['peak_positive_growth_bytes'])
        self.assertEqual(456, receipt['lowest_free_bytes'])
        self.assertIn('OSError', receipt['final_observation_error'])
        self.assertIs(receipt['task_accepted'], False)
        self.assertIs(receipt['full_peak_known'], False)

    def test_final_observation_error_fails_successful_execution(self):
        def final_guard():
            raise OSError('final observation unavailable')
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            with self.assertRaisesRegex(RuntimeError, 'Final resource observation failed'):
                with_measurements(lambda: {'status': 'passed'}, destination, {}, final_guard)
            self.assertTrue((destination/'measurements.json').exists())

    def test_final_resource_limit_cannot_leave_successful_execution_qualified(self):
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            with self.assertRaisesRegex(core.StorageBlocked, 'Final resource guard failed: growth budget reached'):
                with_measurements(lambda: {'status': 'passed'}, destination,
                                  {'peak_positive_growth_bytes': 123}, lambda: 'growth budget reached')
            receipt = json.loads((destination/'measurements.json').read_text())
            self.assertEqual('growth budget reached', receipt['final_guard_reason'])
            self.assertEqual(123, receipt['peak_positive_growth_bytes'])

    def test_existing_resource_stop_reaches_adapter_without_losing_its_classification(self):
        outcome = {'status': 'failed', 'exit_code': -15, 'process_group_terminal': True,
                   'stop_reason': 'runtime-forecast volume below2GiB hard reserve'}
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            actual = with_measurements(lambda: outcome, destination,
                {'lowest_free_bytes': 2078433280}, lambda: outcome['stop_reason'])
            receipt = json.loads((destination/'measurements.json').read_text())
        self.assertIs(actual, outcome)
        self.assertEqual('failed', actual['status'])
        self.assertEqual(-15, actual['exit_code'])
        self.assertEqual(outcome['stop_reason'], receipt['final_guard_reason'])
        self.assertEqual(2078433280, receipt['lowest_free_bytes'])


if __name__ == '__main__':
    unittest.main()
