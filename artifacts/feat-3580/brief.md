## Module
Purchase

## Finding
The manual mapping from `PurchaseOrderHistory` (domain entity) to `PurchaseOrderHistoryDto` (contract) is copy-pasted verbatim in three handlers:

```csharp
// CreatePurchaseOrderHandler.cs:114–121
var history = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
{
    Id = h.Id,
    Action = h.Action,
    OldValue = h.OldValue,
    NewValue = h.NewValue,
    ChangedAt = h.ChangedAt,
    ChangedBy = h.ChangedBy
}).ToList();

// GetPurchaseOrderByIdHandler.cs:71–79
History = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
{
    Id = h.Id,
    Action = h.Action,
    OldValue = h.OldValue,
    NewValue = h.NewValue,
    ChangedAt = h.ChangedAt,
    ChangedBy = h.ChangedBy
}).OrderByDescending(h => h.ChangedAt).ToList(),

// GetPurchaseOrderHistoryHandler.cs:38–47
var items = history.Select(h => new PurchaseOrderHistoryDto
{
    Id = h.Id,
    Action = h.Action,
    OldValue = h.OldValue,
    NewValue = h.NewValue,
    ChangedAt = h.ChangedAt,
    ChangedBy = h.ChangedBy,
}).ToList();
```

`PurchaseOrderLineDto` already solves the same problem cleanly with a static factory:
`backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderLineDto.cs:17` — `public static PurchaseOrderLineDto FromLine(PurchaseOrderLine line, string? catalogNote = null)`

`PurchaseOrderHistoryDto` lacks the equivalent.

## Why it matters
If `PurchaseOrderHistory` gains a new field (or any field is renamed), all three mapping sites must be updated in sync. One missed site produces silently incorrect API responses. Because the duplication is purely mechanical, it offers no information benefit — three identical blocks are harder to diff than one canonical source.

## Suggested fix
Add a static factory to `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs`:

```csharp
public static PurchaseOrderHistoryDto FromDomain(PurchaseOrderHistory h) => new()
{
    Id = h.Id,
    Action = h.Action,
    OldValue = h.OldValue,
    NewValue = h.NewValue,
    ChangedAt = h.ChangedAt,
    ChangedBy = h.ChangedBy
};
```

Then replace all three mapping sites:
- `CreatePurchaseOrderHandler.cs:114` → `.Select(PurchaseOrderHistoryDto.FromDomain)`
- `GetPurchaseOrderByIdHandler.cs:71` → `.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt)`
- `GetPurchaseOrderHistoryHandler.cs:38` → `.Select(PurchaseOrderHistoryDto.FromDomain)`

---
_Filed by daily arch-review routine on 2026-07-10._
