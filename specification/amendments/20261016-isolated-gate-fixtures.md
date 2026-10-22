# Evidence harness amendment 2 — isolate copied gate heads

The F02 continuation recheck retained an actual failed report: test_gate_not_passed inherited the now-passed host_license head from the live checkout. Its synthetic nonpassing-gate input was rejected for a head mismatch, so it never reached the rejection branch the test intended to exercise.

Only the isolated temporary fixture's gate execution fields now reset to not_verified/null, alongside its existing copied task-state reset. Tests author their own synthetic passed gate evidence when required. The actual project gates, task state and accepted observations are untouched. The original mutation and expected rejection remain; an additional regression verifies copied head isolation and byte-for-byte preservation of the live gate registry.

No validator behavior, passing criterion, task dependency, resource floor, product/rules revision or 25-mission scope changes. This amendment corrects fixture independence as implementation gates become real. The native failed attempt is preserved under evidence/runs/F02/continuation-20261016/attempt-01.

The prior amendment1 seal is preserved at specification/archive/freeze_manifest-1.1.0-amendment-1.json. Controlled seal revision2 updates only tools/test_spec_freeze.py and adds this amendment plus that archive. No implementation sources are silently added to the seal. Source-snapshot byte binding and recursive Unity inputs are ordinary unsealed implementation changes with their own current native regressions.
