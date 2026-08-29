# Specification: Fix silent swallow of "no runner registered" error in RunDqtHandler

## Summary
`RunDqtHandler.Handle` starts DQT (Data Quality Test) runs by persisting a `DqtRun` entity and then kicking off the actual work in a fire-and-forget `Task.Run`. If no `IDqtJobRunner` is registered for the requested `DqtTestType`, an `InvalidOperationException` is thrown *inside* the discarded task before `RunAsync` (which has its own try/catch) is ever reached, so the exception is silently swallowed. The `DqtRun` is left permanently in `Running` state with no error recorded and nothing logged. This change makes that failure mode impossible by validating that a runner exists for the requested test type before the run entity is persisted, rejecting the request synchronously with the existing `DqtUnsupportedTestType` error code.

## Background
`POST /api/data-quality/runs` (`RunDqtHandler`) is the entry point for starting a DQT run for a given `DqtTestType`. Two runners currently implement `IDqtJobRunner`: `DriftDqtJobRunner` (delegates to registered `IDriftDqtComparer`s) and `InvoiceDqtJobRunner`. Each `IDqtJobRunner.RunAsync` already wraps its work in try/catch and calls `run.Fail(...)` + `SaveChangesAsync` on error — that part of the design is sound.

The gap is the runner *lookup* itself:

```csharp
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    var runner = scope.ServiceProvider
        .GetServices<IDqtJobRunner>()
        .SingleOrDefault(r => r.CanHandle(request.TestType))
        ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
    await runner.RunAsync(run.Id);
}, CancellationToken.None);
```

The `?? throw` happens before `RunAsync` is entered, inside the fire-and-forget lambda, with the resulting `Task` discarded (`_ = Task.Run(...)`). A .NET unobserved-task exception has no handler here, so it is lost: no log entry, no `run.Fail()` call, no `SaveChangesAsync`. The `DqtRun` row is stuck in `Running` forever with no diagnostic trail. This can be triggered today by a corrupted/misconfigured runner registration, and will be triggered going forward whenever a new `DqtTestType` value is added before its `IDqtJobRunner` implementation is wired into DI.

The codebase already has a purpose-built error code for this exact case, `ErrorCodes.DqtUnsupportedTestType = 2204`, currently used by `GetDqtRunDetailHandler`. This confirms "unsupported test type" is treated elsewhere as a normal, synchronously-reportable validation failure — `RunDqtHandler` should follow the same pattern rather than deferring the check into the background task.

## Functional Requirements

### FR-1: Validate runner existence before persisting the run
`RunDqtHandler.Handle` must resolve the set of registered `IDqtJobRunner` instances and confirm at least one `CanHandle(request.TestType)` **before** calling `DqtRun.Start(...)`, `_repository.AddAsync(...)`, and `_repository.SaveChangesAsync(...)`.

**Acceptance criteria:**
- When no registered `IDqtJobRunner` can handle `request.TestType`, `Handle` returns `RunDqtResponse { Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType }` and does **not** call `_repository.AddAsync` or `_repository.SaveChangesAsync`.
- No `DqtRun` entity is persisted for a rejected request (no orphaned `Running` row is ever created for an unsupported test type).
- When exactly one registered `IDqtJobRunner` can handle `request.TestType`, existing behavior (persist run, fire-and-forget `RunAsync`, return `Success = true` with the new `DqtRunId`) is unchanged.
- The lookup used for validation and the lookup used inside the fire-and-forget task must agree (same `CanHandle` predicate, same runner set) so a positive validation cannot be followed by a lookup miss inside the task.

### FR-2: Defense-in-depth inside the fire-and-forget task
Even with FR-1 in place, the runner lookup inside the `Task.Run` body must not be able to silently swallow an exception if it is ever reached with no matching runner (e.g. a future refactor reintroduces a race, or DI registration changes between validation and task execution in a scaled-out/hot-reload scenario). Wrap the full body of the `Task.Run` lambda — including the runner-lookup line — in a `try/catch` that mirrors the pattern already used in `DriftDqtJobRunner.RunAsync`.

