## Module
Purchase

## Finding
`UpdatePurchaseOrderHandler.MapToResponseAsync` fetches a catalog item for every order line, but the fetched object is never used — the DTO maps `line.MaterialName` (already stored on the entity):

```csharp
// backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:121-157
private async Task<UpdatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId, CancellationToken cancellationToken)
{
    var lines = new List<PurchaseOrderLineDto>();
    foreach (var line in purchaseOrder.Lines)
    {
        // Try to get material name from catalog
        var material = await _catalogRepository.GetByIdAsync(line.MaterialId, cancellationToken);  // ← fetched
        var materialName = material?.ProductName ?? "Unknown Material";                              // ← computed

        lines.Add(new PurchaseOrderLineDto
        {
            ...
            MaterialName = line.MaterialName,   // ← entity value used, not 'materialName'
            ...
        });
    }
```

`materialName` is computed but then discarded — the DTO always reads `line.MaterialName`. For an order with N lines this makes N catalog lookups (each of which queries a large in-memory cache or external system) and throws the results away.

Compare with `GetPurchaseOrderByIdHandler`, which performs the same lookup legitimately: it maps `CatalogNote = catalogItem.Note` — a field that only lives in the catalog and cannot come from the entity.

## Why it matters
- Every `PUT /api/purchase-orders/{id}` request fires N catalog reads that produce no visible output. This is wasted compute on every update.
- `ICatalogRepository.GetByIdsAsync` was added specifically to eliminate N+1 patterns (its code comment says so). Using `GetByIdAsync` in a loop already goes against that design; computing and discarding the result compounds the waste.

## Suggested fix
Delete the two dead lines from `MapToResponseAsync`:
```csharp
// Remove:
var material = await _catalogRepository.GetByIdAsync(line.MaterialId, cancellationToken);
var materialName = material?.ProductName ?? "Unknown Material";
```
The DTO already reads `line.MaterialName`; no replacement is needed. If `CatalogNote` ever needs to appear on the update response too, use `GetByIdsAsync` once (bulk) outside the loop, as `GetPurchaseOrderByIdHandler` does.

---
_Filed by daily arch-review routine on 2026-05-22._