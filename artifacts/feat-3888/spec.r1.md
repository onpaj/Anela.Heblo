# Specification: Inject TimeProvider into TransportBoxCompletionService

## Summary
`TransportBoxCompletionService` is the only class in the Transport Boxes (Logistics) part that still reads the wall clock directly via `DateTime.UtcNow`, while every sibling handler in the same part injects `TimeProvider`. This specification covers replacing the three `DateTime.UtcNow` call sites with an injected `TimeProvider`, and extending the existing unit test suite to assert the resulting state-log timestamps against a frozen fake clock. It is a pure consistency/testability refactoring: no behavioral, schema, API, or scheduling change.

## Background

### Current state
`backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` is a background refresh task (registered via `RegisterRefreshTask`, runs every 2 minutes per `docs/features/complete-received-boxes-job.md`) that scans transport boxes in `Received` state and transitions each one to `Stocked` or `Error` depending on the state of its stock-up operations. Each transition writes a `TransportBoxStateLog` entry whose timestamp is supplied by the caller:

- `:91` — `box.Error(DateTime.UtcNow, "System", "No stock-up operations found for this box")`
- `:111` — `box.ToPick(DateTime.UtcNow, "System")`
- `:131` — `box.Error(DateTime.UtcNow, "System", errorMessage)`

`TransportBox.ChangeState(...)` (in `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`) uses that value for both `LastStateChanged` and the appended `TransportBoxStateLog` entry, so the injected value is persisted, not merely logged.

### Established pattern in this part
Every sibling handler in the Logistics part already takes `TimeProvider` as a constructor parameter and derives timestamps from it:

- `AddItemToBoxHandler` — `TimeProvider timeProvider` is the last constructor parameter; `var timestamp = _timeProvider.GetUtcNow().UtcDateTime;`
- `ChangeTransportBoxStateHandler` — same constructor shape; `_timeProvider.GetUtcNow().UtcDateTime` at the `Close(...)` call site (`:243`)
- `RemoveItemFromBoxHandler`, `OpenOrResumeBoxByCodeHandler`, `CreateNewTransportBoxHandler` — same pattern

`TimeProvider.System` is registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` (`AddCrossCuttingServices`), so the dependency is already resolvable everywhere in the composition root.

### Why it matters
This service is unattended, recurring background work whose only externally visible product — besides the state change itself — is a timestamped audit trail. Because it reads the real clock, its unit test suite (`backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`) cannot assert anything about those timestamps and runs against wall-clock time. It is also the one place in the part where a faked/time-travelled clock would silently fail to apply, making the module's time behaviour non-uniform for testing and future debugging.

### Assumptions
1. `TimeProvider` (BCL `System.TimeProvider`) is the intended abstraction — no project-specific `IDateTimeProvider` wrapper exists or should be introduced.
2. `DateTimeOffset.UtcDateTime` (Kind = `Utc`) is an exact behavioural substitute for `DateTime.UtcNow` for persistence purposes; Npgsql's `timestamp with time zone` mapping requires `DateTimeKind.Utc` and both expressions satisfy it.
3. The refactoring is limited to `TransportBoxCompletionService` and its test file; no other `DateTime.UtcNow` occurrence in the repository is in scope.

## Functional Requirements

### FR-1: Inject `TimeProvider` into `TransportBoxCompletionService`
Add a `TimeProvider` constructor parameter to `TransportBoxCompletionService`, stored in a `private readonly TimeProvider _timeProvider;` field, following the shape used by the sibling handlers in this part.

Details:
- Parameter name: `timeProvider`; field name: `_timeProvider`.
- Position: appended as the **last** constructor parameter, after `stockOperationQueryService`, matching `AddItemToBoxHandler` / `ChangeTransportBoxStateHandler` where `TimeProvider` is last.
- No change to `ITransportBoxCompletionService` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`) — the dependency is a constructor concern, not part of the contract.
- No `ArgumentNullException` guard is added; the existing constructor performs plain assignments for its other dependencies and that style is retained.

