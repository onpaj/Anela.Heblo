# Specification: Decouple ShoptetApiPackingOrderClient from Catalog and Logistics Domain Repositories

## Summary

`ShoptetApiPackingOrderClient` currently injects two broad domain repository interfaces owned by foreign modules (`ICatalogRepository` from Catalog, `ICarrierCoolingRepository` from Logistics) and reads Catalog entity internals directly. This violates the project's documented cross-module communication pattern. The fix introduces two narrow consumer-owned contracts in ShoptetOrders' `Contracts/` folder, has the provider modules implement adapters, and removes all foreign-module knowledge from the adapter.

## Background

The project's cross-module communication rule (documented in `docs/architecture/development_guidelines.md`, section "Cross-Module Communication Example") requires that:

1. The **consuming module** defines a narrow interface in its own `Contracts/` folder.
2. The **providing module** implements an adapter in its own `Infrastructure/` folder.
3. The **providing module** registers the DI binding in its `{Feature}Module.cs`.

The existing precedent is `ILeafletKnowledgeSource` (consumer-owned, in Leaflet) implemented by `KnowledgeBaseLeafletSourceAdapter` (provider-owned, in KnowledgeBase). The same pattern is used across Logistics, Catalog, Analytics, DataQuality, and other module pairs.

`ShoptetApiPackingOrderClient` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`) currently violates this rule in two ways:

- It injects `ICatalogRepository` (a broad Catalog-owned interface with 20+ methods, timestamps, and merge-tracking) to perform a simple bulk product lookup.
- It injects `ICarrierCoolingRepository` (a Logistics-owned interface) to read the full carrier-cooling settings matrix.
- It accesses Catalog entity internals directly: `CatalogAggregate.Properties.Cooling`, `GrossWeight`, `NetWeight`, and `Image`.

The business enrichment logic embedded in the adapter (weight fallback chain: GrossWeight → NetWeight → default; cooling lookup; image lookup) belongs at the consumer boundary where it can be named, tested, and changed independently of Catalog's internal entity shape.

## Functional Requirements

### FR-1: Define `IPackingProductSource` in ShoptetOrders Contracts

Create a narrow, ShoptetOrders-owned interface at:

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs
```

The interface must expose only what the packing screen needs: cooling classification, weight in grams (already resolved — no fallback logic in the consumer), and image URL.

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes,
        CancellationToken ct = default);
}

public class PackingProductInfo
{
    public Cooling Cooling { get; init; }
    public int? WeightGrams { get; init; }
    public string? ImageUrl { get; init; }
}
```

`Cooling` is `Anela.Heblo.Domain.Shared.Cooling` — a shared domain enum, not Catalog-owned, so referencing it from ShoptetOrders' `Contracts/` is permitted.

`PackingProductInfo` must be a **class** (not a record), per the project-wide DTO rule (CLAUDE.md: "DTOs are classes, never C# records").

**Acceptance criteria:**
- File `IPackingProductSource.cs` exists at the path above.
- `PackingProductInfo` is a `class`, not a `record`.
- The interface has exactly one method: `GetByCodesAsync`.
- No reference to `CatalogAggregate`, `ICatalogRepository`, `CatalogProperties`, or any other Catalog-owned type appears in the contract file.

### FR-2: Implement `CatalogPackingProductSourceAdapter` in the Catalog Module

Create an adapter in:

```
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs
```

The adapter injects `ICatalogRepository` (which it is permitted to use — it is in the Catalog module) and maps `CatalogAggregate` properties to `PackingProductInfo`:

- **Cooling**: `aggregate.Properties.Cooling`
- **WeightGrams**: `(int?)((int)aggregate.GrossWeight.Value)` if `GrossWeight.HasValue`, else `(int?)aggregate.NetWeight.Value` if `NetWeight.HasValue`, else `null`. This encodes the current fallback chain and makes it explicit and testable.
- **ImageUrl**: `aggregate.Image`

The adapter must be `internal sealed`.

**Acceptance criteria:**
- File exists at path above, is `internal sealed`, implements `IPackingProductSource`.
- The adapter applies the weight fallback (GrossWeight → NetWeight → null) internally, so the consumer never sees the raw `double?` fields.
- The adapter calls `ICatalogRepository.GetByIdsAsync(productCodes, ct)` — the existing bulk lookup method — and does not call `GetAllAsync`.
- No ShoptetOrders-specific business logic (e.g., what to do when weight is null, what default to use) appears in the adapter; that stays in `ShoptetApiPackingOrderClient`.

### FR-3: Register Adapter in `CatalogModule`

Add the DI binding to `CatalogModule.AddCatalogModule` in:

```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
```

```csharp
services.AddTransient<IPackingProductSource, CatalogPackingProductSourceAdapter>();
```

with a comment mirroring the existing cross-module registration comments:

```csharp
// Cross-module contract: Catalog implements ShoptetOrders' IPackingProductSource via adapter.
// DI registration is owned by the provider (Catalog), not the consumer (ShoptetOrders).
```

**Acceptance criteria:**
- `CatalogModule` references `IPackingProductSource` from `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.
- `ShoptetOrdersModule` does not register `IPackingProductSource` — the binding is provider-owned.
- The existing `services.AddTransient<ICatalogRepository, CatalogRepository>()` line is unchanged.

