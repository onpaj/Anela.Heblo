## Module
MarketingInvoices

## Finding
`MarketingInvoiceImportService.ImportAsync` calls `SaveChangesAsync` after every individual `AddAsync` inside the transaction loop:

```csharp
// backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs, lines 56–57
await _repository.AddAsync(entity, ct);
await _repository.SaveChangesAsync(ct);   // ← one round-trip to DB per transaction
```

For a 7-day lookback import, this issues N separate `INSERT + COMMIT` round-trips instead of one. The `AddAsync` accumulates the entity into EF change tracking; a single `SaveChangesAsync` after the loop is sufficient and correct.

## Why it matters
- **Performance:** Each call to `SaveChangesAsync` is a separate database round-trip. With dozens or hundreds of transactions over a 7-day window, this is unnecessary I/O at every job execution (twice daily).
- **Atomicity mismatch:** The current loop saves each record individually. If the process is interrupted mid-batch, the DB is left in a partially-imported state with no way to distinguish which records came from the current run. Batching the save makes all-or-nothing reasoning easier and better matches the intent of the domain operation.
- **The `IImportedMarketingTransactionRepository` interface exposes `SaveChangesAsync`** — calling it once per item is not what that method is designed for; it is a unit-of-work flush operation.

## Suggested fix
Move `SaveChangesAsync` outside the loop, and update the `result.Imported` count to reflect what was tracked before the final save:

```csharp
foreach (var transaction in transactions)
{
    try
    {
        var exists = await _repository.ExistsAsync(_source.Platform, transaction.TransactionId, ct);
        if (exists) { result.Skipped++; continue; }

        await _repository.AddAsync(new ImportedMarketingTransaction { ... }, ct);
        result.Imported++;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, ...);
        result.Failed++;
    }
}

if (result.Imported > 0)
    await _repository.SaveChangesAsync(ct);
```

If per-record error isolation is required, the save can still happen per-record, but that should be a deliberate choice noted in the code, not an accidental pattern.

---
_Filed by daily arch-review routine on 2026-05-18._