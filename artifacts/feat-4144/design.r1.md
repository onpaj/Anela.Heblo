# Design: Move MarketingImportResult to Contracts/ (MarketingInvoices)

## Component Design

No new or redesigned components. This change relocates one existing type within the already-established `MarketingInvoices` complex-feature structure; no responsibilities move between components and no interface changes.

- **`MarketingImportResult`** (relocated: `Features/MarketingInvoices/MarketingImportResult.cs` → `Features/MarketingInvoices/Contracts/MarketingImportResult.cs`)
  Plain internal result object with no behavior — three `int` auto-properties. Its role is unchanged: it is the return type `IMarketingInvoiceImportService.ImportAsync(...)` uses to report import counts back to its caller. Moving it into `Contracts/` aligns it with its two siblings (`MarketingTransaction`, `IMarketingTransactionSource`) that already live there as types shared across the feature's `Services/` and `UseCases/` layers.

- **`IMarketingInvoiceImportService` / `MarketingInvoiceImportService`** (`Services/`) — unchanged. Both already `using` the `Contracts` namespace (needed for `IMarketingTransactionSource`), so the relocated type resolves with no edit to these files. `ImportAsync(...)`'s declared return type keeps the same simple name.

- **`ImportMarketingInvoicesHandler`** (`UseCases/ImportMarketingInvoices/`) — unchanged. It consumes the service's result via an implicitly-typed (`var`) local and never names `MarketingImportResult` directly, so it is unaffected by the namespace change.

- **`ImportMarketingInvoicesHandlerTests`** (test) — one `using Anela.Heblo.Application.Features.MarketingInvoices;` line removed. It becomes unused once `MarketingImportResult` no longer lives in the feature-root namespace; the file's existing `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` already resolves the relocated type for `new MarketingImportResult { ... }`.

No changes to module registration (`MarketingInvoicesModule.cs`), DI wiring, MediatR pipeline, or any other file.

## Data Schemas

No schema change of any kind — no database, API, or event payload is affected.

`MarketingImportResult`'s shape is unchanged, byte-for-byte, apart from its namespace:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

This type is an internal application-layer object — it never crosses the MediatR/API boundary (`ImportMarketingInvoicesResponse` is the actual API-facing DTO) — so there is no OpenAPI contract impact and no client regeneration is triggered.
