# Design: InvoiceImportStatisticsSourceAdapter — remove direct ApplicationDbContext dependency

## Component Design

### `IIssuedInvoiceRepository` (Domain — `Anela.Heblo.Domain.Features.Invoices`)
Responsibility: sole abstraction the Application layer may depend on for `IssuedInvoice` persistence access. Gains one new read method alongside its existing ones (`GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, `RevertTrackedChangesAsync`):

```csharp
Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    ImportDateType dateType,
    CancellationToken cancellationToken = default);
```

Contract: returns one `DailyInvoiceCount` per calendar day in the inclusive range `[startDate.Date, endDate.Date]`, counting `IssuedInvoice` rows grouped by either `InvoiceDate` or `LastSyncTime` (selected by `dateType`); days with no matching invoices are present in the result with `Count = 0`; every `Date` in the result is `DateTimeKind.Utc`. This is an exact carry-over of the contract already documented on `IInvoiceImportStatisticsSource.GetDailyCountsAsync`.

### `IssuedInvoiceRepository` (Persistence — `Anela.Heblo.Persistence.Invoices`)
Responsibility: sole place EF Core queries against `IssuedInvoice`/`ApplicationDbContext` are allowed to live for this aggregate. Implements the new `GetDailyCountsAsync`, absorbing the query and gap-filling logic currently in the adapter, unchanged in behavior:
- Normalizes `startDate`/`endDate` to `DateTimeKind.Unspecified` for comparison against stored timestamps (same UTC→Unspecified conversion the adapter does today).
- Branches on `dateType` (`InvoiceDate` vs `LastSyncTime`, the latter filtered to `HasValue`), groups by (Year, Month, Day), orders ascending.
- Gap-fills every date in range with a zero-count `DailyInvoiceCount` where the query returned no row.

### `InvoiceImportStatisticsSourceAdapter` (Application — `Anela.Heblo.Application.Features.Invoices.Infrastructure`)
Responsibility: satisfies the Analytics-owned consumer contract `IInvoiceImportStatisticsSource` from within the Invoices module, translating the contract call into a call on the Invoices module's own domain abstraction. After this change it holds **no persistence logic** — it is a pure pass-through to `IIssuedInvoiceRepository.GetDailyCountsAsync`, mirroring `InvoiceConsumptionSourceAdapter`'s existing shape (satisfy contract → delegate to `IIssuedInvoiceRepository`). No change to its public interface, its DI lifetime, or its registration in `InvoicesModule.cs`.

### Unchanged components
`IInvoiceImportStatisticsSource` (Domain/Analytics, the consumer-owned contract), `GetInvoiceImportStatisticsHandler`/`Request`/`Response` (Analytics use case), `InvoiceImportStatisticsTile` (dashboard tile), `InvoicesModule.cs` (DI registrations already correct) — none of these are touched by this design.

## Data Schemas

No database schema change (no new tables/columns, no migration). No API request/response shape change — this is entirely internal to the Invoices module, below the `IInvoiceImportStatisticsSource` contract boundary that Analytics consumes.

Types involved (all pre-existing, reused as-is):

```csharp
// Anela.Heblo.Domain.Features.Analytics.DailyInvoiceCount
public class DailyInvoiceCount
{
    public DateTime Date { get; set; }   // DateTimeKind.Utc
    public int Count { get; set; }
}

// Anela.Heblo.Domain.Features.Analytics.ImportDateType
public enum ImportDateType
{
    InvoiceDate,
    LastSyncTime
}
```

Method shape (identical across the Domain interface and its Persistence implementation, and mirrored one-for-one by the Application adapter's delegating method):

```csharp
Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    ImportDateType dateType,
    CancellationToken cancellationToken = default);
```