### FR-4: Define `IPackingCarrierCoolingSource` in ShoptetOrders Contracts

Create a second narrow interface at:

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs
```

The adapter resolves the carrier-cooling matrix and exposes a pre-computed lookup, shaped for the single call site in `ShoptetApiPackingOrderClient.GetPackingOrderAsync`:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyDictionary<(Carriers Carrier, DeliveryHandling DeliveryHandling), Cooling>> GetCoolingMatrixAsync(
        CancellationToken ct = default);
}
```

`Carriers`, `DeliveryHandling`, and `Cooling` are all in `Anela.Heblo.Domain.Shared` or `Anela.Heblo.Domain.Features.Logistics` — they are domain value types, not Logistics-application-module-owned types. Their use in the ShoptetOrders contract file must be assessed against the `ModuleBoundariesTests` rules. If `Carriers` and `DeliveryHandling` are in `Anela.Heblo.Domain.Features.Logistics`, they are Logistics-Domain-owned, which would require an allowlist entry or an alternative design (see Open Questions).

**Alternative design (preferred if Carriers/DeliveryHandling create a boundary issue):** Return a flat list of `PackingCarrierCoolingSetting` (a ShoptetOrders-owned DTO with string fields for carrier and handling), and build the dictionary in `ShoptetApiPackingOrderClient`. This avoids any Logistics-namespace import in ShoptetOrders.

```csharp
public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default);
}

public class PackingCarrierCoolingSetting
{
    public string CarrierGuid { get; init; } = string.Empty;
    public Cooling Cooling { get; init; }
}
```

The choice between these two designs is resolved in Open Questions below. The spec proceeds with the assumption that `Carriers` and `DeliveryHandling` are Logistics-Domain-owned types and that the preferred design uses a ShoptetOrders-owned `PackingCarrierCoolingSetting` DTO with a `CarrierGuid` string field (matching how `ShoptetApiExpeditionListSource.ResolveCarrierCooling` already works — it takes a `string` shipping GUID and matches against a `(Carriers, DeliveryHandling)` dictionary). See Open Questions OQ-1.

**Acceptance criteria:**
- Interface is defined in `ShoptetOrders/Contracts/`.
- No Logistics-application-namespace type appears in the contract (i.e., no `ICarrierCoolingRepository`, no `CarrierCoolingSetting` domain class).
- The `Cooling` enum from `Anela.Heblo.Domain.Shared` may be used.

### FR-5: Implement `LogisticsPackingCarrierCoolingAdapter` in the Logistics Module

Create an adapter in:

```
backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsPackingCarrierCoolingAdapter.cs
```

The adapter injects `ICarrierCoolingRepository` and maps `CarrierCoolingSetting` to the ShoptetOrders-owned DTO.

The adapter must be `internal sealed`.

**Acceptance criteria:**
- File exists at path above, is `internal sealed`, implements `IPackingCarrierCoolingSource`.
- The adapter does not contain any ShoptetOrders business logic.
- No `CarrierCoolingSetting` domain class reference leaks into the ShoptetOrders namespace.

### FR-6: Register Adapter in `LogisticsModule`

Add the DI binding to `LogisticsModule.AddTransportModule` in:

```
backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
```

```csharp
// Cross-module contract: Logistics implements ShoptetOrders' IPackingCarrierCoolingSource via adapter.
// DI registration is owned by the provider (Logistics), not the consumer (ShoptetOrders).
services.AddScoped<IPackingCarrierCoolingSource, LogisticsPackingCarrierCoolingAdapter>();
```