**Acceptance criteria:**
- `TransportBoxCompletionService` has a `private readonly TimeProvider _timeProvider` field assigned in the constructor.
- The constructor signature is `(ILogger<TransportBoxCompletionService> logger, ITransportBoxRepository transportBoxRepository, ILogisticsStockOperationQueryService stockOperationQueryService, TimeProvider timeProvider)`.
- `backend/src/Anela.Heblo.Application` compiles with `dotnet build` and is clean under `dotnet format`.

### FR-2: Replace all three `DateTime.UtcNow` call sites
Replace every direct wall-clock read in `TransportBoxCompletionService` with `_timeProvider.GetUtcNow().UtcDateTime`.

Call sites (current line numbers):
| Line | Current | Replacement |
|------|---------|-------------|
| 91 | `box.Error(DateTime.UtcNow, "System", "No stock-up operations found for this box")` | `box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", "No stock-up operations found for this box")` |
| 111 | `box.ToPick(DateTime.UtcNow, "System")` | `box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System")` |
| 131 | `box.Error(_timeProvider…, "System", errorMessage)` | `box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage)` |

Decision (assumption, see Open Questions): the timestamp is read **inline at each call site** rather than hoisted into a single local at the top of `ProcessBoxAsync`. The three branches are mutually exclusive — at most one executes per box — so inlining reads the clock exactly once per state transition and keeps the diff minimal. Do not use `DateTime.SpecifyKind(...)` around the expression: `DateTimeOffset.UtcDateTime` already returns `DateTimeKind.Utc`. (`ChangeTransportBoxStateHandler:118` wraps it in `SpecifyKind` redundantly; that is pre-existing and explicitly not to be changed here.)

**Acceptance criteria:**
- `grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` returns no matches.
- All three transitions use `_timeProvider.GetUtcNow().UtcDateTime`.
- The `"System"` user string, the error messages, the branch conditions, the ordering of `UpdateAsync`/`SaveChangesAsync`, the returned `BoxProcessingResult` values, and all log statements are byte-for-byte unchanged.
- Under `TimeProvider.System`, timestamps written are indistinguishable from the previous behaviour (Kind = `Utc`, current UTC instant).

### FR-3: Dependency injection continues to resolve without registration changes
`LogisticsModule.AddLogisticsModule` (`backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs:28`) registers `services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>()` using constructor-based activation, so the new `TimeProvider` parameter is satisfied automatically by the singleton registered in `AddCrossCuttingServices`. No DI code change is required or permitted by this specification.

**Acceptance criteria:**
- `LogisticsModule.cs` is unmodified by this change.
- The background refresh task registration (`RegisterRefreshTask<ITransportBoxCompletionService>(nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync), ...)`) is unmodified.
- `ApplicationStartupTests` (`backend/test/Anela.Heblo.Tests/ApplicationStartupTests.cs`) still passes — the application host starts and resolves its graph.
- Resolving `ITransportBoxCompletionService` from the application's service provider succeeds (no `InvalidOperationException: Unable to resolve service for type 'System.TimeProvider'`).

### FR-4: Update the unit test suite to use a frozen clock
Update `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` to construct the service with a fake, frozen `TimeProvider` instead of implicitly depending on the wall clock.

