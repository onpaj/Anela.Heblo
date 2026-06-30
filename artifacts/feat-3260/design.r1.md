# Design: Decouple ShoptetApiPackingOrderClient from Catalog and CarrierCooling Repositories

## Component Design

### New consumer-owned contracts (ShoptetOrders)

Two narrow interfaces and their companion DTOs are added to a new
`ShoptetOrders/Contracts/` subfolder. The subfolder does not currently exist;
it must be created.

**`IPackingProductSource`**
Namespace: `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`

```csharp
public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes,
        CancellationToken ct = default);
}
```

**`PackingProductInfo`** — class (not record)
```csharp
public class PackingProductInfo
{
    public Cooling Cooling { get; init; }       // Anela.Heblo.Domain.Shared.Cooling
    public int? WeightGrams { get; init; }      // null when both GrossWeight and NetWeight absent
    public string? ImageUrl { get; init; }
}
```

**`IPackingCarrierCoolingSource`**
Namespace: `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`

```csharp
public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(
        CancellationToken ct = default);
}
```

**`PackingCarrierCoolingSetting`** — class (not record)

The DTO uses string fields for carrier and handling names to avoid any compile-time
dependency on `Anela.Heblo.Domain.Features.Logistics` enum types. The adapter maps
`Carriers.ToString()` and `DeliveryHandling.ToString()` to these fields. The client
rebuilds its lookup dictionary keyed by `(string, string)` and calls a new
`ResolveCarrierCooling` overload that accepts that key type.

```csharp
public class PackingCarrierCoolingSetting
{
    public string CarrierName { get; init; } = string.Empty;          // e.g. "Zasilkovna"
    public string DeliveryHandlingName { get; init; } = string.Empty; // e.g. "NaRuky"
    public Cooling Cooling { get; init; }
}
```

`Cooling` from `Anela.Heblo.Domain.Shared` is permitted in both contract files because
it is a shared kernel type, not a Logistics-module-owned type.

---

### New provider-side adapter: Catalog

**`CatalogPackingProductSourceAdapter`**
Path: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs`
Namespace: `Anela.Heblo.Application.Features.Catalog.Infrastructure`

```
internal sealed class CatalogPackingProductSourceAdapter : IPackingProductSource
  ctor: ICatalogRepository repository
  GetByCodesAsync → _repository.GetByIdsAsync(productCodes, ct)
                 → map each CatalogAggregate to PackingProductInfo
```

Weight fallback logic (currently inline in `ShoptetApiPackingOrderClient`) moves here:
- `WeightGrams = aggregate.GrossWeight.HasValue ? (int)aggregate.GrossWeight.Value`
- `             : aggregate.NetWeight.HasValue   ? (int)aggregate.NetWeight.Value`
- `             : (int?)null`

The adapter must not call `GetAllAsync`. It must not contain any ShoptetOrders business
logic (default weight, log warnings, etc.). Registration: `AddTransient` in `CatalogModule`.

---

### New provider-side adapter: CarrierCooling

**`CarrierCoolingPackingCarrierCoolingAdapter`**
Path: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs`
Namespace: `Anela.Heblo.Application.Features.CarrierCooling.Infrastructure`

The `CarrierCooling/Infrastructure/` directory does not currently exist and must be created.

```
internal sealed class CarrierCoolingPackingCarrierCoolingAdapter : IPackingCarrierCoolingSource
  ctor: ICarrierCoolingRepository repository
  GetAllAsync → _repository.GetAllAsync(ct)
             → map each CarrierCoolingSetting to PackingCarrierCoolingSetting:
               CarrierName          = setting.Carrier.ToString()
               DeliveryHandlingName = setting.DeliveryHandling.ToString()
               Cooling              = setting.Cooling
```

