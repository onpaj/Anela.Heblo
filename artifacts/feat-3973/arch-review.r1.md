# Architecture Review: Fix silent swallow of "no runner registered" error in RunDqtHandler

## Skip Design: true

## Architectural Fit Assessment
This is a small, well-contained backend correctness fix inside a single MediatR handler (`RunDqtHandler`), in a module (`DataQuality`) that already has the exact building blocks needed: a `DqtRunRepository`, a `DqtRun.Fail(errorMessage, completedAt)` domain method, an `ErrorCodes.DqtUnsupportedTestType` (2204) constant already defined and already consumed by a sibling handler (`GetDqtRunDetailHandler`), and two `IDqtJobRunner` implementations (`DriftDqtJobRunner`, `InvoiceDqtJobRunner`) that already demonstrate the correct try/catch/`Fail()`/`SaveChangesAsync` pattern this fix needs to extend one level up. No new abstractions, packages, or cross-module dependencies are required — the fix is fully local to `RunDqtHandler.cs` (plus the DI registration is already in place in `DataQualityModule.cs`, unchanged).

Verified via direct code read (`DataQualityModule.cs`): both `IDqtJobRunner` implementations are registered `AddScoped`, resolved through `IServiceScopeFactory.CreateScope()` inside the fire-and-forget task — this pattern (documented inline in the handler: "the HTTP request scope is disposed before RunAsync completes") is correct and must be preserved.

## Proposed Architecture

### Component Overview
```
RunDqtHandler.Handle(request)
 ├─ [1] validate DateFrom <= DateTo                     (existing, unchanged)
 ├─ [2] NEW: resolve IEnumerable<IDqtJobRunner> from a
 │        scope, check Any(r => r.CanHandle(TestType))
 │        -> if none: return Success=false,
 │           ErrorCode=DqtUnsupportedTestType
 │           (no DqtRun persisted)
 ├─ [3] DqtRun.Start(...) + repository.AddAsync/Save    (existing, unchanged; now only
 │                                                        reached once a runner is confirmed)
 └─ [4] _ = Task.Run(async () => {
          using scope = _scopeFactory.CreateScope();
          try {
              runner = scope.ServiceProvider
                  .GetServices<IDqtJobRunner>()
                  .SingleOrDefault(r => r.CanHandle(request.TestType))
                  ?? throw new InvalidOperationException(...);   // now inside try
              await runner.RunAsync(run.Id);
          } catch (Exception ex) {                                // NEW safety net
              _logger.LogError(ex, "...", run.Id, request.TestType);
              var scopedRepo = scope.ServiceProvider.GetRequiredService<IDqtRunRepository>();
              var scopedRun = await scopedRepo.GetByIdAsync(run.Id, CancellationToken.None);
              scopedRun?.Fail(ex.Message, _timeProvider.GetUtcNow().DateTime);
              await scopedRepo.SaveChangesAsync(CancellationToken.None);
          }
        }, CancellationToken.None);
```

### Key Design Decisions

#### Decision 1: Synchronous pre-check vs. try/catch-only fix
**Options considered:**
- (a) Only wrap the `Task.Run` body in try/catch, calling `run.Fail()` on the pre-existing `run` instance from the outer scope.
- (b) Validate a runner exists *before* persisting the `DqtRun`, rejecting synchronously with `DqtUnsupportedTestType`; additionally wrap the `Task.Run` body in try/catch as defense-in-depth.

**Chosen approach:** (b) — both, per the spec's FR-1 and FR-2.

**Rationale:** The spec (FR-1) requires that an unsupported `DqtTestType` never even create a `DqtRun` row — a synchronous 200-with-`Success:false` response is strictly better UX than a run that starts as `Running` and flips to `Failed` moments later, and it matches the existing convention (`DqtInvalidDateRange` is already rejected synchronously, before any persistence, for a different pre-condition on the same handler). It also reuses `ErrorCodes.DqtUnsupportedTestType`, which already exists specifically for "no matching runner/comparer" and is otherwise unused by `RunDqtHandler` — a strong signal this was the intended path. The try/catch in `Task.Run` (FR-2) is retained as defense-in-depth per the spec, not as the primary fix, because relying solely on it still allows a transiently-orphaned `DqtRun` row to exist for a few hundred milliseconds between insert and background-task failure, and duplicates the runner-lookup logic's failure surface across two code paths instead of rejecting once, early.

#### Decision 2: Where the try/catch obtains its `IDqtRunRepository` and `DqtRun` reference
**Options considered:**
- (a) Reuse the outer `_repository` field and the `run` local captured by the closure.
- (b) Resolve a fresh `IDqtRunRepository` from the `Task.Run`'s own `scope` and re-fetch the `DqtRun` by id.

**Chosen approach:** (b).

**Rationale:** The handler's own code comment already documents *why* the scoped resolution pattern exists: "the HTTP request scope is disposed before `RunAsync` completes, so capturing `_jobRunner` directly would cause `ObjectDisposedException` on the DbContext." The same reasoning applies to `_repository` — it is scoped to the same HTTP request `DbContext` and must not be captured into the background task for the same reason `IDqtJobRunner` isn't. Both existing `IDqtJobRunner` implementations already follow this exact pattern (`DriftDqtJobRunner.RunAsync` resolves its own `_repository` inside its own constructor-injected, per-call scope and calls `run.Fail(...)` on an entity it fetched itself via `_repository.GetByIdAsync`). The new catch block must mirror that: resolve `IDqtRunRepository` from the `Task.Run`'s `scope.ServiceProvider`, re-fetch by `run.Id`, call `.Fail(...)`, then `SaveChangesAsync` on that scoped repository — never the outer `_repository`/`run` captured from `Handle`'s own DbContext-bound scope.

