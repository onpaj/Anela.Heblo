# Architecture Review: Unit test coverage for GetIssuedInvoiceSyncStatsHandler

## Skip Design: true
Backend-only, test-only change. No UI/UX, no new visual components, no API
contract change — nothing for a designer to review.

## Architectural Fit Assessment
This is a pure coverage-gap fix, fully aligned with existing conventions:
xUnit + Moq + FluentAssertions unit tests against a MediatR handler, mocking
its single collaborator (`IIssuedInvoiceRepository`). The repository's own
`GetSyncStatsAsync` SQL/query behavior is already exercised separately by
`IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` and
`IssuedInvoiceRepositoryTests.cs` — this task only needs to pin the
handler's own branching (date defaulting, exception mapping, field mapping),
not repository internals. `docs/architecture/testing-strategy.md` confirms
this is exactly the "Unit Tests — MediatR handlers: all business logic,
validation, error scenarios" bucket (70% of the pyramid), and the existing
sibling fixture `GetIssuedInvoiceDetailHandlerTests.cs` (same directory,
same module) is a directly reusable structural template: same constructor
shape (`repositoryMock.Object`, `Mock.Of<ILogger<Handler>>()`), same
`[Fact]`/AAA layout, same assertion style.

No architectural risk: no new component, no new dependency, no schema
change, no interface change. Confirmed by reading the handler
(`GetIssuedInvoiceSyncStatsHandler.cs`), its request/response DTOs, and the
domain type `IssuedInvoiceSyncStats.cs` — all four files are exactly as
described in the spec, with `SyncSuccessRate` on the domain type being a
computed property (`SyncedInvoices / TotalInvoices * 100`), not a settable
field, which the spec already accounts for correctly in FR-4.

## Proposed Architecture

### Component Overview
```
GetIssuedInvoiceSyncStatsHandlerTests   (NEW — test-only)
        │  constructs
        ▼
GetIssuedInvoiceSyncStatsHandler        (UNCHANGED — production code)
        │  calls
        ▼
IIssuedInvoiceRepository.GetSyncStatsAsync   (MOCKED via Moq in the test)
```
No production component changes. The only new artifact is one test class.

### Key Design Decisions

#### Decision 1: Where the ambient clock (`DateTime.Now`) leaves the test
**Options considered:**
1. Introduce a clock abstraction (`TimeProvider`/`ISystemClock`) into the
   handler so tests can inject a fixed "now".
2. Leave the handler as-is; have the test compute its own expected
   `fromDate`/`toDate` from the real system clock at assertion time, same as
   the handler does, and assert with `Moq`'s `It.Is<DateTime>(...)` /
   captured-argument comparison on the `.Date` component only.

**Chosen approach:** Option 2 — no clock abstraction, no production code
touched.

**Rationale:** This is a coverage-only task (see spec NFR-2); introducing a
`TimeProvider` is a legitimate future improvement but is an unrelated,
unrequested production-code change that would expand blast radius for no
benefit to this issue's goal. Comparing on `.Date` (not exact
`DateTimeOffset`/millisecond) makes the test robust against the sub-millisecond
gap between the test computing its expected value and the handler executing;
a midnight-boundary flake is theoretically possible but not practically
reachable in CI (test executes in microseconds, not near midnight
deterministically), and is the same class of accepted risk the codebase
already carries wherever `DateTime.Now.Date` is used directly without a
clock seam. Do not add `Thread.Sleep`, retries, or freeze-time libraries —
none are already a project dependency, and adding one is out of scope for a
1-hour coverage task.

#### Decision 2: How to assert the repository call arguments
**Options considered:**
1. `It.IsAny<DateTime>()` for both dates — verifies *that* the repository
   was called but not *with what*, which is the exact defect class the
   issue calls out (it "silently returns statistics for the wrong window").
2. `It.Is<DateTime>(d => ...)` predicates (or `Callback`-captured arguments)
   asserting the exact expected `fromDate`/`toDate` values.

**Chosen approach:** Option 2, mandatory for FR-1 and FR-2 — this is the
whole point of the coverage gap being closed. A test using `It.IsAny<DateTime>()`
for the date arguments would satisfy the letter of "call `GetSyncStatsAsync`"
but not the issue's actual ask and must be treated as not meeting FR-1/FR-2.