Registration: `AddTransient` in `CarrierCoolingModule` (not `LogisticsModule` — the
adapter's sole dependency `ICarrierCoolingRepository` is registered there). This
corrects the placement stated in the spec.

---

### Modified: `ShoptetApiExpeditionListSource` — new `ResolveCarrierCooling` overload

The existing `internal static Cooling ResolveCarrierCooling(string, IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling>)`
overload must be kept unchanged (used by `ShoptetApiExpeditionListSource` itself).

A new `internal static` overload is added alongside it:

```csharp
internal static Cooling ResolveCarrierCooling(
    string shippingGuid,
    IReadOnlyDictionary<(string CarrierName, string DeliveryHandlingName), Cooling> matrix)
{
    if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
        return Cooling.None;

    var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
    if (!handling.HasValue)
        return Cooling.None;

    return matrix.TryGetValue((method.Carrier.ToString(), handling.Value.ToString()), out var cooling)
        ? cooling
        : Cooling.None;
}
```

This keeps the static helper pattern intact and avoids requiring `ShoptetApiPackingOrderClient`
to know about `Carriers` or `DeliveryHandling` enum types.

---

### Modified: `ShoptetApiPackingOrderClient`

Path: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`

Injections to remove:
- `ICatalogRepository _catalog`
- `ICarrierCoolingRepository _carrierCooling`

Injections to add:
- `IPackingProductSource _productSource`
- `IPackingCarrierCoolingSource _carrierCoolingSource`

`using` directives to remove:
- `using Anela.Heblo.Domain.Features.Catalog;`
- `using Anela.Heblo.Domain.Features.Logistics;`

`using` directive to add:
- `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;`

Data flow in `GetPackingOrderAsync` after the change:

1. Carrier cooling (lines 73–76 of current file):
   ```csharp
   var settings = await _carrierCoolingSource.GetAllAsync(ct);
   var matrix = settings.ToDictionary(
       s => (s.CarrierName, s.DeliveryHandlingName), s => s.Cooling);
   order.CarrierCooling = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
       detail.Shipping?.Guid ?? string.Empty, matrix);
   ```

2. Catalog lookup (lines 79–112 of current file):
   ```csharp
   var productCodes = order.Items.Select(i => i.ProductCode).Distinct().ToList();
   var catalogItems = await _productSource.GetByCodesAsync(productCodes, ct);

   var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Cooling);
   ShoptetApiExpeditionListSource.ApplyEnrichment(
       order.Items,
       new Dictionary<string, decimal>(),
       new Dictionary<string, string>(),
       coolingByCode);

   var items = order.Items.Select(i =>
   {
       catalogItems.TryGetValue(i.ProductCode, out var info);
       var w = info?.WeightGrams;
       if (w is null)
           _logger.LogWarning(...);
       return new PackingOrderItem
       {
           ...,
           ImageUrl = info?.ImageUrl,
           WeightGrams = w ?? _defaultItemWeightGrams,
       };
   }).ToList();
   ```

The `_defaultItemWeightGrams` fallback and `_logger.LogWarning` stay in this class.

---

### Modified: `CatalogModule`

Add one registration with an explanatory comment:

```csharp
// Cross-module contract: Catalog implements ShoptetOrders' IPackingProductSource via adapter.
// DI registration is owned by the provider (Catalog), not the consumer (ShoptetOrders).
services.AddTransient<IPackingProductSource, CatalogPackingProductSourceAdapter>();
```

---

### Modified: `CarrierCoolingModule`

Add one registration with an explanatory comment:

```csharp
// Cross-module contract: CarrierCooling implements ShoptetOrders' IPackingCarrierCoolingSource via adapter.
// DI registration is owned by the provider (CarrierCooling), not the consumer (ShoptetOrders).
services.AddTransient<IPackingCarrierCoolingSource, CarrierCoolingPackingCarrierCoolingAdapter>();
```

---

### Modified: `ModuleBoundariesTests`

Two new allowlists (declared near the existing ones) and two new `ModuleBoundaryRule`
entries in `Rules()`, targeting `Anela.Heblo.Adapters.ShoptetApi` (not the Application
assembly — `ShoptetApiPackingOrderClient` is in the Adapters assembly).

```csharp
// Allowlist for ShoptetApi Adapters -> Catalog.
// ShoptetApiExpeditionListSource has the same ICatalogRepository injection as
// ShoptetApiPackingOrderClient; it is out of scope for this refactoring.
// Track as a follow-up and remove when ShoptetApiExpeditionListSource is decoupled.
private static readonly HashSet<string> ShoptetApiAdaptersCatalogAllowlist =
    new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository",
        // compiler-generated state machines for ShoptetApiExpeditionListSource are covered
        // by the declaring-type check against the entry above.
    };

