## Module
MarketingInvoices

## Finding
`MarketingTransaction` (the domain value object returned by `IMarketingTransactionSource`) declares three fields that are never mapped to `ImportedMarketingTransaction` during the import:

```
backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs
  line 8:   public string Description { get; set; }
  line 9:   public string Currency { get; set; }
  line 11:  public string? RawData { get; set; }
```

The import service constructs the persisted entity without any of these:

```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs
  lines 58–65:
      var entity = new ImportedMarketingTransaction
      {
          TransactionId = transaction.TransactionId,
          Platform = source.Platform,
          Amount = transaction.Amount,
          TransactionDate = transaction.TransactionDate,
          ImportedAt = DateTime.UtcNow,
          IsSynced = false,
      };
```

`Currency` is the most critical omission: `Amount` is stored as `numeric(18,2)` without any currency context (`ImportedMarketingTransactionConfiguration.cs`, line 33). If a Google Ads or Meta Ads account operates in EUR while the rest of the system assumes CZK, the stored amounts are silently misrepresented.

## Why it matters
Storing a monetary `Amount` without its `Currency` is an implicit assumption that will break if ad accounts ever bill in a currency other than the expected one. The domain model signals these fields matter (otherwise why declare them?) but the persistence layer discards them with no comment. This is either a latent bug or unnecessary bloat on `MarketingTransaction`.

## Suggested fix
Choose one path:

**A — Persist currency (preferred if multi-currency is possible):**
1. Add `Currency string` column to `ImportedMarketingTransaction` and `ImportedMarketingTransactionConfiguration`.
2. Map `transaction.Currency` in `MarketingInvoiceImportService` lines 58–65.
3. Add a migration.

**B — Remove unused fields (if single-currency is a firm constraint):**
Remove `Description`, `Currency`, and `RawData` from `MarketingTransaction` and document the assumption in a code comment on the class.

---
_Filed by daily arch-review routine on 2026-05-25._