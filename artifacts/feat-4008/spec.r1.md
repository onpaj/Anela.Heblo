# Specification: Unit test coverage for GetIssuedInvoiceSyncStatsHandler

## Summary
`GetIssuedInvoiceSyncStatsHandler` (Invoices module) computes a default date
range when the caller omits `FromDate`/`ToDate`, delegates to
`IIssuedInvoiceRepository.GetSyncStatsAsync`, and maps the result — or an
exception — into `GetIssuedInvoiceSyncStatsResponse`. Line coverage is
currently 19.4%; the date-defaulting branch and the exception-handling branch
are both untested. This is a test-only change: add a unit test fixture that
exercises the handler's branches with a mocked repository. No production code
changes are required or in scope.

## Background
The stats page (invoice sync health dashboard) calls this handler through
MediatR. Two behaviors are load-bearing but currently unverified by any test:

1. When both `FromDate` and `ToDate` are `null`, the handler defaults to a
   30-day trailing window ending today (`DateTime.Now.Date.AddDays(-30)` to
   `DateTime.Now.Date`). A regression here (wrong sign, wrong date source,
   off-by-one) would silently change what date range the dashboard reports on
   — no exception, no visible failure, just wrong numbers.
2. When the repository call throws, the handler must catch it and return a
   structured, non-throwing failure (`Success = false`,
   `ErrorCode = ErrorCodes.Exception`, with a `Params["ErrorMessage"]` Czech
   message) rather than letting the exception propagate. A regression here
   would surface as an unhandled 500 on the frontend instead of a graceful
   error response.

The sibling handler `GetIssuedInvoiceDetailHandler` already has a test fixture
(`backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`)
using the same repository-mock + `xUnit` + `Moq` + `FluentAssertions` pattern
this task should follow.

## Functional Requirements

### FR-1: Date-range defaulting is covered
When `request.FromDate` and `request.ToDate` are both `null`, the handler
must call `_repository.GetSyncStatsAsync` with a `fromDate` equal to
`DateTime.Now.Date.AddDays(-30)` and a `toDate` equal to `DateTime.Now.Date`
(both re-derived at assertion time from the ambient clock, since the
production code uses `DateTime.Now` with no injected clock abstraction).

**Acceptance criteria:**
- A test asserts the exact `fromDate`/`toDate` values passed to
  `GetSyncStatsAsync` when both request dates are `null`, using
  `Moq`'s `It.Is<DateTime>(...)` (or capturing the call arguments) rather
  than `It.IsAny<DateTime>()`, so a sign flip (`AddDays(-30)` →
  `AddDays(+30)`) or a swapped `fromDate`/`toDate` would fail the test.
- Dates are compared allowing for the (near-zero, but real) risk of a
  midnight rollover between the moment the test computes its expected value
  and the moment the handler runs — assert on `DateTime.Date` components (no
  time-of-day) and note that `DateTime.Now.Date` is already midnight-safe
  under normal test run times; do not add artificial `Thread.Sleep`s or
  retries to compensate.

### FR-2: Explicit dates pass through unchanged
When `request.FromDate` and `request.ToDate` are both supplied, the handler
must pass those exact values to `GetSyncStatsAsync` without modification.

**Acceptance criteria:**
- A test supplies fixed, distinct `FromDate`/`ToDate` values and asserts
  `GetSyncStatsAsync` was called with exactly those values (not the
  30-day-default values).

### FR-3: Exception path returns a structured failure
When `_repository.GetSyncStatsAsync` throws any `Exception`, the handler must
catch it and return a response with:
- `Success == false`
- `ErrorCode == ErrorCodes.Exception`
- `Params` containing key `"ErrorMessage"` with value
  `"Chyba při načítání statistik synchronizace faktur"`
- All statistics fields (`TotalInvoices`, `SyncedInvoices`, `UnsyncedInvoices`,
  `InvoicesWithErrors`, `CriticalErrors`, `LastSyncTime`, `SyncSuccessRate`)
  left at their default/unset values (the handler does not populate them on
  the exception path).

**Acceptance criteria:**
- A test sets up the repository mock to throw (e.g. `InvalidOperationException`)
  and asserts the full response shape above, including the exact `Params`
  dictionary contents — not just `Success == false`.
- The test confirms the handler does not rethrow (the `await` completes
  normally and returns the failure response).

