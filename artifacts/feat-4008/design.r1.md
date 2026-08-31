# Design: Unit test coverage for GetIssuedInvoiceSyncStatsHandler

## Component Design

### `GetIssuedInvoiceSyncStatsHandlerTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Invoices`
- **Responsibility:** Pin the branching behavior of
  `GetIssuedInvoiceSyncStatsHandler` — date-range defaulting, pass-through
  of explicit dates, exception-to-structured-failure mapping, and
  happy-path field mapping — against a mocked `IIssuedInvoiceRepository`.
  Production code is not modified.
- **Collaborators:**
  - `Mock<IIssuedInvoiceRepository>` — the handler's sole dependency,
    fully mocked; no database or real I/O.
  - `Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>()` — logger stub,
    not asserted on (log call correctness is not part of the coverage gap).
- **Test cases (one `[Fact]` each, corresponding 1:1 to spec FR-1..FR-4):**
  1. `Handle_BothDatesNull_DefaultsToTrailing30DayWindow` — both request
     dates `null`; asserts `GetSyncStatsAsync` invoked with
     `fromDate.Date == DateTime.Now.Date.AddDays(-30)` and
     `toDate.Date == DateTime.Now.Date`, using an exact `It.Is<DateTime>`
     predicate (not `It.IsAny`) on both arguments.
  2. `Handle_ExplicitDates_PassesThemThroughUnchanged` — both request
     dates set to fixed, distinct values; asserts `GetSyncStatsAsync`
     invoked with exactly those values.
  3. `Handle_RepositoryThrows_ReturnsStructuredFailure` — repository mock
     configured with `.ThrowsAsync(new InvalidOperationException(...))`;
     asserts `response.Success == false`,
     `response.ErrorCode == ErrorCodes.Exception`, and
     `response.Params["ErrorMessage"] == "Chyba při načítání statistik synchronizace faktur"`.
  4. `Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse` — repository
     mock returns a populated `IssuedInvoiceSyncStats` with distinct
     values per field; asserts each response field equals the
     corresponding source field one-to-one, including the computed
     `SyncSuccessRate`.
- **Structural template:** mirrors the existing sibling fixture
  `GetIssuedInvoiceDetailHandlerTests.cs` — same constructor-injected mock
  fields, same AAA (`// Arrange` / `// Act` / `// Assert`) comment
  structure, same use of `FluentAssertions`' `.Should()` API.

## Data Schemas
No schema changes — existing types consumed as-is by the new test class:

```csharp
// Request (nullable date range)
public class GetIssuedInvoiceSyncStatsRequest : IRequest<GetIssuedInvoiceSyncStatsResponse>
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

// Response (BaseResponse: Success, ErrorCode, Params)
public class GetIssuedInvoiceSyncStatsResponse : BaseResponse
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate { get; set; }
}

// Domain type returned by the mocked repository call
public class IssuedInvoiceSyncStats
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate => TotalInvoices > 0
        ? (decimal)SyncedInvoices / TotalInvoices * 100 : 0; // computed, no setter
}

// Mocked collaborator signature
Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(
    DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
```

No event payloads or API contract changes are involved — this handler is
invoked in-process via MediatR and the tests call `Handle(...)` directly.
