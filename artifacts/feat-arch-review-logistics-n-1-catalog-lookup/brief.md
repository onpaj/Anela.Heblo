## Module
Logistics

## Finding
Two Logistics handlers load catalog items one-by-one inside a `foreach` loop, producing N+1 round-trips to the catalog data source:

**1. `GetTransportBoxByCodeHandler` — lines 74–86**
```csharp
// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs
foreach (var itemDto in dto.Items)
{
    var catalogItem = (await _catalogRepository.GetByIdAsync(itemDto.ProductCode, cancellationToken))!;
    itemDto.ImageUrl = catalogItem.Image;
    itemDto.OnStock = catalogItem.Stock.Eshop;
}
```
A typical transport box has 5–30 distinct products, meaning 5–30 sequential DB/cache calls per scan.

**2. `GiftPackageManufactureService.GetGiftPackageDetailAsync` — lines 155–170**
```csharp
// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs
foreach (var part in productParts)
{
    var ingredientProduct = await _catalogRepository.GetByIdAsync(part.ProductCode, cancellationToken);
    ...
}
```
Gift packages typically have 4–15 ingredients; every `GetGiftPackageDetail` call issues that many catalog reads serially.

## Why it matters
Each `GetByIdAsync` call is a separate repository dispatch (and likely a separate DB query or cache fetch). The scan endpoint (`GetTransportBoxByCode`) is latency-sensitive — it is called from barcode scanners in the warehouse — so serial N lookups multiply the handler's wall-clock time linearly with box contents.

## Suggested fix
Replace the serial loop with a single batched lookup. Add a `GetByIdsAsync(IEnumerable<string> codes)` method to whichever catalog interface Logistics uses (or to the Logistics-owned interface after #1960 is addressed), and call it once per request:

```csharp
var codes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
var catalogItems = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
var byCode = catalogItems.ToDictionary(c => c.ProductCode);

foreach (var itemDto in dto.Items)
{
    if (byCode.TryGetValue(itemDto.ProductCode, out var cat))
    {
        itemDto.ImageUrl = cat.Image;
        itemDto.OnStock = cat.Stock.Eshop;
    }
}
```

Apply the same pattern in `GetGiftPackageDetailAsync`.

---
_Filed by daily arch-review routine on 2026-05-28._