Details:
- Use `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing` (`using Microsoft.Extensions.Time.Testing;`). The package `Microsoft.Extensions.TimeProvider.Testing` v8.1.0 is already referenced in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:26` and is used the same way by `TimePeriodResolverTests` and `InventoryCountTileBaseTests`. Do **not** hand-roll a local `TimeProvider` subclass.
- Declare a single frozen instant as a `private static readonly DateTimeOffset FrozenNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);` (any fixed UTC instant is acceptable) and pass `new FakeTimeProvider(FrozenNow)` as the fourth constructor argument in the test-class constructor.
- Keep the existing field/constructor arrangement of the test class (Moq mocks assigned in the constructor, `_service` built there) — extend it, do not restructure it.

**Acceptance criteria:**
- The test class constructs `TransportBoxCompletionService` with a `FakeTimeProvider` frozen at `FrozenNow`.
- All seven existing tests still pass unmodified in intent (`NoReceivedBoxes_DoesNothing`, `AllOperationsCompleted_TransitionsBoxToStocked`, `AnyOperationFailed_TransitionsBoxToError`, `OperationsPending_LeavesBoxInReceived`, `NoOperationsForBox_TransitionsToError`, `MultipleBoxes_ProcessesAll`, `OperationsSubmitted_LeavesBoxInReceived`).
- No test references `DateTime.UtcNow` or otherwise depends on the real clock.

### FR-5: Assert timestamps against the frozen clock
Extend test coverage so the injected clock is actually observable — otherwise FR-1/FR-2 are untested. Add assertions on `LastStateChanged` and on the appended `TransportBoxStateLog` entry.

Required new/extended coverage (at minimum):
1. **Stocked transition** — for the `AllOperationsCompleted` case: `box.LastStateChanged` equals `FrozenNow.UtcDateTime`, and the newest `TransportBoxStateLog` entry for state `Stocked` carries the same timestamp, user `"System"`.
2. **Error transition, failed operations** — for the `AnyOperationFailed` case: `box.LastStateChanged` equals `FrozenNow.UtcDateTime` and the `Error` state-log entry carries the same timestamp.
3. **Error transition, no operations** — for the `NoOperationsForBox` case: same timestamp assertion.
4. **Clock advance is honoured** — one test that advances the fake clock (`fakeTimeProvider.Advance(TimeSpan.FromHours(1))` or a second service instance at a different frozen instant) and asserts the written timestamp reflects the advanced value, proving the service reads the injected clock rather than the wall clock on every call.

Notes for the implementer:
- The service's state-log entries are reachable through the `TransportBox` aggregate's state-log collection; use whichever public accessor `TransportBox` exposes (`StateLog`) and select the last entry. If the collection is not publicly readable, asserting `LastStateChanged` alone satisfies criteria 1–4 and the state-log assertion may be dropped — but `LastStateChanged` assertions are mandatory.
- Use the existing FluentAssertions style (`.Should().Be(...)`) already used throughout the file.
- Keep using the existing `CreateBox` / `CreateStatus` / `SetupQueryReturns` helpers; extend them only if strictly necessary.

**Acceptance criteria:**
- At least one assertion per transition kind (`Stocked`, `Error`-from-failed, `Error`-from-empty) binds the persisted timestamp to the frozen instant.
- At least one test proves the timestamp changes when the fake clock advances.
- A deliberate reintroduction of `DateTime.UtcNow` at any of the three call sites causes at least one test to fail (this is the regression guard the whole change exists for).
- `dotnet test` for `Anela.Heblo.Tests` passes with no new warnings.

### FR-6: No behavioural change
This is a refactoring. Runtime behaviour under the production registration (`TimeProvider.System`) must be identical.

**Acceptance criteria:**
- No change to the set of boxes selected, the branch decision logic, the number of `UpdateAsync`/`SaveChangesAsync` calls, the exception handling in `CompleteReceivedBoxesAsync`, or the summary counters (`completedCount` / `errorCount` / `skippedCount`).
- No change to log message templates or levels.
- No change to `ITransportBoxCompletionService`, `TransportBox`, `TransportBoxStateLog`, `LogisticsModule`, or any configuration file.
- `docs/features/complete-received-boxes-job.md` requires no update (the documented behaviour is unchanged); do not edit it.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. `TimeProvider.System.GetUtcNow()` has the same cost profile as `DateTime.UtcNow` (both resolve to the OS clock); one virtual dispatch and one `DateTimeOffset`→`DateTime` conversion are added per state transition, on a task that runs every 2 minutes over a small set of boxes. Target: no regression in `CompleteReceivedBoxesAsync` wall-clock duration — no benchmarking required.

### NFR-2: Security
Not applicable. No authentication, authorization, secret, or PII surface is touched. The service already runs unattended under the synthetic `"System"` user; that string is unchanged. Timestamps written to `TransportBoxStateLog` remain an audit trail — the change makes their source explicit and controllable rather than implicit, which is a small integrity improvement (a faked clock in a test environment can no longer be bypassed by this one class).

### NFR-3: Maintainability / consistency
After the change, every class in the Transport Boxes part obtains time from an injected `TimeProvider`. A future grep for `DateTime.UtcNow` under `backend/src/Anela.Heblo.Application/Features/Logistics/Services/` returns nothing, and the part has a single, uniform time-access convention.

### NFR-4: Test determinism
The `TransportBoxCompletionServiceTests` suite must be fully deterministic with respect to time: no dependence on the machine clock, time zone, or execution instant, and no flakiness around date boundaries (midnight/DST).

## Data Model
No change. `TransportBox`, `TransportBoxStateLog`, and every EF Core mapping and migration are untouched. The values written to `TransportBox.LastStateChanged` and `TransportBoxStateLog.Date` keep the same type (`DateTime`, `DateTimeKind.Utc`) and, in production, the same values as before — only the source of the instant changes from a static call to an injected dependency.

## API / Interface Design
No change. This service is not exposed over HTTP; it is invoked by `BackgroundRefreshSchedulerService` through the registered refresh task. `ITransportBoxCompletionService.CompleteReceivedBoxesAsync(CancellationToken)` is unchanged, so no OpenAPI regeneration and no frontend/TypeScript-client change is involved. The only interface touched is the non-public constructor signature of the concrete `TransportBoxCompletionService` class, whose sole non-test consumer is the DI container.

## Dependencies
- **`System.TimeProvider`** (BCL, .NET 8) — already used throughout the codebase; `TimeProvider.System` is registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`. No new package.
- **`Microsoft.Extensions.TimeProvider.Testing` v8.1.0** — already referenced by `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:26`; supplies `FakeTimeProvider`. No new package.
- No external service, migration, feature flag, configuration key, or deployment step is involved.

