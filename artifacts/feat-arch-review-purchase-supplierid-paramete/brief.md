## Module
Purchase

## Finding
Both repository implementations of `IPurchaseOrderRepository.GetPaginatedAsync` accept a `supplierId` parameter but silently discard it — the body of the `if` block is empty:

```csharp
// backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs:50-54
if (supplierId.HasValue)
{
    // Note: SupplierId filtering is disabled as we now use SupplierName
    // In future, implement supplier name filtering if needed
}

// backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderRepository.cs:72-75
if (supplierId.HasValue)
{
    // Note: SupplierId filtering is disabled as we now use SupplierName
    // In future, implement supplier name filtering if needed
}
```

The same dead block appears in both the real EF repository and the in-memory one. Any caller passing a `supplierId` receives results silently unfiltered — the method's public contract lies about its behaviour.

## Why it matters
- The parameter is part of the repository interface signature and is passed through from the handler; callers reasonably expect it to work.
- The YAGNI comment ("In future…") has been in the code since the parameter was added — this is speculative dead code, not a planned transition.
- Silent no-ops at the data layer are the hardest category of bugs to diagnose.

## Suggested fix
Pick one:
- **If the filter is not needed**: Remove `supplierId` from the interface signature and all three callsites (`IPurchaseOrderRepository`, both implementations, `GetPurchaseOrdersHandler`). No dead parameter, no dead block.
- **If supplier-name filtering is genuinely wanted**: Replace the empty block with an actual `WHERE` clause on `SupplierName` (EF LIKE or ILike for PostgreSQL) in the real repository, and add the equivalent string comparison to the in-memory one.

Do not leave the parameter in place with an empty body.

---
_Filed by daily arch-review routine on 2026-05-22._