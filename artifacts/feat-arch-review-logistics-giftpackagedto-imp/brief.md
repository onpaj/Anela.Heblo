## Module
Logistics

## Finding
`GiftPackageDto`, a Logistics contract DTO, directly imports and uses the `StockSeverity` enum that is defined inside the Purchase module's use-case response class:

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageDto.cs` — line 1: `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;`
- Line 14: `public StockSeverity Severity { get; set; }`

`StockSeverity` is defined at line 96 of `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`. `GiftPackageManufactureService` also imports and uses it (lines 343, 349, 352).

Note: the Manufacture module independently defined a parallel `ManufacturingStockSeverity` enum in its own response, which is the correct pattern.

## Why it matters
A Logistics DTO must not reference a type owned by the Purchase module. The `using` import creates a compile-time dependency between the two modules. If Purchase renames or restructures `StockSeverity`, Logistics breaks. `development_guidelines.md` requires module communication exclusively through owned contracts.

## Suggested fix
Define a `GiftPackageSeverity` enum (or reuse a name like `StockSeverity`) in the Logistics module's own contracts folder:

```
backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/GiftPackageSeverity.cs
```

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public enum GiftPackageSeverity { Optimal, Severe, Critical }
```

Update `GiftPackageDto`, `GiftPackageManufactureService`, and `CriticalGiftPackagesTile` to use the new enum. Remove the `using` import from Purchase.

---
_Filed by daily arch-review routine on 2026-05-15._