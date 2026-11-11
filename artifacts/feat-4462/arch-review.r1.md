# Architecture Review: Plaud CLI timeout callback must not crash the process

## Skip Design: true
Backend-only bug fix; no UI.

## Architectural Fit Assessment
Change is local to the Plaud adapter (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`). No module boundary, DTO or persistence impact. Verified in code: `cts.Token.Register(() => process.Kill(entireProcessTree: true))` is registered after `process.Start()` and never disposed; `CancelAfter` fires it on a timer thread. `ci-main-branch.yml` already sets `WEBSITES_CONTAINER_START_TIME_LIMIT=600`.

## Proposed Architecture

### Component Overview
`RunCliCoreAsync` -> `using var registration = cts.Token.Register(() => TryKillProcessTree(process))` -> `WaitForExitAsync(cts.Token)`. `TryKillProcessTree` is the single place where Kill is invoked and all expected exceptions are absorbed.

### Key Design Decisions

#### Decision 1: Guard helper with catch-all of expected exceptions
**Options considered:** (a) bare try/catch inline; (b) `HasExited` pre-check only; (c) helper with `HasExited` check plus catch.
**Chosen approach:** (c). `HasExited` itself can throw `InvalidOperationException` if no process is associated, so it must be inside the try. Catch `InvalidOperationException`, `Win32Exception`, `NotSupportedException`; log Debug when already exited, Warning otherwise. Do not catch `Exception` broadly except as a last-resort guard: because this runs on a timer thread, an escaping exception is fatal, so a final `catch (Exception)` with Warning log is justified here and documented in a comment.
**Rationale:** The failure mode is process death; defensive breadth is warranted.

#### Decision 2: Registration disposal order
`using var registration` is declared after `using var process` so it is disposed first (reverse order), guaranteeing the callback cannot touch a disposed `Process`. `Dispose` of a registration blocks until a running callback completes, which is safe because the callback is non-throwing and short.

#### Decision 3: Testability
Make helper `internal static bool TryKillProcessTree(Process process, ILogger logger)`; Plaud assembly already has `AssemblyAttributes.cs` (check for `InternalsVisibleTo` to the test project; add if missing). Integration-style test uses the existing shell-shim pattern in `PlaudCliClientRunTests` (`plaud.sh`) with `sleep` and `ProcessTimeoutSeconds = 1`.

## Implementation Guidance

### Directory / Module Structure
- Modify `PlaudCliClient.cs`.
- Modify/add tests in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` (or a new `PlaudCliClientTimeoutTests.cs`).
- Update `docs/processes/sync-plaud-recordings.md`.

### Interfaces and Contracts
No public contract changes. `ProcessTimeoutSeconds` is an int, so the timeout test needs 1 s minimum.

### Data Flow
Timeout -> timer fires callback -> `TryKillProcessTree` (safe) -> `WaitForExitAsync` throws OperationCanceledException -> mapped to `TimeoutException` -> job failure handled by the recurring-job framework.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Regression test flaky on CI (shell shim timing) | Low | 1 s timeout, `sleep 30` shim, generous assertion bounds |
| `Process.Kill` on already-reaped process races with `HasExited` | Medium | Catch inside helper; do not rely on pre-check |
| Other unguarded callbacks elsewhere in Plaud adapter | Low | Grep for `Register(`/`Timer` in the adapter during implementation |

## Specification Amendments
- `WEBSITES_CONTAINER_START_TIME_LIMIT=600` already exists; drop that item from scope (already reflected in spec).

## Prerequisites
None. Operational mitigation (disable `plaud-polling`) is manual and independent of this PR.