**Acceptance criteria:**
- `LogisticsModule` references `IPackingCarrierCoolingSource` from `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.
- `ShoptetOrdersModule` does not register this binding.

### FR-7: Update `ShoptetApiPackingOrderClient` to Use the New Contracts

Modify `ShoptetApiPackingOrderClient` to:

1. Replace `ICatalogRepository _catalog` with `IPackingProductSource _packingProductSource`.
2. Replace `ICarrierCoolingRepository _carrierCooling` with `IPackingCarrierCoolingSource _packingCarrierCoolingSource`.
3. Remove the `using` directives for `Anela.Heblo.Domain.Features.Catalog` and `Anela.Heblo.Domain.Features.Logistics`.
4. Replace the catalog lookup block (lines 79–112) with a call to `_packingProductSource.GetByCodesAsync(productCodes, ct)` and map the returned `PackingProductInfo` to the existing `PackingOrderItem` fields.
5. Replace the carrier-cooling block (lines 73–76) with a call to `_packingCarrierCoolingSource` and use the returned data to resolve `order.CarrierCooling` via the existing `ShoptetApiExpeditionListSource.ResolveCarrierCooling` helper (or inline the resolution if the returned DTO changes the signature).
6. Retain the `_defaultItemWeightGrams` fallback for items where `PackingProductInfo.WeightGrams` is `null`, and retain the `_logger.LogWarning` for that case.

**Acceptance criteria:**
- `ShoptetApiPackingOrderClient` has no field of type `ICatalogRepository` or `ICarrierCoolingRepository`.
- No `CatalogAggregate`, `CatalogProperties`, `CarrierCoolingSetting`, or any type from `Anela.Heblo.Domain.Features.Catalog` or `Anela.Heblo.Domain.Features.Logistics` namespaces is referenced in the file.
- The observable behavior of `GetPackingOrderAsync` is unchanged: same cooling values, same weight fallback logic (now inside the adapter), same image URL, same default weight on missing catalog entry.
- `dotnet build` passes with no warnings introduced.

### FR-8: Add Architecture Boundary Tests for ShoptetOrders → Catalog and ShoptetOrders → Logistics

Add two new `ModuleBoundaryRule` entries to `ModuleBoundariesTests.Rules()` in:

```
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```

```csharp
new ModuleBoundaryRule(
    Name: "ShoptetOrders -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),

new ModuleBoundaryRule(
    Name: "ShoptetOrders -> Logistics",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Logistics",
        "Anela.Heblo.Application.Features.Logistics",
        "Anela.Heblo.Persistence.Logistics",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

Note: `ShoptetApiPackingOrderClient` lives in `Anela.Heblo.Adapters.ShoptetApi`, not in `Anela.Heblo.Application`. The `ModuleBoundariesTests` reflection-based check inspects `Anela.Heblo.Application`. The adapter lives in a different assembly. Verify which assembly to inspect (see Open Questions OQ-2). If the Adapters assembly is not inspected by the existing test, a separate adapter-level boundary check may be needed, or the test may require an `InspectedAssembly` override.

**Acceptance criteria:**
- The two new rules are registered in `Rules()`.
- The test suite passes with empty allowlists (no violations).
- If OQ-2 determines a different inspection strategy is needed, the rules are adjusted accordingly — but the no-violation requirement still holds.

## Non-Functional Requirements

### NFR-1: No Behavioral Change

The refactoring is purely structural. The packing screen behavior — cooling values, weight computation, image display, eligibility check, order counts — must remain identical. No user-visible change.

### NFR-2: Build and Lint Clean

`dotnet build` and `dotnet format` must pass with no new warnings or formatting violations.

### NFR-3: No New Public API Surface

`PackingProductInfo`, `IPackingProductSource`, `PackingCarrierCoolingSetting`, and `IPackingCarrierCoolingSource` are Application-layer types. They must not be exposed through any controller response DTO or API endpoint.

### NFR-4: Adapter Performance Parity

`CatalogPackingProductSourceAdapter` must use the existing `GetByIdsAsync` bulk lookup (not `GetAllAsync` followed by filtering), preserving the current N+1-free access pattern.

## Data Model

No database schema changes. No new entities. The following existing types are used:

| Type | Owner | Role |
|---|---|---|
| `CatalogAggregate` | `Anela.Heblo.Domain.Features.Catalog` | Source of product data; accessed only within `CatalogPackingProductSourceAdapter` |
| `CarrierCoolingSetting` | `Anela.Heblo.Domain.Features.Logistics` | Source of carrier matrix; accessed only within `LogisticsPackingCarrierCoolingAdapter` |
| `Cooling` | `Anela.Heblo.Domain.Shared` | Shared domain enum; used in both contracts |
| `PackingProductInfo` | New, `ShoptetOrders.Contracts` | Projected view of catalog data for packing |
| `PackingCarrierCoolingSetting` | New, `ShoptetOrders.Contracts` | Projected view of carrier cooling for packing |

## API / Interface Design

No HTTP API changes. This is an internal refactoring of the Application and Adapters layers.

**New files:**

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs
backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsPackingCarrierCoolingAdapter.cs
```

**Modified files:**

```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```

## Dependencies

- `ICatalogRepository.GetByIdsAsync` — existing method, no change required.
- `ICarrierCoolingRepository.GetAllAsync` — existing method, no change required.
- `ShoptetApiExpeditionListSource.ResolveCarrierCooling` — existing static helper; continues to be called by `ShoptetApiPackingOrderClient` with the same arguments (shipping GUID and cooling matrix dictionary). If `IPackingCarrierCoolingSource` returns a flat list of `PackingCarrierCoolingSetting`, the dictionary reconstruction (keyed by carrier GUID) moves into `ShoptetApiPackingOrderClient.GetPackingOrderAsync`, not the adapter.
- `ModuleBoundariesTests` reflection harness — the new rules rely on the existing `EnumerateReferencedTypes` + `IsForbidden` infrastructure.

## Out of Scope

- Changing `IPackingOrderClient`, `PackingOrder`, or `PackingOrderItem` (the ShoptetOrders public contracts consumed by Packaging).
- Moving weight-fallback configuration or defaults to the Catalog adapter — those remain in `ShoptetApiPackingOrderClient` which owns the default-weight setting.
- Adding unit tests beyond the architecture boundary test (a behavioral integration test already exercises the full path; adding unit tests for the adapters is desirable but not required for this refactoring).
- Addressing the `ShoptetApiExpeditionListSource` adapter's direct reference to `ICarrierCoolingRepository` for the expedition list feature (separate concern).
- Resolving other pre-existing allowlist entries in `ModuleBoundariesTests` for unrelated module pairs.
- The `ICarrierCoolingRepository` definition itself — it stays in `Anela.Heblo.Domain.Features.Logistics` and is only hidden from ShoptetOrders by the new adapter.

## Open Questions

### OQ-1: Contract shape for carrier-cooling source

`ShoptetApiExpeditionListSource.ResolveCarrierCooling` takes a `string` shipping GUID and a `Dictionary<(Carriers, DeliveryHandling), Cooling>` matrix. The matrix key types `Carriers` and `DeliveryHandling` live in `Anela.Heblo.Domain.Features.Logistics` — a Logistics-owned namespace.

If `IPackingCarrierCoolingSource` exposes `IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling>`, the ShoptetOrders `Contracts/` file imports Logistics-Domain types, which the new `ShoptetOrders -> Logistics` boundary test would flag as a violation.

**Decision needed:** Should `IPackingCarrierCoolingSource` return a flat list of `PackingCarrierCoolingSetting` (a ShoptetOrders-owned class with a `string CarrierGuid` and `Cooling Cooling` property), leaving dictionary reconstruction and GUID resolution inside `ShoptetApiPackingOrderClient`? Or should `Carriers` and `DeliveryHandling` be added to `Domain.Shared` (promoting them to shared-kernel status) so they can be referenced freely?

Assumption: Use the flat-list approach with a ShoptetOrders-owned DTO — it avoids any import of Logistics-owned types and keeps the contract narrow.

### OQ-2: Boundary test assembly for `ShoptetApiPackingOrderClient`

`ShoptetApiPackingOrderClient` is compiled into `Anela.Heblo.Adapters.ShoptetApi`, not `Anela.Heblo.Application`. The existing `ModuleBoundariesTests` inspects types from `Anela.Heblo.Application` (and optionally `Anela.Heblo.Domain` for domain-layer rules). The new boundary rules in FR-8 inspect `Anela.Heblo.Application.Features.ShoptetOrders` — this will catch violations in `ShoptetOrders` application-layer types but will NOT catch violations in the Adapters assembly.

**Decision needed:** Should a separate `ModuleBoundaryRule` or a new `[Fact]` be added to inspect `Anela.Heblo.Adapters.ShoptetApi` for references to Catalog and Logistics domain namespaces? After the refactoring, `ShoptetApiPackingOrderClient` should have no such references — a test targeting that assembly is the only way to prevent regression.

Assumption: Add a targeted `[Fact]` (or a `ModuleBoundaryRule` with `InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"`) for `ShoptetApiPackingOrderClient`.

## Status: HAS_QUESTIONS
