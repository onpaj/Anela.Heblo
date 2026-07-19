# Design: Move `IMarketingTransactionSource` and `MarketingTransaction` from Domain to Application/Contracts

## Component Design

This is a pure code-relocation refactor. No new components, no behavioral change — two existing types move to a new namespace/folder, and their consumers update `using` statements accordingly.

### `IMarketingTransactionSource` (moved)
- **New location:** `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs`
- **New namespace:** `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`
- **Responsibility:** unchanged — outbound port abstracting a single ad platform's transaction feed (Meta, Google). Selected by `Platform` and invoked via `GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)`.
- **Implementors** (unchanged, only their `using` changes): `MetaAdsTransactionSource`, `GoogleAdsTransactionSource`.
- **Consumers** (unchanged, only their `using` changes): `ImportMarketingInvoicesHandler`, `MarketingInvoiceImportService`.
- Precedent: structurally identical to `Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogTransportSource` (Application-owned port, adapter-implemented).

### `MarketingTransaction` (moved)
- **New location:** `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs`
- **New namespace:** `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`
- **Responsibility:** unchanged — transient, non-persisted DTO carrying one raw transaction from an ad platform, including its raw JSON payload. Never mapped by EF; never exposed via HTTP/MediatR.
- Remains a plain class (not a record), per this repo's DTO convention.

### Unaffected components (explicitly out of scope, listed to make the boundary clear)
- `ImportedMarketingTransaction` (EF entity) — stays in `Anela.Heblo.Domain.Features.MarketingInvoices`.
- `IImportedMarketingTransactionRepository` — stays in `Anela.Heblo.Domain.Features.MarketingInvoices`.
- `MarketingInvoicesModule.cs` — no change; it never referenced either moved type.
- `ImportMarketingInvoicesRequest` / `ImportMarketingInvoicesResponse` (MediatR contracts) — no change.

### Reference/using updates required (mechanical, no logic change)
Files that need only a `using` swap (Domain → Application.Contracts):
- `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
- `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` (keep unrelated `BackgroundJobs` using)
- `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`
- `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` (keep unrelated `BackgroundJobs` using)
- `Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs`
- `Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs`
- `test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` (drop the now-dead Domain using)

Files that need **both** usings (Domain using retained for `IImportedMarketingTransactionRepository`/`ImportedMarketingTransaction`, Contracts using added for `IMarketingTransactionSource`/`MarketingTransaction`):
- `Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- `test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

No `.csproj` changes expected: both `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj` already reference `Anela.Heblo.Application` (not `Anela.Heblo.Domain`) — verify, don't assume, during implementation.

A repo-wide `git grep` for `IMarketingTransactionSource` and `MarketingTransaction\b` (excluding `ImportedMarketingTransaction`) must be re-run at implementation time to catch any reference not listed above.

## Data Schemas

No persisted schema changes. No API/MediatR/HTTP shape changes.

`MarketingTransaction` shape (unchanged, only namespace moves):

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

`IMarketingTransactionSource` shape (unchanged, only namespace moves):

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public interface IMarketingTransactionSource
{
    string Platform { get; }
    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
```

`ImportedMarketingTransaction` (EF entity) and `IImportedMarketingTransactionRepository` are unchanged and remain in `Anela.Heblo.Domain.Features.MarketingInvoices` — not part of this move.
