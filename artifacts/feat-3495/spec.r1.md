# Specification: Inject `TimeProvider` into `GetBankStatementImportStatisticsHandler`

## Summary
`GetBankStatementImportStatisticsHandler` computes its default end-date by calling `DateTime.UtcNow.Date` directly instead of using the module's established `TimeProvider` abstraction. This is a small architectural-hygiene fix: inject `TimeProvider` via constructor DI (already registered in the container as `TimeProvider.System`), replace the direct system-clock call with `_timeProvider.GetUtcNow().Date`, and add a unit test that proves the default-date branch is now controllable in tests. No behavior change is intended for production; the fix is testability and consistency only.

## Background
The Analytics module has already standardized on constructor-injected `TimeProvider` for anything that needs "now": `InvoiceImportStatisticsTile` (`backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs`) injects `TimeProvider` and calls `_timeProvider.GetUtcNow().Date`, and `TimeWindowParser` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`) does the same via `GetLocalNow()`. `TimeProvider` is registered once, application-wide, as a singleton (`services.AddSingleton(TimeProvider.System)` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`), so no DI registration changes are needed for this fix — the type is already resolvable everywhere.

`GetBankStatementImportStatisticsHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs:23`) does not follow this pattern:

```csharp
var endDate = request.EndDate ?? DateTime.UtcNow.Date;
```

`request.EndDate` (`GetBankStatementImportStatisticsRequest.EndDate`) is a nullable, optional field, and the frontend does not currently pass an explicit end date for this endpoint — meaning the `DateTime.UtcNow.Date` branch is the common, real-world code path, not an edge case. Because it calls the static system clock directly, this branch cannot be exercised deterministically in a unit test without a time-freezing shim (e.g. Microsoft.Extensions.Time.Testing `FakeTimeProvider` or a hand-rolled mock), and today there is no such test for it at all (there is no existing `GetBankStatementImportStatisticsHandlerTests` file in `backend/test/Anela.Heblo.Tests/Features/Analytics/`).