#### Decision 3: Test class placement and naming
**Options considered:**
1. New file `GetIssuedInvoiceSyncStatsHandlerTests.cs` alongside the existing
   `GetIssuedInvoiceDetailHandlerTests.cs` in
   `backend/test/Anela.Heblo.Tests/Features/Invoices/`.
2. A subdirectory mirroring the production `UseCases/GetIssuedInvoiceSyncStats/`
   nesting.

**Chosen approach:** Option 1 — matches the existing flat convention in
`Features/Invoices/` (confirmed by directory listing: `IssuedInvoiceTests.cs`,
`GetIssuedInvoiceDetailHandlerTests.cs`,
`GetIssuedInvoicesListHandlerPaginationTests.cs`,
`GetRunningInvoiceImportJobsHandlerTests.cs`, etc. all sit flat in that one
folder, not mirrored into per-use-case subfolders). Class name
`GetIssuedInvoiceSyncStatsHandlerTests`, namespace
`Anela.Heblo.Tests.Features.Invoices` — exact mirror of
`GetIssuedInvoiceDetailHandlerTests`.

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file, no changes elsewhere:
```
backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
```

### Interfaces and Contracts
No new interfaces. Mock target: `IIssuedInvoiceRepository`
(`Anela.Heblo.Domain.Features.Invoices`), method under test:
```csharp
Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(
    DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
```
Handler under test constructor:
```csharp
public GetIssuedInvoiceSyncStatsHandler(
    IIssuedInvoiceRepository repository,
    ILogger<GetIssuedInvoiceSyncStatsHandler> logger)
```
Required `using`s mirror the sibling fixture: `Anela.Heblo.Application.Shared`
(for `ErrorCodes`), `Anela.Heblo.Domain.Features.Invoices` (for
`IIssuedInvoiceRepository`, `IssuedInvoiceSyncStats`),
`Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats`
(for the handler/request/response types), plus `Moq`, `FluentAssertions`,
`Xunit`, `Microsoft.Extensions.Logging`.

### Data Flow
Each test: construct `GetIssuedInvoiceSyncStatsRequest` → configure
`_repositoryMock.Setup(r => r.GetSyncStatsAsync(...))` to return a stubbed
`IssuedInvoiceSyncStats` (or `.ThrowsAsync(...)` for the exception case) →
`await _handler.Handle(request, CancellationToken.None)` → assert on the
returned `GetIssuedInvoiceSyncStatsResponse` and (for FR-1/FR-2) on the
exact arguments the mock was invoked with via `_repositoryMock.Verify(...)`
or an `It.Is<DateTime>` predicate inline in the `Setup`.

Four independent `[Fact]` tests, one per spec FR — no shared mutable state
beyond the standard per-test-instance mocks (xUnit creates a new test class
instance per test, matching the sibling fixture's non-static field pattern).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Date-comparison flake at exact midnight rollover between test setup and handler execution | Low | Compare `.Date` only (not time-of-day); accept as a pre-existing, codebase-wide characteristic of unabstracted `DateTime.Now.Date` usage, not something this test needs to solve |
| Test asserts `It.IsAny<DateTime>()` instead of exact values, silently failing to close the actual coverage gap the issue describes | Medium | Explicitly called out in Decision 2 / FR-1 / FR-2 as a review-blocking requirement — exact-value assertions on both dates are mandatory |
| `SyncSuccessRate` misunderstood as a settable field on `IssuedInvoiceSyncStats` (it is `TotalInvoices > 0 ? SyncedInvoices / TotalInvoices * 100 : 0`, computed) | Low | Spec FR-4 already flags this; test must set `TotalInvoices`/`SyncedInvoices` to values yielding a distinctive rate rather than trying to set `SyncSuccessRate` directly (it has no setter) |

## Specification Amendments
None — the spec (spec.r1.md) is architecturally sound as written and needs
no changes.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The test project
and its `Moq`/`FluentAssertions`/`xUnit` references already exist and are
already used identically by the sibling fixture.
