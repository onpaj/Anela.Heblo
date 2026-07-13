## Module
Invoices

## Finding
`InvoiceImportService.ExecuteImportInvoice` (`backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`) calls `SaveChangesAsync` twice for every newly-imported invoice:

1. **Save #1 — inside `GetOrCreateAsync` (line 134)**: When no existing record is found, the factory is called, `AddAsync` is called, and `SaveChangesAsync` is immediately called before returning the entity.
2. **Save #2 — end of `ExecuteImportInvoice` (line 116)**: After the ERP sync, `UpdateAsync` + `SaveChangesAsync` is called unconditionally.

For existing invoices (re-import), only Save #2 occurs (correct). For new invoices, both saves fire: the first records the invoice without sync data, the second records it with sync data. In between the two saves, `_mapper.Map(invoiceDetail, invoice)` on line 91 also redundantly re-maps the same `invoiceDetail` that the factory already mapped in step 1.

The import processes batches that can contain many new invoices; every new invoice in a batch therefore costs two round-trips to the database.

## Why it matters
- Extra latency during batch imports (each new invoice = 2 saves instead of 1).
- The intermediate Save #1 writes an entity without sync state — if the process crashes between Save #1 and Save #2, the invoice record is in a half-initialised state (no `SyncHistory`, `IsSynced = false`, no `LastSyncTime`) even though it was just created from a fresh source record.

## Suggested fix
Remove `SaveChangesAsync` from inside `GetOrCreateAsync`; only `AddAsync` to register the entity with the context. Let the single `SaveChangesAsync` at line 116 flush everything atomically:

```csharp
private async Task GetOrCreateAsync(string key, Func factory, CancellationToken ct)
{
    var found = await _repository.GetByIdAsync(key, ct);
    if (found == null)
    {
        found = factory();
        await _repository.AddAsync(found, ct);
        // No SaveChangesAsync here — caller flushes once after sync.
    }
    return found;
}
```

The caller already calls `UpdateAsync` + `SaveChangesAsync` after the ERP sync, which covers both new and existing paths.

---
_Filed by daily arch-review routine on 2026-07-08._
