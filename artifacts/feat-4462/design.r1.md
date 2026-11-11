# Design: Plaud CLI timeout callback must not crash the process

## Component Design
- `PlaudCliClient.RunCliCoreAsync`: after `process.Start()` and stream-read task creation, declare `using var killRegistration = cts.Token.Register(() => TryKillProcessTree(process, _logger));`. Remove the old unguarded `Register` call.
- `internal static void TryKillProcessTree(Process process, ILogger logger)`:
  1. `try { if (process.HasExited) return; process.Kill(entireProcessTree: true); }`
  2. `catch (InvalidOperationException)` -> Debug log "process already exited".
  3. `catch (Win32Exception ex)` / `catch (NotSupportedException ex)` -> Warning log.
  4. final `catch (Exception ex)` -> Warning log with comment that this runs on a timer thread where any escape is fatal.
- Tests:
  - Timeout test: shim script `sleep 30`, `ProcessTimeoutSeconds = 1`; assert `TimeoutException` thrown by `ListRecentAsync` and elapsed < 10 s.
  - Helper test: start `true`/short process, wait for exit, dispose-safe call `TryKillProcessTree` and assert no exception; also call with an unstarted `Process` (HasExited throws InvalidOperationException) and assert no exception.
- Docs: add a Known quirk entry to `docs/processes/sync-plaud-recordings.md` and bump `verified_at`.

## Data Schemas
None.
