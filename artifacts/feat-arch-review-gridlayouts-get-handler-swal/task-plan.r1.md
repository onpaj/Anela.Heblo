Plan saved to `artifacts/feat-arch-review-gridlayouts-get-handler-swal/plan.r1.md`.

Self-review pass against the spec + arch review:

- **Spec coverage:** FR-1 → Task 3; FR-2 → Task 5 (with arch review's 500 amendment); FR-3 → Task 4 happy-path tests lock it down; FR-4 → Tasks 6+7 with arch review's corrected premise (FE must be modified, not just verified); FR-5 → logger line preserved verbatim in Task 3, re-asserted in Task 2's test rename.
- **Arch review amendments honored:** HTTP 500 (not 503), raw `StatusCode(500, response)` (not `HandleResponse<T>`), conditional FE catch branch using functional updater, existing `Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError` test renamed + reasserted instead of left to drift.
- **TDD discipline:** RED → GREEN → commit cycle per task; failing test committed before implementation in tasks 2/4/6.
- **Surgical scope:** toast and `HandleResponse<T>` migration explicitly deferred with rationale; no incidental edits.
- **Type consistency:** `GetGridLayoutResponse(ErrorCodes)` constructor introduced in Task 1 is referenced consistently by handler (Task 3) and controller test (Task 4).
- **Placeholders:** none — every code block is full, every command has expected output.