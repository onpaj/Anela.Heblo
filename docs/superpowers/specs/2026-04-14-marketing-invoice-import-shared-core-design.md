# Marketing Invoice Import — Shared Core (Issue #606)

## Context

Anela runs advertising on Meta (Facebook) Ads and Google Ads (one account each, auto-pay, CZK, ~couple of invoices per week per platform). Currently billing invoices are handled manually. This spec covers the shared domain, application, and persistence foundation that both ad-platform adapters (#607 Meta, #608 Google) will build on.

**Out of scope:** FlexiBee received invoice creation, UI for viewing/managing imported transactions, Hangfire jobs (those live in adapter issues #607 and #608).

## Approach

Follow the existing `IssuedInvoice` + `InvoiceImportService` pattern:
- `ImportedMarketingTransaction` implements `IEntity<int>` (auto-increment PK, persistence-owned)
- Repository extends `BaseRepository<ImportedMarketingTransaction, int>`
- Service is concrete (no interface) — adapters inject it directly
- `ImportedMarketingTransaction` is lean: no `RawData` column

## Domain Layer — `Domain/Features/MarketingInvoices/`

### `IMarketingTransactionSource`
Interface each adapter implements:
```csharp
public interface IMarketingTransactionSource
{
    string Platform { get; }  // "MetaAds" or "GoogleAds"
    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
```

### `MarketingTransaction`
Pure value object (not an EF entity):
```
string TransactionId
string Platform
decimal Amount
DateTime TransactionDate
string Description
string Currency
string? RawData   // raw JSON from API, used in-memory only, not persisted
```

### `ImportedMarketingTransaction`
Persistence entity, implements `IEntity<int>`:
```
int Id               (auto-increment PK)
string TransactionId
string Platform
decimal Amount
DateTime TransactionDate
DateTime ImportedAt
bool IsSynced        (default false — set to true in future FlexiBee phase)
string? ErrorMessage
```

### `IImportedMarketingTransactionRepository`
Focused interface, no generic CRUD surface:
```csharp
Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
Task<int> SaveChangesAsync(CancellationToken ct);
```

## Application Layer — `Application/Features/MarketingInvoices/`

### `MarketingImportResult`
Simple result DTO:
```
int Imported
int Skipped
int Failed
```

### `MarketingInvoiceImportService`
Orchestration service (concrete class, no interface needed at this phase):

**Constructor:** `IMarketingTransactionSource source`, `IImportedMarketingTransactionRepository repository`, `ILogger<MarketingInvoiceImportService> logger`

**Method:** `ImportAsync(DateTime from, DateTime to, CancellationToken ct) → Task<MarketingImportResult>`

**Logic:**
1. Call `source.GetTransactionsAsync(from, to, ct)` to fetch transactions
2. For each transaction:
   - If `ExistsAsync(platform, transactionId)` → increment `Skipped`, continue
   - Otherwise: create `ImportedMarketingTransaction`, call `AddAsync` + `SaveChangesAsync`
   - Catch per-transaction exceptions: log error, increment `Failed`, continue (don't abort whole run)
   - On success: increment `Imported`
3. Return `MarketingImportResult`

### `MarketingInvoicesModule`
```csharp
public static IServiceCollection AddMarketingInvoicesModule(this IServiceCollection services)
{
    services.AddScoped<IImportedMarketingTransactionRepository, ImportedMarketingTransactionRepository>();
    services.AddScoped<MarketingInvoiceImportService>();
    return services;
}
```

`ApplicationModule` calls `services.AddMarketingInvoicesModule()`.

## Persistence Layer — `Persistence/Features/MarketingInvoices/`

### `ImportedMarketingTransactionConfiguration`
- Table: `imported_marketing_transactions`, schema `dbo`
- PK: `Id` (serial/auto-increment int)
- `Amount` → `decimal(18,2)`
- `TransactionDate` and `ImportedAt` → `timestamp without time zone`
- Unique index on `(Platform, TransactionId)` — core dedup constraint

### `ImportedMarketingTransactionRepository`
Extends `BaseRepository<ImportedMarketingTransaction, int>`, implements `IImportedMarketingTransactionRepository`:
- `ExistsAsync` → `AnyAsync(x => x.Platform == platform && x.TransactionId == transactionId)`
- `GetUnsyncedAsync` → `FindAsync(x => !x.IsSynced)`

### `ApplicationDbContext`
Add: `DbSet<ImportedMarketingTransaction> ImportedMarketingTransactions`

### EF Core Migration
Generated after configuration is in place:
```
dotnet ef migrations add AddImportedMarketingTransactions --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

## Tests — `Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

Three unit tests covering critical paths (mocked source + repository via Moq):

1. **Happy path** — source returns 2 new transactions → both persisted, result: `Imported=2, Skipped=0, Failed=0`
2. **Deduplication** — source returns 1 transaction that already exists → skipped, `AddAsync` never called, result: `Skipped=1`
3. **Per-transaction error** — source returns 2 transactions, second throws on `AddAsync` → first succeeds, error logged, result: `Imported=1, Failed=1`, run not aborted

## Acceptance Criteria

- [ ] Domain interfaces and models created
- [ ] EF Core entity config + migration created
- [ ] Repository implementation working
- [ ] Import service with deduplication logic implemented
- [ ] Unit tests for import service (mocked source + repository)
- [ ] `dotnet build` passes, `dotnet format` clean
