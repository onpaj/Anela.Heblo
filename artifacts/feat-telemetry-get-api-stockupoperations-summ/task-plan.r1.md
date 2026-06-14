Plan saved to `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md`.

**Summary:**
- 12 tasks, organized by FR/NFR coverage and committable at task granularity.
- Default path is **R-A** (frontend gate via `{ enabled }` option on `useStockUpOperationsSummary`, callsite-level permission check) with inline R-B and R-C branches in Tasks 5 and 7.
- FR-4 backend test follows the reflection-based pattern of `DashboardControllerAuthorizationTests` / `GridLayoutsControllerAuthorizationTests` (per arch-review's specification amendment — `MockAuthenticationHandler` makes HTTP-level 403 testing impractical).
- FR-4 frontend tests spy on `stockUpOperations_GetSummary` and assert it is not invoked when permission is absent.
- FR-2 caller attribution (KQL) drives the path selection in Task 3; arch-review's fallback (default to R-A when attribution is impossible) is encoded in the decision matrix.
- FR-5 (single 500) is investigated in Task 10 with three explicit verdict paths.
- Task 12 runs the full validation gate (BE build/format/tests, FE build/lint/tests, generated client unchanged per NFR-4).