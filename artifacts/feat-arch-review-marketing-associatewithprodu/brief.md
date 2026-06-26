## Module
Marketing

## Finding
`MarketingAction.AssociateWithProduct` checks for a duplicate using the raw (un-normalized) input, but stores the normalized value:

`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:76–84`
```csharp
if (ProductAssociations.Any(pa => pa.ProductCodePrefix == productCode))  // raw, case-sensitive
    return;

ProductAssociations.Add(new MarketingActionProduct
{
    ProductCodePrefix = productCode.Trim().ToUpperInvariant(),  // stored uppercase
    ...
});
```

Calling `AssociateWithProduct("abc")` when `"ABC"` is already stored passes the guard (`"ABC" != "abc"`) and attempts to insert a second row with `ProductCodePrefix = "ABC"`. The DB composite key then raises an exception at save time instead of the domain method silently deduplicating.

## Why it matters
The domain entity's guard is the intended defence; the DB constraint is a safety net. Relying on the constraint to catch what the entity method should handle means an unhandled DB exception surfaces instead of a clean no-op — affecting `UpdateMarketingActionHandler` and `ImportFromOutlookHandler` whenever product codes are passed with mixed casing.

## Suggested fix
Normalize before the dedup check:

```csharp
public void AssociateWithProduct(string productCode)
{
    if (string.IsNullOrWhiteSpace(productCode))
        throw new ArgumentException("Product code cannot be empty", nameof(productCode));

    var normalized = productCode.Trim().ToUpperInvariant();

    if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
        return;

    ProductAssociations.Add(new MarketingActionProduct
    {
        MarketingActionId = Id,
        ProductCodePrefix = normalized,
        CreatedAt = DateTime.UtcNow,
    });
}
```

---
_Filed by daily arch-review routine on 2026-05-17._