**Acceptance criteria:**
- Any exception thrown anywhere inside the `Task.Run` lambda (including the `?? throw new InvalidOperationException(...)` for a missing runner) is caught.
- On catch, the handler loads the persisted `run` (already in scope — no re-fetch needed since it's the same in-memory instance created earlier in `Handle`) and calls `run.Fail(ex.Message, _timeProvider.GetUtcNow().DateTime)`, then persists via `_repository.SaveChangesAsync(CancellationToken.None)` inside the new scope (using that scope's own resolved `IDqtRunRepository`, not the outer `_repository`, since the outer one's `DbContext` may belong to a disposed HTTP-request scope by the time the task runs).
- The exception is also logged (`_logger.LogError`) with the run id and test type, consistent with the logging pattern in `DriftDqtJobRunner`/`InvoiceDqtJobRunner`.
- This path is a safety net, not the primary defense — FR-1 is expected to prevent it from ever firing in normal operation.

### FR-3: No behavior change for the happy path and existing error path
The existing `DateFrom > DateTo` validation (`ErrorCodes.DqtInvalidDateRange`) and the general `catch (Exception ex)` around persistence (`ErrorCodes.Exception`) must continue to behave exactly as today. FR-1's new check must be evaluated after the date-range check and before entity creation, so response semantics for existing test cases are unaffected.

**Acceptance criteria:**
- Existing unit tests for `RunDqtHandler` covering the date-range validation and the general persistence-exception path pass unmodified.
- A request with `DateFrom > DateTo` and an unsupported `TestType` still returns `DqtInvalidDateRange` (date check runs first, matching current code order).

## Non-Functional Requirements

### NFR-1: Performance
The added runner-existence check is an in-process `IEnumerable<IDqtJobRunner>` resolution plus a LINQ `Any`/`SingleOrDefault` over an in-memory collection (currently 2 runners) — negligible overhead, no additional I/O, no change to the synchronous request latency budget beyond microseconds.

### NFR-2: Reliability / Observability
No `DqtRun` may be left in `Running` state with a root cause that produced zero log output. Every failure path — synchronous rejection (FR-1) or an exception during background execution (FR-2, and the existing in-runner try/catch) — must result in either (a) no persisted run at all, or (b) a persisted run whose terminal state is `Completed` or `Failed` with a non-null `ErrorMessage`, plus a corresponding log entry.

## Data Model
No schema changes. `DqtRun` (`Anela.Heblo.Domain.Features.DataQuality.DqtRun`) keeps its existing shape: `Status` (`Running` / `Completed` / `Failed`), `ErrorMessage`, `CompletedAt`. This change only affects when the entity is created and ensures `Fail()` is reachable from one additional code path.

## API / Interface Design
`POST /api/data-quality/runs` (`RunDqtRequest` → `RunDqtResponse`) — no contract change. `RunDqtResponse` already has `Success` and `ErrorCode` fields; the only behavioral change is that a request for an unregistered `DqtTestType` now returns `Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType` synchronously (HTTP 200 with a failure payload, matching the existing pattern used for `DqtInvalidDateRange`) instead of `Success = true` with a run that silently never completes.

## Dependencies
- `IDqtJobRunner` implementations: `DriftDqtJobRunner`, `InvoiceDqtJobRunner` (both already registered via DI in `DataQualityModule`).
- `ErrorCodes.DqtUnsupportedTestType` (already defined, value `2204`, currently consumed by `GetDqtRunDetailHandler`) — reused, not newly introduced.
- `IServiceScopeFactory`, `IDqtRunRepository`, `TimeProvider`, `ILogger<RunDqtHandler>` — all already injected into `RunDqtHandler`.

## Out of Scope
- Reworking the fire-and-forget `Task.Run` pattern itself (e.g. moving to a proper background job queue/hosted service) — that is a separate architectural concern noted only as context.
- Adding retry or alerting for `Failed` DQT runs.
- Changes to `DriftDqtJobRunner`/`InvoiceDqtJobRunner`'s existing internal try/catch — those already correctly call `run.Fail()`.
- UI changes to surface the new synchronous rejection differently from other `Success = false` responses — the frontend already handles `Success = false` / `ErrorCode` generically.

## Open Questions

None.

## Status: COMPLETE
