Plan saved to `docs/superpowers/plans/2026-06-13-plaud-token-expiry-handling.md`.

**Summary of the plan:**

30 bite-sized TDD tasks delivering the spec end-to-end. The plan follows the arch review's component split: introduce `IPlaudTokenStore` (disk-first then KV writes with non-fatal KV failure) and `IPlaudTokenManager` (in-memory `PlaudTokens` cache + `SemaphoreSlim` single-flight + telemetry orchestration), then wire them into `PlaudCliClient` so it calls `EnsureFreshAsync` before each shell-out and `ForceRefreshAsync` + one retry on `AUTH_FAILED`. Each task is RED → GREEN → COMMIT, with exact file paths, complete code blocks, and verifier commands.

Key coverage points:
- **FR-2/FR-3** behaviors covered by 6 TDD scenarios in `PlaudTokenManagerTests` (happy path, near-expiry refresh, force-refresh success/failure, KV non-fatal, disk fatal, single-flight) plus 4 CLI-shim scenarios in `PlaudCliClientRefreshRetryTests`.
- **NFR-2** (no token leak) enforced by an explicit assertion test (Task 27) and per-process HMAC `tokenIdShort` derivation.
- **FR-1** runbook (Task 29) documents the rotation steps plus the alert configurations referenced in the arch review.
- `PlaudTokenRefreshJob` refactored to delegate to `IPlaudTokenStore` and `DefaultIsEnabled` flipped to `true` (Task 25), per arch review Decision 4.
- Existing `PlaudCliClient(logger, options)` ctor preserved so the existing CLI-shim tests don't break (Task 20).

Per pipeline instructions, skipping the execution handoff prompt — the plan file is the artifact.