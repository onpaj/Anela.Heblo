# Specification: Plaud CLI timeout callback must not crash the process

## Summary
`PlaudCliClient.RunCliCoreAsync` registers a cancellation callback that calls `Process.Kill(entireProcessTree: true)` on a timer thread. When the CLI process has already exited, `Kill` throws `InvalidOperationException`, which is unhandled on a thread-pool timer thread and terminates the whole .NET host (production outage since 2026-10-09). This feature makes the callback non-throwing, scopes its lifetime to the method, and adds regression tests.

## Background
The `plaud-polling` recurring job runs every 5 minutes. When the Plaud CLI hangs and the 60 s `ProcessTimeoutSeconds` elapses, `CancelAfter` fires the registered callback. If the process is already gone, the exception escapes the timer callback, the container exits (code 139), and restarts (~3 min, near the 230 s startup probe limit) cause near-continuous 502/503. The registration is also never disposed, so it can fire after the method returns and the `Process` object is disposed.

## Functional Requirements

### FR-1: Kill callback never throws
The cancellation callback must swallow `InvalidOperationException`, `Win32Exception` and `NotSupportedException` from `Process.Kill`, and skip the kill when the process has already exited. Failures are logged at Warning/Debug level, not rethrown.
**Acceptance criteria:**
- Timeout/cancellation with an already-exited process does not throw out of the callback.
- Kill failure with a live process is logged and does not propagate.

### FR-2: Registration lifetime scoped to the call
The `CancellationTokenRegistration` is created with `using var` so it is disposed before the `Process` is disposed and cannot fire after `RunCliCoreAsync` returns. Registration happens immediately after `process.Start()` succeeds (before the stream reads are awaited).
**Acceptance criteria:**
- No callback invocation after method return.

### FR-3: Existing behaviour preserved
Timeout still surfaces as `TimeoutException("Plaud CLI process exceeded N seconds timeout")`; caller cancellation still surfaces as `OperationCanceledException`; exit-code handling (AUTH_FAILED, upstream 5xx) unchanged.
**Acceptance criteria:**
- All existing `PlaudCliClientRunTests` pass unchanged.

### FR-4: Regression tests
Add unit tests in `Anela.Heblo.Adapters.Plaud.Tests`: (a) a CLI shim that hangs beyond a short `ProcessTimeoutSeconds` results in `TimeoutException` and the test host survives; (b) the kill helper invoked on an already-exited process does not throw.
**Acceptance criteria:**
- Tests fail on the old code (unhandled exception or throw) and pass on the new code.

### FR-5: Process doc update
Update `docs/processes/sync-plaud-recordings.md` (Known quirks / Runtime facts) with the timeout/kill behaviour and bump `verified_at`; run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change; the helper is O(1).

### NFR-2: Security
No new inputs or secrets. Logging must not include CLI output beyond what is logged today.

### NFR-3: Reliability
No code path in the Plaud adapter may throw from a timer or thread-pool callback.

## Data Model
None.

## API / Interface Design
No public API change. A private static/instance helper `TryKillProcessTree(Process, ILogger)` (internal for testability) is added to `PlaudCliClient`.

## Dependencies
`System.Diagnostics.Process`, existing `ILogger`. Related: #3118 (previous auth-expiry incident).

## Out of Scope
- Root-causing why the Plaud CLI hung at ~07:25 UTC on 2026-10-09 (operational investigation; to be recorded as an issue comment, not code).
- Startup time / DB pool exhaustion investigation. `WEBSITES_CONTAINER_START_TIME_LIMIT=600` is already set in `.github/workflows/ci-main-branch.yml` (line 560), so no workflow change is needed.
- Disabling the `plaud-polling` job in the production DB (manual operational mitigation).

## Open Questions
None.

## Status: COMPLETE
