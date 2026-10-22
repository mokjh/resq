# Evidence harness amendment 1 — F02 implementation

Date: 16 October 2026, Cairo. Scope: evidence serialization and isolated validator fixtures. Product scope, 25 missions, rule algorithms, task dependencies and every passing criterion remain unchanged.

Implementation exposed two concrete issues in freeze 1.1.0:

1. The planning mutation suite copied live task state without its result artifacts. After real F01 acceptance, its isolated temporary repository incorrectly referenced missing live evidence. Its setup now resets **only the copied fixture state** to pending. Actual project state/history is never reset. All existing rejection tests remain.
2. The check-report schema required a native artifact and a passed case even for a pre-launch block, preventing truthful zero-case reporting. Nonpassing reports now allow an absent native artifact and zero cases; failed/skipped/not-run case statuses can be retained. A **passed** report still requires native execution, its nonempty artifact, positive execution count, all passed individual cases, every exact registered case/assertion, successful exit codes and zero required skips/failures. Four regression tests enforce both sides.

The original sealed manifest is preserved at `specification/archive/freeze_manifest-1.1.0-original.json`. The current manifest retains the same reviewed input set, records amendment revision1, the prior manifest digest and this amendment. Its changed hashes are limited to the schema generator, check-report schema and validator test fixture. New implementation code is not silently added to the specification seal.

This compatibility correction changes no game/rules or mission revision. Existing passed F01 evidence remains valid and immutable. Subsequent real task results use the corrected nonpassing format. Schema validity alone never makes a blocked/failed task pass.
