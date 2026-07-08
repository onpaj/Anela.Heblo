# Specification: Inject TimeProvider into GetInvoiceImportStatisticsHandler

## Summary
`GetInvoiceImportStatisticsHandler` computes its query date range using `DateTime.UtcNow` directly instead of the injected `TimeProvider` abstraction used elsewhere in the Analytics module. This spec covers replacing the direct call with `TimeProvider`, matching the pattern already established by `TimeWindowParser`, `InvoiceImportStatisticsTile`, and the sibling `GetBankStatementImportStatisticsHandler`.

## Background
This is a small architecture-review fix, not a new feature. The Analytics module standardized on constructor-injected `TimeProvider` (registered in DI as the system default) to make "now" controllable in tests and consistent across components. `GetInvoiceImportStatisticsHandler` was missed during that standardization: it calls `DateTime.UtcNow.Date` directly (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs:37`), which makes the handler's date range non-deterministic in unit tests and inconsistent with `InvoiceImportStatisticsTile`, which reads from the same repository method but already uses `TimeProvider`.

## Functional Requirements

### FR-1: Inject `TimeProvider` into `GetInvoiceImportStatisticsHandler`
Add a `TimeProvider timeProvider` constructor parameter to `GetInvoiceImportStatisticsHandler`, store it in a `private readonly TimeProvider _timeProvider` field, and use `_timeProvider.GetUtcNow().Date` in place of `DateTime.UtcNow.Date` when computing `endDate` in `Handle()`. No DI registration change is required — `TimeProvider` is already registered in the container (proven by its existing use in `TimeWindowParser` and `InvoiceImportStatisticsTile`).

**Acceptance criteria:**
- The handler's constructor signature becomes `GetInvoiceImportStatisticsHandler(IAnalyticsRepository analyticsRepository, IOptions<InvoiceImportOptions> invoiceImportOptions, TimeProvider timeProvider)`.
- No remaining reference to `DateTime.UtcNow` (or `DateTime.Now`) exists in `GetInvoiceImportStatisticsHandler.cs`.
- `endDate` is computed as `_timeProvider.GetUtcNow().Date`, then normalized with `DateTime.SpecifyKind(endDate, DateTimeKind.Utc)`, preserving the existing `startDate = endDate.AddDays(-daysBack)` logic unchanged.
- Existing response shape, threshold logic, and repository call signature (`GetInvoiceImportStatisticsAsync(startDate, endDate, request.DateType, cancellationToken)`) are unchanged.
- The application builds and starts successfully (DI resolves `GetInvoiceImportStatisticsHandler` without error), confirming `TimeProvider` is already available in the container.

### FR-2: Update existing unit tests to supply a controllable `TimeProvider`
`backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` constructs `GetInvoiceImportStatisticsHandler` directly with two arguments (`_mockRepository.Object, options`) in three places. Once FR-1 lands, these call sites will fail to compile without a `TimeProvider` argument. Update the test file to follow the same pattern as `GetBankStatementImportStatisticsHandlerTests.cs`, which uses `Mock<TimeProvider>`.

**Acceptance criteria:**
- All `new GetInvoiceImportStatisticsHandler(...)` call sites in the test file pass a `TimeProvider` (e.g., a `Mock<TimeProvider>` configured with `GetUtcNow()` returning a fixed `DateTimeOffset`, consistent with the mocking style in `GetBankStatementImportStatisticsHandlerTests.cs`).
- Tests that currently assert against `DateTime.UtcNow.Date` (e.g., `Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`) are updated to assert against the fixed date supplied by the mocked `TimeProvider`, removing their dependency on wall-clock time.
- The full existing test suite in `GetInvoiceImportStatisticsHandlerTests.cs` passes with no flakiness introduced by clock/timing.
- No other test files, callers, or DI registrations require changes (confirm via a build-wide search for `new GetInvoiceImportStatisticsHandler(`).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a like-for-like substitution of one time-source call for another with no behavioral or performance impact.

### NFR-2: Security
Not applicable — no change to auth, data sensitivity, or external-facing surface.

## Data Model
No data model changes. No new entities, fields, or persistence changes.

## API / Interface Design
No public API, request, or response contract changes. `GetInvoiceImportStatisticsRequest` and `GetInvoiceImportStatisticsResponse` are untouched; only the handler's internal date computation and its constructor's dependency list change.

## Dependencies
- `TimeProvider` (`System`, .NET 8) — already registered in the DI container; no new registration needed.
- No external services or new libraries introduced.

## Out of Scope
- Changing the date-range computation logic itself (e.g., `daysBack` semantics, threshold logic, or repository query behavior).
- Refactoring other Analytics handlers beyond `GetInvoiceImportStatisticsHandler` and its test file.
- Introducing `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) if the codebase's established convention for this module is `Mock<TimeProvider>` — follow the existing sibling test's approach rather than introducing a new testing convention.

## Open Questions
None.

## Status: COMPLETE
