# Controlled amendment 9 — transport boarding history

Authority: the user's specification-freeze gap resolution and continued implementation. C04 review exposed an omitted persistent representation for the already-frozen rule that a group boards when every member has completed boarding at least once. A private set or the capped, hash-excluded event stream would make save continuation and rule hashes incomplete.

Section 03 now explicitly registers a compiled-milestone/group-bound internal causal-summary MetricState row. The existing exact wire fields carry sorted distinct boarding history and count; identity derives from canonical mission-version/group input. Initial occupants seed it, actual completed boarding adds to it, and unloading/backup never erases it. Initial true milestones sample O=S=0. The public objective metric union is unchanged; this is not a new authorable counter parameter. Duplicate producer ownership is rejected. C07 must validate and reconstruct these exact bound rows.

No new Snapshot field, private serial, weakened check, rule outcome change or migration is introduced. There is no accepted transport/save result to migrate. All 25 missions, 229 tasks, 231 checks, 942 assertions and 35,784 planned cases remain. Native C04 still requires all four real cases; future C07 must prove full save continuation.

Preserved prior inputs: evidence/runtime-transport-20261016/history-amendment-inputs. Prior seal: specification/archive/freeze_manifest-1.1.0-amendment-8.json. Only explicit reviewed changed/new inputs are resealed.
