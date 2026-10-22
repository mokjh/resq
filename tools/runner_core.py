"""Fail-closed, stdlib execution primitives. No task acceptance is inferred here.

Native reports are observations, not proof of authentic device/human participation.
The caller owns registry admission, identities, case catalogue and evidence sealing.
"""
from __future__ import annotations

import contextlib
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import signal
import subprocess
import threading
import time
import uuid
import xml.etree.ElementTree as ET

GIB = 1024 ** 3


class RunnerError(RuntimeError):
    pass


class LockBusy(RunnerError):
    pass


class ReportError(RunnerError):
    pass


class StorageBlocked(RunnerError):
    pass


def utc_now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json_exclusive(path, payload):
    """Durably finalize on the same volume, without replacing existing evidence."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.parent / ("." + path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2, sort_keys=True, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)  # Atomic, unlike exists()+replace().
        directory = os.open(path.parent, os.O_RDONLY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        temporary.unlink(missing_ok=True)


class ResourceLock:
    """Exclusive ownership record; foreign/stale locks always require review.

    Locks are never reclaimed by guessing whether a PID still exists. Own release
    verifies inode and random token, protecting against a replaced ownership file.
    """

    def __init__(self, lock_dir, resource, owner):
        if not isinstance(resource, str) or not resource or not owner:
            raise ValueError("resource and owner must be nonempty")
        self.resource = resource
        self.owner = str(owner)
        self.path = Path(lock_dir) / (hashlib.sha256(resource.encode()).hexdigest() + ".lock.json")
        self.token = uuid.uuid4().hex
        self.identity = None
        self.pid = os.getpid()

    def acquire(self):
        if self.identity is not None:
            raise LockBusy("lock instance is already acquired")
        self.path.parent.mkdir(parents=True, exist_ok=True)
        record = {"resource": self.resource, "owner": self.owner, "pid": self.pid,
                  "parent_pid": os.getppid(), "cwd": str(Path.cwd().resolve()),
                  "created_utc": utc_now(), "token": self.token}
        try:
            descriptor = os.open(self.path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        except FileExistsError as error:
            raise LockBusy("existing lock requires owner review: " + str(self.path)) from error
        try:
            info = os.fstat(descriptor)
            self.identity = (info.st_dev, info.st_ino)
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                descriptor = None
                json.dump(record, stream, sort_keys=True)
                stream.flush()
                os.fsync(stream.fileno())
        except BaseException:
            if descriptor is not None:
                os.close(descriptor)
            self.release()
            raise
        return self

    def release(self):
        if self.identity is None:
            return
        if os.getpid() != self.pid:
            raise LockBusy("a forked process cannot release its parent's lock")
        try:
            info = self.path.lstat()
            if (info.st_dev, info.st_ino) != self.identity or self.path.is_symlink():
                raise LockBusy("ownership file was replaced; preserved: " + str(self.path))
            record = json.loads(self.path.read_text(encoding="utf-8"))
            if record.get("token") != self.token or record.get("pid") != self.pid:
                raise LockBusy("ownership token changed; preserved: " + str(self.path))
            self.path.unlink()
            self.identity = None
        except FileNotFoundError:
            self.identity = None
        except (ValueError, OSError) as error:
            raise LockBusy("cannot verify own lock; preserved: " + str(self.path)) from error

    def __enter__(self):
        return self.acquire()

    def __exit__(self, exc_type, exc_value, traceback):
        self.release()


@contextlib.contextmanager
def resource_locks(lock_dir, resources, owner):
    with contextlib.ExitStack() as stack:
        locks = [stack.enter_context(ResourceLock(lock_dir, item, owner))
                 for item in sorted(set(resources))]
        yield locks


def _stop_owned_group(process, grace_seconds=2.0):
    # start_new_session=True makes this PID the owned process-group leader. Never
    # signal a group discovered by name, port, parent search or a foreign PID.
    if process.poll() is None:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            process.wait(timeout=grace_seconds)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=grace_seconds)
    # A group child can outlive a leader that exited on TERM. Clean that owned
    # group as well; a fresh process session cannot share this live group ID.
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        return True
    # Sending KILL is not proof that every member has exited. Probe only the
    # exact owned group, with a bounded wait. EPERM remains an error, not absence.
    deadline = time.monotonic() + grace_seconds
    while True:
        try:
            os.killpg(process.pid, 0)
        except ProcessLookupError:
            return True
        if time.monotonic() >= deadline:
            return False
        time.sleep(min(0.05, max(0.0, deadline - time.monotonic())))


def run_process(argv, *, cwd, stdout_path, stderr_path, allowed_commands,
                timeout_seconds, owner, process_record_path=None, outcome_path=None,
                redactor=None, monitored_volumes=(), stop_check=None):
    """Execute an exact caller-allowlisted argv and retain actual process facts.

    Logs are created exclusively before launch. Optional redactor receives each
    decoded output line before storage; callers must redact any secrets in argv.
    Timeouts and interrupts terminate only this newly created process group.
    """
    if (not isinstance(argv, (list, tuple)) or not argv or
            any(not isinstance(arg, str) or "\0" in arg for arg in argv)):
        raise ValueError("argv must be a nonempty string array")
    if tuple(argv) not in {tuple(command) for command in allowed_commands}:
        raise RunnerError("command not in caller's exact allowlist")
    if (isinstance(timeout_seconds, bool) or not isinstance(timeout_seconds, (int, float))
            or not math.isfinite(timeout_seconds) or timeout_seconds <= 0):
        raise ValueError("a finite positive timeout is required")
    if not owner:
        raise ValueError("process owner required")
    if os.name != "posix":
        raise RunnerError("owned process-group cancellation requires POSIX")
    cwd = Path(cwd).resolve(strict=True)
    if not cwd.is_dir():
        raise ValueError("cwd must be a directory")
    stdout_path, stderr_path = Path(stdout_path), Path(stderr_path)
    if stdout_path.resolve() == stderr_path.resolve():
        raise ValueError("stdout and stderr require different destinations")
    for path in (stdout_path, stderr_path, process_record_path, outcome_path):
        if path is not None and Path(path).exists():
            raise FileExistsError(str(path))
    for path in (stdout_path, stderr_path):
        path.parent.mkdir(parents=True, exist_ok=True)
    outcome = {"argv": list(argv), "cwd": str(cwd), "owner": str(owner),
               "started_utc": utc_now(), "ended_utc": None, "pid": None,
               "parent_pid": os.getpid(), "pgid": None, "exit_code": None,
               "timed_out": False, "status": "failed", "launch_error": None,
               "stop_reason": None, "execution_error": None,
               "cleanup_attempted": False, "cleanup_errors": [],
               "process_group_terminal": None}
    process = None
    readers = []
    reader_errors = []
    interrupted = None
    start = time.monotonic()

    def cleanup_owned_group():
        nonlocal interrupted
        if outcome["cleanup_attempted"]:
            return
        outcome["cleanup_attempted"] = True
        try:
            outcome["process_group_terminal"] = _stop_owned_group(process)
        except BaseException as error:
            # Preserve the observed failure and finish durable receipts. No
            # exception, leader exit code, or KILL success proves group absence.
            outcome["process_group_terminal"] = False
            outcome["cleanup_errors"].append({
                "type": type(error).__name__, "errno": getattr(error, "errno", None),
                "message": str(error), "pgid": process.pid,
                "observed_utc": utc_now()})
            if not isinstance(error, Exception) and interrupted is None:
                interrupted = error

    with stdout_path.open("x", encoding="utf-8") as stdout_log, stderr_path.open("x", encoding="utf-8") as stderr_log:
        def copy_output(pipe, stream):
            try:
                for line in iter(pipe.readline, ""):
                    stream.write(redactor(line) if redactor else line)
                    stream.flush()
            except BaseException as error:
                reader_errors.append(type(error).__name__ + ": " + str(error))
            finally:
                pipe.close()

        try:
            process = subprocess.Popen(list(argv), cwd=cwd, stdin=subprocess.DEVNULL,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       text=True, encoding="utf-8", errors="replace",
                                       shell=False, start_new_session=True)
            outcome.update(pid=process.pid, pgid=process.pid)
            if process_record_path:
                write_json_exclusive(process_record_path, dict(outcome, purpose="registered command execution"))
            for pipe, stream in ((process.stdout, stdout_log), (process.stderr, stderr_log)):
                thread = threading.Thread(target=copy_output, args=(pipe, stream), daemon=True)
                thread.start()
                readers.append(thread)
            deadline = start + timeout_seconds
            while process.poll() is None:
                if any(shutil.disk_usage(volume).free < 5 * GIB for volume in monitored_volumes):
                    outcome["stop_reason"] = "output volume below 5 GiB reserve"
                    cleanup_owned_group()
                    break
                if stop_check is not None:
                    reason = stop_check()
                    if reason:
                        if not isinstance(reason, str):
                            raise ValueError("stop_check must return a reason string or None")
                        outcome["stop_reason"] = reason
                        cleanup_owned_group()
                        break
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    outcome["timed_out"] = True
                    outcome["stop_reason"] = "timeout"
                    cleanup_owned_group()
                    break
                try:
                    process.wait(timeout=min(remaining, 0.2))
                except subprocess.TimeoutExpired:
                    pass
        except OSError as error:
            key = "launch_error" if process is None else "execution_error"
            outcome[key] = type(error).__name__ + ": " + str(error)
        except BaseException as error:
            interrupted = error
            outcome["interrupted"] = type(error).__name__
        finally:
            if process is not None:
                # Descendants must not remain after this operation. This also
                # closes inherited pipes if the leader exits before its child.
                cleanup_owned_group()
                outcome["exit_code"] = process.poll()
                for thread in readers:
                    thread.join(timeout=5)
                if any(thread.is_alive() for thread in readers):
                    reader_errors.append("owned output reader did not terminate")
                if not readers:
                    process.stdout.close()
                    process.stderr.close()
            stdout_log.flush()
            stderr_log.flush()
            os.fsync(stdout_log.fileno())
            os.fsync(stderr_log.fileno())
    outcome.update(ended_utc=utc_now(), duration_seconds=time.monotonic() - start,
                   output_errors=reader_errors)
    outcome["stdout"] = {"path": str(stdout_path.resolve()), "sha256": sha256_file(stdout_path),
                         "byte_length": stdout_path.stat().st_size}
    outcome["stderr"] = {"path": str(stderr_path.resolve()), "sha256": sha256_file(stderr_path),
                         "byte_length": stderr_path.stat().st_size}
    if (outcome["exit_code"] == 0 and not outcome["timed_out"] and not outcome["stop_reason"] and not interrupted
            and not outcome["launch_error"] and not outcome["execution_error"]
            and outcome["process_group_terminal"] is True
            and not outcome["cleanup_errors"] and not reader_errors):
        outcome["status"] = "passed"
    if outcome_path:
        write_json_exclusive(outcome_path, outcome)
    if interrupted:
        raise interrupted
    return outcome


def _integer(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ReportError(name + " must be a nonnegative integer")
    return value


def parse_nunit_xml(path):
    """Parse native NUnit 2/3 leaves and check every declared suite counter."""
    try:
        source = Path(path).read_bytes()
        if b"<!DOCTYPE" in source.upper() or b"<!ENTITY" in source.upper():
            raise ReportError("DTD/entity declarations are not native result evidence")
        root = ET.fromstring(source)
    except (OSError, ET.ParseError) as error:
        raise ReportError("missing or malformed native NUnit XML") from error
    if root.tag not in ("test-run", "test-results"):
        raise ReportError("unsupported native test root")
    cases = []
    seen = set()
    results = {"Passed": "passed", "Success": "passed", "Failed": "failed",
               "Failure": "failed", "Error": "failed", "Cancelled": "failed",
               "Skipped": "skipped", "Ignored": "skipped", "Inconclusive": "skipped",
               "NotRunnable": "skipped", "Invalid": "failed"}
    native_by_element = {}
    for element in root.iter("test-case"):
        raw = element.get("result")
        if raw not in results:
            raise ReportError("unknown native test result: " + str(raw))
        executed_attribute = element.get("executed")
        if executed_attribute is not None and executed_attribute.lower() not in ("true", "false"):
            raise ReportError("invalid native executed flag")
        status = results[raw]
        executed = (executed_attribute.lower() == "true" if executed_attribute is not None
                    else raw not in ("Skipped", "Ignored", "NotRunnable"))
        if status == "passed" and not executed:
            raise ReportError("unexecuted native case claims success")
        case_id = element.get("fullname") or element.get("name")
        properties = [node.get("value") for node in element.findall("./properties/property")
                      if node.get("name") == "case_id"]
        if len(properties) > 1:
            raise ReportError("duplicate native case_id property")
        if properties:
            case_id = properties[0]
        if not case_id or case_id in seen:
            raise ReportError("missing or duplicate native case identity")
        seen.add(case_id)
        record = {"case_id": case_id, "status": status, "executed": executed,
                  "native_result": raw, "native_name": element.get("fullname") or element.get("name")}
        cases.append(record)
        native_by_element[element] = record
    if not cases:
        raise ReportError("zero native test cases")

    def totals(rows):
        return {"total": len(rows), "executed": sum(row["executed"] for row in rows),
                "passed": sum(row["status"] == "passed" for row in rows),
                "failed": sum(row["status"] == "failed" for row in rows),
                "skipped": sum(row["status"] == "skipped" for row in rows)}

    for element in root.iter():
        if element.tag not in ("test-run", "test-results", "test-suite"):
            continue
        rows = [native_by_element[leaf] for leaf in element.iter("test-case")]
        counts = totals(rows)
        counters = {"total": counts["total"], "testcasecount": counts["total"],
                    "passed": counts["passed"], "failed": counts["failed"],
                    "skipped": counts["skipped"], "executed": counts["executed"]}
        if element.get("executed", "").lower() in ("true", "false"):
            if (element.get("executed").lower() == "true") != (counts["executed"] > 0):
                raise ReportError("native suite executed flag contradicts leaves")
            del counters["executed"]
        # NUnit 2 splits failures/errors and skipped/ignored/inconclusive.
        if root.tag == "test-results":
            counters.update(errors=sum(row["native_result"] == "Error" for row in rows),
                            failures=sum(row["native_result"] == "Failure" for row in rows),
                            ignored=sum(row["native_result"] == "Ignored" for row in rows),
                            inconclusive=sum(row["native_result"] == "Inconclusive" for row in rows),
                            invalid=sum(row["native_result"] in ("NotRunnable", "Invalid") for row in rows))
            counters["skipped"] = sum(row["native_result"] == "Skipped" for row in rows)
            counters["not-run"] = counts["total"] - counts["executed"]
        else:
            counters["inconclusive"] = sum(row["native_result"] == "Inconclusive" for row in rows)
            # NUnit 3 keeps inconclusive separate from skipped in its counters.
            counters["skipped"] -= counters["inconclusive"]
        for key, expected in counters.items():
            if key not in element.attrib:
                continue
            try:
                actual = int(element.get(key))
            except ValueError as error:
                raise ReportError("invalid native counter " + key) from error
            # NUnit3's testcasecount is discovery scope; total and the outcome
            # counters describe selected leaves. Unity retains unselected suite
            # discovery counts in filtered runs (e.g.18discovered,4selected).
            # Require an explicit exact total before admitting a larger count.
            discovery_count = root.tag == "test-run" and key == "testcasecount" and "total" in element.attrib
            if actual < 0 or (actual < expected if discovery_count else actual != expected):
                raise ReportError("native counter contradiction: " + key)
        if element.get("result") in ("Passed", "Success") and any(row["status"] != "passed" for row in rows):
            raise ReportError("native suite success contradicts leaves")
        if element.get("result") not in (None, "Passed", "Success") and rows and all(row["status"] == "passed" for row in rows):
            raise ReportError("native suite failure contradicts passing leaves")
    counts = totals(cases)
    return dict(counts, status="passed" if counts["passed"] == counts["total"] else "failed",
                cases=cases, path=str(Path(path).resolve()), sha256=sha256_file(path))


def parse_build_report(path, *, artifact_root=None):
    """Validate actual BuildReport summary and independently rehash its APK."""
    try:
        report = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ReportError("missing or malformed native BuildReport JSON") from error
    if not isinstance(report, dict):
        raise ReportError("BuildReport must be an object")
    errors = _integer(report.get("total_errors"), "total_errors")
    size = _integer(report.get("total_size"), "total_size")
    if report.get("result") != "Succeeded" or errors != 0 or size <= 0:
        raise ReportError("native BuildReport did not succeed with positive output")
    output = report.get("output_path")
    if not isinstance(output, str) or not output:
        raise ReportError("missing native APK output path")
    apk = Path(output)
    if not apk.is_absolute():
        apk = Path(path).resolve().parent / apk
    try:
        apk = apk.resolve(strict=True)
        if artifact_root is not None:
            apk.relative_to(Path(artifact_root).resolve(strict=True))
    except (OSError, ValueError) as error:
        raise ReportError("APK missing or outside permitted output root") from error
    if not apk.is_file() or apk.suffix.lower() != ".apk" or apk.stat().st_size <= 0:
        raise ReportError("actual positive-size APK required")
    digest = sha256_file(apk)
    if report.get("apk_sha256") != digest:
        raise ReportError("native APK hash differs from actual bytes")
    if _integer(report.get("apk_byte_length"), "apk_byte_length") != apk.stat().st_size:
        raise ReportError("native APK byte length differs from actual bytes")
    return dict(report, status="passed", output_path=str(apk), apk_sha256=digest,
                path=str(Path(path).resolve()), sha256=sha256_file(path))


def crosscheck_status(process, native, *, reported_status=None):
    exit_code = process.get("exit_code")
    if isinstance(exit_code, bool) or not isinstance(exit_code, int):
        raise ReportError("wrapper has no actual integer process exit")
    actual = (exit_code == 0 and not process.get("timed_out", False)
              and not process.get("launch_error") and not process.get("output_errors")
              and not process.get("interrupted") and not process.get("stop_reason"))
    if process.get("status") != ("passed" if actual else "failed"):
        raise ReportError("wrapper status contradicts process outcome")
    if native.get("status") not in ("passed", "failed"):
        raise ReportError("missing native execution status")
    status = "passed" if actual and native["status"] == "passed" else "failed"
    if reported_status is not None and reported_status != status:
        raise ReportError("reported acceptance status contradicts process/native results")
    return status


def check_storage(operation, volumes, *, peak_bytes=None, new_output_bytes=None,
                  raw_bytes=None, export_bytes=None):
    """Fresh per-output/temp-volume measurements for heavy operations only."""
    def estimate(value, label):
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise StorageBlocked("unknown or invalid " + label)
        return value
    if operation == "unity":
        required = max(40 * GIB, estimate(peak_bytes, "peak_bytes") + 10 * GIB)
    elif operation == "art":
        required = max(15 * GIB, 3 * estimate(new_output_bytes, "new_output_bytes") + 10 * GIB)
    elif operation == "web":
        required = max(10 * GIB, estimate(peak_bytes, "peak_bytes") + 5 * GIB)
    elif operation == "capture":
        required = max(20 * GIB, 2 * estimate(raw_bytes, "raw_bytes")
                       + 3 * estimate(export_bytes, "export_bytes") + 10 * GIB)
    else:
        raise StorageBlocked("unknown heavy operation: " + str(operation))
    if not volumes:
        raise StorageBlocked("all output and temporary volumes must be declared")
    observations = []
    for volume in volumes:
        path = Path(volume).resolve(strict=True)
        usage = shutil.disk_usage(path)
        observations.append({"path": str(path), "free_bytes": usage.free,
                             "required_bytes": required, "passed": usage.free >= required,
                             "observed_utc": utc_now()})
    return {"operation": operation, "required_bytes": required, "volumes": observations,
            "status": "passed" if all(row["passed"] for row in observations) else "blocked"}