### Files expected to change
1. `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`
2. `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

No other file should appear in the diff.

### Validation before completion
- `dotnet build` (backend solution) — clean.
- `dotnet format` — no diff.
- `dotnet test` for `backend/test/Anela.Heblo.Tests` — the Logistics service tests plus `ApplicationStartupTests` and `Architecture/ModuleBoundariesTests` pass.
- No frontend build/lint and no E2E run is required: nothing under `frontend/` changes and no HTTP contract moves.

## Out of Scope
- `DateTime.UtcNow` at `backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/TransportBoxBaseTile.cs:47` and every other `DateTime.UtcNow`/`DateTime.Now` occurrence elsewhere in the repository. If a sweep is wanted, it belongs in a separate issue.
- The redundant `DateTime.SpecifyKind(...)` wrapper at `ChangeTransportBoxStateHandler:118`.
- Introducing null-argument guards, changing the `AddTransient` lifetime, or otherwise restructuring `LogisticsModule` registrations.
- Changing the `"System"` actor string, moving it to a constant, or plumbing `ICurrentUserService` into the service.
- Changing the refresh-task schedule, initial delay, or hydration tier in `appsettings.json`.
- Changing the branch logic, error messages, logging, or transaction/save granularity of `ProcessBoxAsync`.
- Any change to `ITransportBoxCompletionService`, the `TransportBox` aggregate, or the module-boundary allowlists in `ModuleBoundariesTests`.
- Integration or E2E tests for this service; unit tests with a fake clock are the agreed level of coverage.
- Documentation updates (`docs/features/complete-received-boxes-job.md` describes behaviour that does not change).

## Open Questions
None. The one design choice with real latitude — reading the clock inline at each of the three mutually exclusive call sites versus hoisting one `var timestamp = _timeProvider.GetUtcNow().UtcDateTime;` at the top of `ProcessBoxAsync` (the shape `AddItemToBoxHandler` uses) — is resolved in FR-2 in favour of inline reads, because at most one branch executes per box, so hoisting would add a clock read on the skip path and enlarge the diff for no behavioural gain. A reviewer preferring the hoisted form may say so; both satisfy every acceptance criterion.

## Status: COMPLETE
