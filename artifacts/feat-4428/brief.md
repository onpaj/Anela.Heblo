## Module
Logistics

## Finding
`GiftPackageManufactureService` has a private helper method `ConsumedQuantity` (line 228 of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`) that exists specifically so the stock-down calculation and the stock validation agree exactly:

```csharp
// line 224–228
/// Quantity of one ingredient consumed by a manufacture run. Validation and the
/// stock-down operation must agree exactly, so both go through here.
private static int ConsumedQuantity(GiftPackageIngredientDto ingredient, int quantity) =>
    (int)(ingredient.RequiredQuantity * quantity);
```

`CreateManufactureAsync` calls this method for both validation (`EnsureIngredientsAreInStock`) and the actual stock-down loop. However, `DisassembleGiftPackageAsync` at line 327 duplicates the same formula inline:

```csharp
var returnedQuantity = (int)(ingredient.RequiredQuantity * quantity);  // line 327 — not using ConsumedQuantity
```

## Why it matters
If the quantity rounding logic ever changes (e.g., to support fractional amounts or different rounding semantics), the manufacture path is protected by the shared helper but the disassembly path silently diverges. The helper comment explicitly calls out this exact risk.

## Suggested fix
Replace the inline formula on line 327 with `ConsumedQuantity(ingredient, quantity)`. One-line change, no behaviour change.

---
_Filed by daily arch-review routine on 2026-10-05._
