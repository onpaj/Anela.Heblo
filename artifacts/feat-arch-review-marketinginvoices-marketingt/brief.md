## Module
MarketingInvoices

## Finding
`MarketingTransaction.Platform` (`Domain/Features/MarketingInvoices/MarketingTransaction.cs:6`) is populated by both source adapters but never consumed by the import service.

Both adapters set it:
- `MetaAdsTransactionSource.cs:68`: `Platform = Platform`
- `GoogleAdsTransactionSource.cs:39`: `Platform = Platform`

But `MarketingInvoiceImportService.ImportAsync` always uses `source.Platform` when constructing `ImportedMarketingTransaction` (`Services/MarketingInvoiceImportService.cs:70`):
```csharp
Platform = source.Platform,   // transaction.Platform is never read
```

`transaction.Platform` has zero read sites in the entire codebase. Test data populates it, but no assertion ever checks it.

## Why it matters
The field creates a false impression that platform can vary per transaction (rather than being authoritative from the source), and suggests that the service validates or reconciles per-transaction vs source platform. A future developer could reasonably add logic that reads `transaction.Platform` expecting the service to honor it — silently getting no effect. It's YAGNI code in the domain contract.

## Suggested fix
Remove `Platform` from `MarketingTransaction`:
```csharp
// Domain/Features/MarketingInvoices/MarketingTransaction.cs
public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    // Remove: public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```
Remove the `Platform = Platform` initializer line from both adapter source files.

---
_Filed by daily arch-review routine on 2026-05-26._