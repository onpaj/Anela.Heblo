### task: add-boundary-tests

- [ ] Locate `ModuleBoundariesTests.cs` and add allowlists for ShoptetApi-to-Catalog and ShoptetApi-to-Logistics cross-module references
- [ ] Add two new `ModuleBoundaryRule` entries to `Rules()`
- [ ] Run the boundary tests; if unexpected violations surface, add them to the appropriate allowlist and re-run

In `ModuleBoundariesTests.cs`, after the existing allowlist declarations, add:

```csharp
// Allowlist for ShoptetApi Adapters -> Catalog.
// ShoptetApiExpeditionListSource retains ICatalogRepository injection — out of scope.
// Track as follow-up; remove when ShoptetApiExpeditionListSource is decoupled.
private static readonly HashSet<string> ShoptetApiAdaptersCatalogAllowlist =
    new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.CatalogProperties",
    };

// Allowlist for ShoptetApi Adapters -> Logistics.
// ShoptetApiExpeditionListSource retains ICarrierCoolingRepository — out of scope.
// ShippingMethodRegistry/ShippingMethod reference Carriers/DeliveryHandling by design.
private static readonly HashSet<string> ShoptetApiAdaptersLogisticsAllowlist =
    new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.ICarrierCoolingRepository",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.CarrierCoolingSetting",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.Carriers",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.DeliveryHandling",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethodRegistry -> Anela.Heblo.Domain.Features.Logistics.Carriers",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethodRegistry -> Anela.Heblo.Domain.Features.Logistics.DeliveryHandling",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethod -> Anela.Heblo.Domain.Features.Logistics.Carriers",
    };
```

Add two new rules to `Rules()`:
```csharp
new ModuleBoundaryRule(
    Name: "ShoptetApi Adapters -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.ShoptetApi",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: ShoptetApiAdaptersCatalogAllowlist,
    InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"),

new ModuleBoundaryRule(
    Name: "ShoptetApi Adapters -> Logistics",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.ShoptetApi",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Logistics",
        "Anela.Heblo.Application.Features.Logistics",
        "Anela.Heblo.Persistence.Logistics",
    },
    Allowlist: ShoptetApiAdaptersLogisticsAllowlist,
    InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"),
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ModuleBoundariesTests"
```

If the test fails with unexpected violations, add those types to the appropriate allowlist and re-run.

---