A sibling issue (#3488) flags the identical gap in `GetInvoiceImportStatisticsHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs:36`), which as of this writing also still calls `DateTime.UtcNow.Date` directly and is out of scope here — it is tracked and fixed independently. This spec covers only `GetBankStatementImportStatisticsHandler`.

## Functional Requirements

### FR-1: Handler must resolve "now" via injected `TimeProvider`
`GetBankStatementImportStatisticsHandler` must take a `TimeProvider` as a constructor dependency (in addition to its existing `IAnalyticsRepository` dependency) and store it in a private readonly field, following the exact pattern already used by `InvoiceImportStatisticsTile`.

**Acceptance criteria:**
- The constructor signature becomes `GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository, TimeProvider timeProvider)`.
- A private readonly field `_timeProvider` is assigned from the constructor parameter, matching the existing `_analyticsRepository` field style.
- No changes are made to DI registration (`ServiceCollectionExtensions.cs` or elsewhere) — `TimeProvider.System` is already registered as a singleton and MediatR/the DI container will resolve it automatically for the handler's added constructor parameter.

### FR-2: Default end-date must come from the injected clock, not the static system clock
Line 23's `var endDate = request.EndDate ?? DateTime.UtcNow.Date;` must become `var endDate = request.EndDate ?? _timeProvider.GetUtcNow().Date;`. No other line in the method changes.

**Acceptance criteria:**
- The only occurrence of `DateTime.UtcNow` in `GetBankStatementImportStatisticsHandler.cs` is removed and replaced with `_timeProvider.GetUtcNow().Date`.
- Behavior when `request.EndDate` is supplied is unchanged (the `??` fallback is untouched other than its right-hand side).
- The subsequent `DateTimeKind` normalization logic (lines 26–30: `DateTime.SpecifyKind(...)` for both `startDate` and `endDate`) is left exactly as-is — `GetUtcNow().Date` already yields a `DateTime` whose `.Kind` may be `Unspecified` after `.Date`, so the existing normalization block continues to guarantee UTC kind and must not be removed or altered.
- `startDate`'s derivation (`request.StartDate ?? endDate.AddDays(-30)`) is unchanged and continues to depend on the (now provider-sourced) `endDate`.

### FR-3: Unit test coverage for the default-date branch
A new test file `GetBankStatementImportStatisticsHandlerTests.cs` must be added under `backend/test/Anela.Heblo.Tests/Features/Analytics/` (none exists today), following the mocking pattern established in `InvoiceImportStatisticsTileTests.cs` (`Mock<TimeProvider>` with `.Setup(x => x.GetUtcNow()).Returns(fixedDateTime)`).

**Acceptance criteria:**
- A test constructs the handler with a mocked `IAnalyticsRepository` and a mocked `TimeProvider` whose `GetUtcNow()` returns a fixed, arbitrary `DateTimeOffset` (e.g. `2025-10-14T10:00:00Z`).
- A test calls `Handle` with a request that has `EndDate == null` (and, per current default behavior, `StartDate == null` too) and verifies that `IAnalyticsRepository.GetBankStatementImportStatisticsAsync` is invoked with `startDate`/`endDate` derived deterministically from the fixed mocked time (i.e. `endDate == fixedDate`, `startDate == fixedDate.AddDays(-30)`), not from the real wall clock.
- At least one test also verifies the pass-through behavior when `request.EndDate` (and/or `request.StartDate`) is explicitly supplied, confirming the injected `TimeProvider` is not consulted in that case (mirrors existing sibling-handler test coverage style, e.g. `GetInvoiceImportStatisticsHandlerTests.Handle_ShouldPassCorrectDateTypeToRepository`-style verification via `_mockRepository.Verify(...)`).
- All new and existing tests in the Analytics test folder pass under `dotnet test`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. `TimeProvider.GetUtcNow()` is a trivial call equivalent in cost to `DateTime.UtcNow`; no additional I/O, allocation of note, or async work is introduced.

### NFR-2: Security
Not applicable — this change touches only internal date-computation logic and does not affect authentication, authorization, or data exposure. No new external inputs or trust boundaries are introduced.

### NFR-3: Backward compatibility
The change must be behavior-preserving for all production and existing-test scenarios: given the same wall-clock time, `_timeProvider.GetUtcNow().Date` must produce the same effective date as `DateTime.UtcNow.Date` did (since `TimeProvider.System`, the only production registration, wraps the real system clock). Any existing caller relying on the endpoint's current default-date behavior sees no observable difference in production.

## Data Model
No data model changes. `GetBankStatementImportStatisticsRequest`, `GetBankStatementImportStatisticsResponse`, and `DailyBankStatementStatistics` are unaffected — this is a pure implementation-detail change inside the handler's constructor and `Handle` method.

## API / Interface Design
No public API surface changes. The MediatR request/response contract (`GetBankStatementImportStatisticsRequest` → `GetBankStatementImportStatisticsResponse`) and the controller endpoint that dispatches it are untouched. Only the handler's internal constructor dependency list changes (adding `TimeProvider`), which is resolved transparently by the existing DI container — no callers or consumers need to change.

## Dependencies
- `TimeProvider` (BCL type, .NET 8) — already registered as a singleton (`TimeProvider.System`) in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`. No new package or registration dependency.
- Existing `IAnalyticsRepository` dependency is unchanged.
- Test project already references `Moq` and the `Mock<TimeProvider>` pattern (see `InvoiceImportStatisticsTileTests.cs`), so no new test dependency is required.

## Out of Scope
- `GetInvoiceImportStatisticsHandler` (tracked separately under issue #3488) — not touched by this change.
- Any change to the 30-day default window, the `DateTimeKind` normalization logic, or the repository query signature.
- Any change to `TimeWindowParser` or `InvoiceImportStatisticsTile` — both already follow the correct pattern and serve only as reference implementations here.
- Any DI registration changes — `TimeProvider.System` registration already exists and is reused as-is.
- Any frontend changes — the frontend does not pass explicit end dates for this endpoint today, and that is not altered by this fix.

## Open Questions
None.

## Status: COMPLETE