### FR-4: Happy path field mapping
When the repository returns a populated `IssuedInvoiceSyncStats`, every field
must be copied onto the response and `Success` must be `true`.

**Acceptance criteria:**
- A test seeds a `IssuedInvoiceSyncStats` with distinct, non-default values
  for `TotalInvoices`, `SyncedInvoices`, `UnsyncedInvoices`,
  `InvoicesWithErrors`, `CriticalErrors`, `LastSyncTime`, and asserts each
  field on the response equals the corresponding source value one-to-one
  (guards against a field being dropped or two fields being swapped during
  mapping). Note `SyncSuccessRate` on `IssuedInvoiceSyncStats` is a computed
  property (`SyncedInvoices / TotalInvoices * 100`), not an independently
  settable field — choose `TotalInvoices`/`SyncedInvoices` values that yield a
  distinctive, easily-asserted rate to confirm it round-trips onto the
  response.

## Non-Functional Requirements

### NFR-1: Test isolation and speed
Tests must not touch a real database, real clock injection, or real I/O.
`IIssuedInvoiceRepository` is mocked (`Moq`); no `DateTime` abstraction exists
in the production code today, so tests read the ambient system clock the same
way the handler does (see FR-1). Tests must run as pure in-memory unit tests
completing in milliseconds, consistent with the existing
`GetIssuedInvoiceDetailHandlerTests` fixture style.

### NFR-2: No production code changes
This is a coverage-only task. `GetIssuedInvoiceSyncStatsHandler.cs`,
`GetIssuedInvoiceSyncStatsRequest.cs`, `GetIssuedInvoiceSyncStatsResponse.cs`,
and `IssuedInvoiceSyncStats.cs` must not be modified. If a genuine bug is
found while writing tests (there is no evidence of one in the current code),
stop and flag it rather than silently fixing it — fixing behavior is out of
scope for a coverage task.

## Data Model
No new or changed data model. Existing types used as-is:
- `GetIssuedInvoiceSyncStatsRequest` — `FromDate?`, `ToDate?` (nullable
  `DateTime`).
- `GetIssuedInvoiceSyncStatsResponse : BaseResponse` — `TotalInvoices`,
  `SyncedInvoices`, `UnsyncedInvoices`, `InvoicesWithErrors`,
  `CriticalErrors`, `LastSyncTime?`, `SyncSuccessRate` (decimal), plus
  inherited `Success`, `ErrorCode`, `Params` from `BaseResponse`.
- `IssuedInvoiceSyncStats` (domain, returned by the repository) —
  `TotalInvoices`, `SyncedInvoices`, `UnsyncedInvoices`, `InvoicesWithErrors`,
  `CriticalErrors`, `LastSyncTime?`, and a computed `SyncSuccessRate`.
- `IIssuedInvoiceRepository.GetSyncStatsAsync(DateTime fromDate, DateTime
  toDate, CancellationToken)` — the single collaborator to mock.

## API / Interface Design
No API surface changes. This handler is invoked via MediatR
(`IRequestHandler<GetIssuedInvoiceSyncStatsRequest,
GetIssuedInvoiceSyncStatsResponse>`); tests call `Handle(request,
CancellationToken.None)` directly against a handler instance constructed with
a mocked `IIssuedInvoiceRepository` and a `Mock.Of<ILogger<...>>()`, mirroring
the constructor shape:

```csharp
new GetIssuedInvoiceSyncStatsHandler(
    _repositoryMock.Object,
    Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>());
```

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit, Moq,
  FluentAssertions — already referenced, see
  `GetIssuedInvoiceDetailHandlerTests.cs` for the established pattern).
- No new NuGet packages.
- No external services; `IIssuedInvoiceRepository` is fully mocked.

## Out of Scope
- Any change to `GetIssuedInvoiceSyncStatsHandler` production logic.
- Integration/DB-backed tests of `IssuedInvoiceRepository.GetSyncStatsAsync`
  itself (already covered separately by
  `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` /
  `IssuedInvoiceRepositoryTests.cs`).
- Introducing a clock abstraction (`ISystemClock`/`TimeProvider`) to make the
  date defaulting independently testable without reading the ambient clock —
  worth considering separately, but not required to hit the coverage goal for
  this issue and out of scope as a production-code change (see NFR-2).
- Frontend/dashboard changes.

## Open Questions
None.

## Status: COMPLETE