// Allowlist for ShoptetApi Adapters -> Logistics.
// ShoptetApiExpeditionListSource retains ICarrierCoolingRepository; out of scope.
// ShippingMethodRegistry and ShippingMethod reference Carriers/DeliveryHandling enum types
// from Domain.Features.Logistics by design — these are the Adapters-layer shipping-method
// catalog and are not candidates for removal.
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
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.PickingListBatchProcessor -> Anela.Heblo.Domain.Features.Logistics.Carriers",
        // PickingListBatchProcessor is the file previously responsible for bulk order expansion.
        // Exact set of references must be verified at implementation time and entries adjusted.
    };
```

Rules to add to `Rules()`:

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

**Important precondition:** `Anela.Heblo.Tests.csproj` must reference `Anela.Heblo.Adapters.ShoptetApi`
for `Assembly.Load("Anela.Heblo.Adapters.ShoptetApi")` to succeed at test runtime. Verify and
add the project reference if absent before writing the test code.

The `ShoptetApiAdaptersLogisticsAllowlist` entries above are indicative. The exact set of
compiler-generated types produced by `ShoptetApiExpeditionListSource`'s async methods
will surface when the test first runs. The declaring-type check in `EnumerateReferencedTypes`
covers compiler-generated nested types (state machines, display classes), so only the
declaring class entries need to be in the allowlist.

---

## Data Schemas

### New Application-layer types

All four types live in `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.
None are exposed via any HTTP API endpoint or controller DTO.

| Type | Kind | Properties |
|---|---|---|
| `IPackingProductSource` | interface | `GetByCodesAsync(IEnumerable<string>, CancellationToken) → Task<IReadOnlyDictionary<string, PackingProductInfo>>` |
| `PackingProductInfo` | class | `Cooling Cooling`, `int? WeightGrams`, `string? ImageUrl` |
| `IPackingCarrierCoolingSource` | interface | `GetAllAsync(CancellationToken) → Task<IReadOnlyList<PackingCarrierCoolingSetting>>` |
| `PackingCarrierCoolingSetting` | class | `string CarrierName`, `string DeliveryHandlingName`, `Cooling Cooling` |

### Mapping: `CatalogAggregate` → `PackingProductInfo`

| Source | Target | Rule |
|---|---|---|
| `aggregate.Properties.Cooling` | `Cooling` | direct |
| `aggregate.GrossWeight` | `WeightGrams` | `(int)value` if `HasValue`, else fall through |
| `aggregate.NetWeight` | `WeightGrams` | `(int)value` if `HasValue`, else `null` |
| `aggregate.Image` | `ImageUrl` | direct (`null` when absent) |

### Mapping: `CarrierCoolingSetting` → `PackingCarrierCoolingSetting`

| Source | Target | Rule |
|---|---|---|
| `setting.Carrier.ToString()` | `CarrierName` | enum name as string (e.g. `"Zasilkovna"`) |
| `setting.DeliveryHandling.ToString()` | `DeliveryHandlingName` | enum name as string (e.g. `"NaRuky"`) |
| `setting.Cooling` | `Cooling` | direct |

String enum names are derived from member names of `Carriers` and `DeliveryHandling`.
The new `ResolveCarrierCooling` overload in `ShoptetApiExpeditionListSource` maps back
from `ShippingMethod.Carrier.ToString()` / `ResolveDeliveryHandling(method).Value.ToString()`
to the same string keys, so no external GUID or integer identity is needed.

### No database schema changes

No new tables, columns, migrations, or persistence types are introduced. The two new adapters
read exclusively from existing repositories (`ICatalogRepository.GetByIdsAsync`,
`ICarrierCoolingRepository.GetAllAsync`) with no writes.

### File inventory

New files:
```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs
backend/src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs
```

Modified files:
```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```