#### Decision 3: Validation lookup must not duplicate/diverge from the fire-and-forget lookup
**Options considered:**
- (a) Two independently-written `CanHandle` checks (one in `Handle` before persistence, one inside `Task.Run`).
- (b) Same predicate shape (`services.Any(r => r.CanHandle(request.TestType))` before persistence; `services.SingleOrDefault(r => r.CanHandle(request.TestType)) ?? throw` inside the task, unchanged from today), each resolving its own `IEnumerable<IDqtJobRunner>` from its own scope.

**Chosen approach:** (b).

**Rationale:** `IDqtJobRunner` registrations are static (module-level DI, not per-request/runtime-conditional), so a resolve-and-check in `Handle`'s own request scope and a second resolve-and-check in the `Task.Run`'s scope will always agree in practice — there's no need to thread the resolved runner instance through the closure (that would fight the existing scope-isolation design from Decision 2). Keep the validation check in `Handle` scoped to that method's own `IServiceScopeFactory.CreateScope()` (or reuse whatever scope is already available there — see Prerequisites), separate from the one created inside `Task.Run`.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes are inside:
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — the only file that changes.

No changes needed to `DataQualityModule.cs` (DI registrations are already correct), `DqtRun.cs` (domain method `Fail` already has the right shape), `ErrorCodes.cs` (`DqtUnsupportedTestType` already exists), or either `IDqtJobRunner` implementation.

### Interfaces and Contracts
No public interface or contract changes. `RunDqtRequest`/`RunDqtResponse` DTOs are unchanged in shape; only the *value* of `ErrorCode` on an already-existing failure path (`Success = false`) changes for one previously-mishandled input (unsupported `TestType`).

### Data Flow
1. `Handle` validates date range (unchanged).
2. `Handle` creates an `IServiceScopeFactory` scope, resolves `IEnumerable<IDqtJobRunner>`, checks `Any(r => r.CanHandle(request.TestType))`.
   - If false: return `RunDqtResponse { Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType }` immediately. No `DqtRun` created, no scope leaked (dispose the validation scope, e.g. via `using`).
   - If true: proceed exactly as today — `DqtRun.Start(...)`, `AddAsync`, `SaveChangesAsync`, return `Success = true`.
3. The existing outer `catch (Exception ex)` around persistence is unchanged and still returns `ErrorCodes.Exception` for e.g. a DB failure during `SaveChangesAsync`.
4. The fire-and-forget `Task.Run` body now runs entirely inside a `try/catch`. On success, behavior is identical to today. On any exception (including a theoretical future lookup miss, now expected to be unreachable in practice because of step 2's guarantee): log the error, re-fetch the `DqtRun` via a repository resolved from the task's own scope, call `.Fail(ex.Message, timeProvider.GetUtcNow().DateTime)`, `SaveChangesAsync` on that scoped repository.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Validation scope (step 2) and fire-and-forget scope (step 4) resolve `IDqtJobRunner` instances differently because of scoped-service state | Low | `IDqtJobRunner` registrations are stateless module registrations (`AddScoped` but hold no request-specific state relevant to `CanHandle`); `CanHandle` is a pure predicate over `DqtTestType`. No divergence expected; call this out in code review if either runner's `CanHandle` is ever changed to depend on scoped state. |
| Existing test `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked` (in `RunDqtHandlerTests.cs`) currently asserts `Assert.True(response.Success)` and its own comment documents this exact bug as "pre-existing, out-of-scope" | High (test will fail after fix, must be updated as part of this change) | Update this test's assertions to `Assert.False(response.Success)` / `Assert.Equal(ErrorCodes.DqtUnsupportedTestType, response.ErrorCode)` / `Assert.Null(response.DqtRunId)`, add `_repositoryMock.Verify(r => r.AddAsync(...), Times.Never)`, and drop the now-inapplicable `await Task.Delay(100)` comment about the swallowed exception. This is not optional cleanup — the task-plan must include it explicitly so it isn't missed as "test framework noise." |
| Introducing a second scope-creation call in `Handle` (for validation) adds a small amount of overhead per request | Negligible | `CreateScope()`/dispose is cheap and already used once per request in this same handler for the fire-and-forget path; a second short-lived scope for validation is consistent with existing cost profile. |
| A future `IDqtJobRunner` implementation with expensive/side-effecting `CanHandle` could make the new synchronous check slow | Low | Out of scope for this fix — both current implementations' `CanHandle` are pure/cheap (`_comparers.Any(...)`, presumably similar in `InvoiceDqtJobRunner`). Flag as a note for future `IDqtJobRunner` authors, not a change here. |

## Specification Amendments
- Add to FR-1's acceptance criteria (or as a new FR-1a): the existing test `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked` in `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` must be updated to assert the new synchronous-rejection behavior (`Success = false`, `ErrorCode = DqtUnsupportedTestType`, `DqtRunId = null`, `AddAsync` never called) — this is a required part of the implementation task, not incidental test churn.
- FR-2's "same in-memory `run` instance" language should be corrected: per Decision 2 above, the catch block must **not** reuse the outer `run`/`_repository` (DbContext lifetime hazard) — it must resolve a fresh `IDqtRunRepository` from the `Task.Run`'s own `scope` and re-fetch the entity by `run.Id`, exactly like `DriftDqtJobRunner.RunAsync` does today.

## Prerequisites
None. No migrations, no config, no infrastructure changes — implementation can start immediately against `main`/this feature branch.
