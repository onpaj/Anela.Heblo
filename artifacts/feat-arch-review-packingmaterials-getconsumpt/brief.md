## Module
PackingMaterials

## Finding
The following private static method is copy-pasted identically into five separate handler files:

```csharp
private static string GetConsumptionTypeText(ConsumptionType type) => type switch
{
    ConsumptionType.PerOrder => "za zakázku",
    ConsumptionType.PerProduct => "za produkt",
    ConsumptionType.PerDay => "za den",
    _ => type.ToString()
};
```

Locations:
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs:50-57`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs:50-56`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs:63-70`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs:53-59`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs:63-69`

## Why it matters
Every time a new `ConsumptionType` enum value is added, all five files must be updated. Missing one produces a silently wrong label in the API response (falls through to `type.ToString()` which returns the raw enum name instead of the Czech UI string). This already happened once — the `_ => type.ToString()` fallback exists precisely because someone forgot to extend all copies.

## Suggested fix
Extract to a single static helper within the module. The natural home is a static class alongside the module's contracts or a `PackingMaterialsTextHelper` in the `Contracts/` folder:

```csharp
// Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
internal static class PackingMaterialsTextHelper
{
    public static string ConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder   => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay     => "za den",
        _ => type.ToString()
    };
}
```

Replace all five private copies with a call to `PackingMaterialsTextHelper.ConsumptionTypeText(...)`. This is a purely mechanical, zero-risk change.

---
_Filed by daily arch-review routine on 2026-05-20._