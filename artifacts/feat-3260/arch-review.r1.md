# Architecture Review: Decouple ShoptetApiPackingOrderClient from Catalog and Logistics Domain Repositories

## Skip Design: false

## Architectural Fit Assessment

`ShoptetApiPackingOrderClient` (in `Anela.Heblo.Adapters.ShoptetApi`) currently injects `ICatalogRepository` and `ICarrierCoolingRepository` — both domain-layer types owned by foreign modules — and reads `CatalogAggregate` properties directly. This is a textbook cross-module boundary violation under the project's documented pattern: consumer modules must communicate with provider modules exclusively through consumer-owned contracts implemented by provider-side adapters.

The violation is particularly sharp here because `ShoptetApiPackingOrderClient` sits in the Adapters assembly (outside the Application layer), yet it reaches through two Application module boundaries to pull domain repositories. The fix is fully aligned with the existing pattern: `ILeafletKnowledgeSource`, `ILogisticsCatalogSource`, `IManufactureCatalogSource`, and others follow exactly the same consumer-owns-contract / provider-owns-adapter model.

`ShoptetApiExpeditionListSource` (the sibling adapter for picking lists) has the same `ICatalogRepository` + `ICarrierCoolingRepository` injections and the same violation profile. The spec scopes this task to `ShoptetApiPackingOrderClient` only. That is correct — `ShoptetApiExpeditionListSource` should be tracked as a follow-up so this change stays surgical.

**Key structural fact:** `ICarrierCoolingRepository` is registered by `CarrierCoolingModule`, not by `LogisticsModule`. The `Logistics` namespace is the home for the `Carriers`, `DeliveryHandling`, and `Cooling` domain enums, but the repository's DI registration lives in `CarrierCoolingModule`. The new `IPackingCarrierCoolingSource` contract and its adapter are scoped to the ShoptetOrders → Logistics dependency edge, but the adapter will delegate to `ICarrierCoolingRepository` (registered by `CarrierCoolingModule`) and must be registered in the module that owns `ICarrierCoolingRepository` — which is `CarrierCoolingModule`, not `LogisticsModule`. The spec says "LogisticsModule"; this must be corrected (see Specification Amendments).

The `ModuleBoundariesTests` already covers the Application assembly. `ShoptetApiPackingOrderClient` is in the Adapters assembly so the existing test does not catch it. A new test checking the Adapters assembly boundary is required.

## Proposed Architecture

### Component Overview

```
ShoptetOrders/
  Contracts/
    IPackingProductSource.cs          ← new, consumer-owned (FR-1)
    IPackingCarrierCoolingSource.cs   ← new, consumer-owned (FR-4)
    PackingProductInfo.cs             ← new, ShoptetOrders-owned DTO (FR-1)
    PackingCarrierCoolingSetting.cs   ← new, ShoptetOrders-owned DTO (FR-4)

Catalog/
  Infrastructure/
    CatalogPackingProductSourceAdapter.cs   ← new, internal sealed (FR-2)

CatalogModule.cs                            ← one new registration line (FR-3)

CarrierCooling/ (not Logistics/)
  Infrastructure/
    CarrierCoolingPackingCarrierCoolingAdapter.cs  ← new, internal sealed (FR-5 corrected)

CarrierCoolingModule.cs                     ← one new registration line (FR-6 corrected)

Adapters/
  ShoptetApiPackingOrderClient.cs           ← replace ICatalogRepository + ICarrierCoolingRepository
                                               with IPackingProductSource + IPackingCarrierCoolingSource (FR-7)

Tests/
  Architecture/ModuleBoundariesTests.cs     ← new rule: ShoptetOrders(Adapters) → Catalog + Logistics (FR-8)
  Features/Catalog/Infrastructure/
    CatalogPackingProductSourceAdapterTests.cs     ← new (FR-8)
  Features/CarrierCooling/Infrastructure/
    CarrierCoolingPackingCarrierCoolingAdapterTests.cs  ← new (FR-8)
```

### Key Design Decisions

#### Decision 1: Where to place `IPackingProductSource` and `IPackingCarrierCoolingSource`

**Options considered:**
- Place both contracts in `ShoptetOrders/Contracts/` (per spec)
- Place them directly in the `ShoptetOrders/` root alongside `IPackingOrderClient`

**Chosen approach:** `ShoptetOrders/Contracts/` subfolder.

**Rationale:** All existing consumer-owned contracts in this codebase live in a `Contracts/` subfolder (`Leaflet/Contracts/`, `Logistics/Contracts/`, etc.). The ShoptetOrders module currently has no `Contracts/` folder because all its existing interfaces (`IPackingOrderClient`, `IEshopOrderClient`) are root-level consumer-facing contracts. The two new interfaces represent a different concern — they are inbound data sources consumed by the adapter — and placing them in `Contracts/` makes the boundary explicit and consistent with the established pattern.

#### Decision 2: Flat-list vs. keyed lookup for `IPackingCarrierCoolingSource`

**Options considered:**
- `GetByCriteriaAsync(string carrierGuid)` — single-lookup interface
- `GetAllAsync()` returning `IReadOnlyList<PackingCarrierCoolingSetting>` — flat list

**Chosen approach:** Flat list returning `IReadOnlyList<PackingCarrierCoolingSetting>`.

**Rationale:** The existing usage pattern in `ShoptetApiPackingOrderClient` loads all settings once and builds a dictionary. A flat-list contract maps directly to that pattern, avoids requiring the caller to know lookup keys, and keeps the adapter trivial (single repository call, map each item). This also mirrors how `ICarrierCoolingRepository.GetAllAsync` already works, making the adapter a thin translation layer.

#### Decision 3: `string CarrierGuid` vs. `Carriers` enum in `PackingCarrierCoolingSetting`

**Options considered:**
- Use the `Carriers` enum from `Anela.Heblo.Domain.Features.Logistics` — compact, type-safe
- Use `string CarrierGuid` — decouples from the Logistics domain enum type

**Chosen approach:** `string CarrierGuid` (per spec decision already made).

**Rationale:** Using the `Carriers` enum would force ShoptetOrders to take a compile-time dependency on `Anela.Heblo.Domain.Features.Logistics`, which is exactly the cross-module type coupling this change is designed to eliminate. The adapter (provider side) is the correct place to contain the mapping from `Carriers` enum to GUID string. Note that `ShoptetApiExpeditionListSource.ResolveCarrierCooling` already uses `ShippingMethodRegistry.ByGuid` to convert a Shoptet shipping GUID to a `(Carriers, DeliveryHandling)` tuple — the adapter inverts this: it converts the domain `Carriers` enum back to the GUID that ShoptetApiPackingOrderClient can use for its dictionary lookup. The implementation must choose a stable GUID representation. The `ShippingMethodRegistry` in the Adapters project already provides `ByGuid` (a dict keyed by GUID). The adapter should produce GUIDs by inverting this registry or by reading the GUID from the `CarrierCoolingSetting.Carrier` using `ShippingMethodRegistry`.

**Correction needed:** The spec says `string CarrierGuid` on `PackingCarrierCoolingSetting`, but the current `ShoptetApiPackingOrderClient.GetPackingOrderAsync` builds the matrix as `(Carrier, DeliveryHandling) → Cooling` keyed by the domain enums, not by GUIDs. After the refactor the client will receive `IReadOnlyList<PackingCarrierCoolingSetting>` with string GUIDs and must build its matrix differently. The cleanest approach: keep `PackingCarrierCoolingSetting` with `string CarrierGuid`, `string DeliveryHandlingKey` (or just let the caller use the GUID directly for the key, since `ResolveCarrierCooling` already resolves by GUID). See Data Flow section.

#### Decision 4: Where to register the `LogisticsPackingCarrierCoolingAdapter`

**Options considered:**
- Register in `LogisticsModule` (as the spec says)
- Register in `CarrierCoolingModule` (where `ICarrierCoolingRepository` is registered)

**Chosen approach:** Register in `CarrierCoolingModule`.

**Rationale:** The adapter's sole dependency is `ICarrierCoolingRepository`, which is registered by `CarrierCoolingModule`. Registering the adapter in `LogisticsModule` would make `LogisticsModule` responsible for wiring a service whose dependency (`ICarrierCoolingRepository`) is registered elsewhere, introducing a fragile ordering dependency. Provider module owns the registration: here the provider is `CarrierCoolingModule`. See Specification Amendments.

#### Decision 5: Adapter class naming

**Options considered:**
- `LogisticsPackingCarrierCoolingAdapter` (spec name)
- `CarrierCoolingPackingCarrierCoolingAdapter` (reflects actual owning module)

**Chosen approach:** `CarrierCoolingPackingCarrierCoolingAdapter` in namespace `Anela.Heblo.Application.Features.CarrierCooling.Infrastructure`.

**Rationale:** The adapter's name should reflect where it lives, not what the consumer assumed. This follows `CatalogManufactureCatalogSourceAdapter` (prefix is the provider module name). The file path is `Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs`.

#### Decision 6: Adapter scope (Transient vs. Scoped)

**Options considered:**
- `AddTransient` (stateless, short-lived)
- `AddScoped` (per-request lifetime)

**Chosen approach:** `AddTransient` for both adapters.

**Rationale:** Both adapters are stateless pass-throughs. `CatalogManufactureCatalogSourceAdapter` and `LogisticsCatalogSourceAdapter` are both registered `AddTransient`. Consistency and the stateless nature of these adapters drive the choice.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/
  IPackingProductSource.cs
  IPackingCarrierCoolingSource.cs
  PackingProductInfo.cs
  PackingCarrierCoolingSetting.cs

backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/
  CatalogPackingProductSourceAdapter.cs

backend/src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/
  CarrierCoolingPackingCarrierCoolingAdapter.cs

backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/
  ShoptetApiPackingOrderClient.cs   (modified)

backend/test/Anela.Heblo.Tests/Architecture/
  ModuleBoundariesTests.cs          (modified — new rule added)

backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
  CatalogPackingProductSourceAdapterTests.cs   (new)

backend/test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/
  CarrierCoolingPackingCarrierCoolingAdapterTests.cs   (new)
```

### Interfaces and Contracts

**`IPackingProductSource`** — namespace `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`

```csharp
public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken = default);
}
```

**`PackingProductInfo`** — class (not record), same namespace

Properties needed by `ShoptetApiPackingOrderClient`:
- `string? ImageUrl`
- `Cooling Cooling` (from `Anela.Heblo.Domain.Shared`)
- `int? WeightGrams` — encodes the GrossWeight → NetWeight → null fallback inside the adapter

**`IPackingCarrierCoolingSource`** — namespace `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`

```csharp
public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
```

**`PackingCarrierCoolingSetting`** — class (not record), same namespace

Properties:
- `string CarrierGuid` — the Shoptet shipping GUID string (see Data Flow)
- `string? DeliveryHandlingKey` — optional string key, OR the client builds its own lookup. See Data Flow for the recommended approach.
- `Cooling Cooling` (from `Anela.Heblo.Domain.Shared`)

### Data Flow

**Carrier cooling lookup (revised):**

The existing `ShoptetApiPackingOrderClient` builds a `Dictionary<(Carriers, DeliveryHandling), Cooling>` matrix and passes it to `ShoptetApiExpeditionListSource.ResolveCarrierCooling`, which looks up `(method.Carrier, handling.Value)`.

After the refactor, the client no longer has access to `Carriers` or `DeliveryHandling` enums. Two clean options:

**Option A (recommended):** Change `PackingCarrierCoolingSetting` to carry `string ShippingGuid` and `Cooling Cooling` only. The adapter maps each `CarrierCoolingSetting` to its known shipping GUIDs by consulting `ShippingMethodRegistry.ByGuid` (already in the Adapters project). Since the adapter lives in the Application layer and `ShippingMethodRegistry` is in the Adapters layer, this cross-layer reference is not acceptable. Therefore the adapter cannot use `ShippingMethodRegistry` directly.

**Option B (recommended, simpler):** The `PackingCarrierCoolingSetting` keeps `Cooling Cooling` and a `string CarrierName` + `string DeliveryHandlingName` (string representations of the enums). The adapter maps `CarrierCoolingSetting.Carrier.ToString()` and `CarrierCoolingSetting.DeliveryHandling.ToString()`. The client side then builds its lookup dictionary keyed by `(CarrierName, DeliveryHandlingName)`. The client calls the static `ResolveCarrierCooling` with an updated overload that accepts `IReadOnlyDictionary<(string, string), Cooling>` instead of `IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling>`. The `ShippingMethod` struct already exposes `Carrier` and `ResolveDeliveryHandling` returns `DeliveryHandling?` — both can be `.ToString()`'d on the adapter side.

This string-key approach fully removes any dependency on Logistics enums from the contract and from `ShoptetApiPackingOrderClient`, while keeping `ResolveCarrierCooling` as a static helper (possibly updated or overloaded).

**Catalog product lookup:**

```
ShoptetApiPackingOrderClient.GetPackingOrderAsync
  → IPackingProductSource.GetByIdsAsync(productCodes)        ← inject via ctor
  → receives Dictionary<string, PackingProductInfo>
  → reads .ImageUrl, .Cooling, .WeightGrams per product code
```

The `CatalogPackingProductSourceAdapter` encapsulates the weight fallback:
```
WeightGrams = aggregate.GrossWeight.HasValue ? (int)aggregate.GrossWeight.Value
            : aggregate.NetWeight.HasValue   ? (int)aggregate.NetWeight.Value
            : (int?)null
```

This fallback currently lives as inline code in `ShoptetApiPackingOrderClient` and must move entirely into the adapter. The adapter is the translation boundary; the client receives a clean nullable int.

**Product cooling enrichment:**

`ApplyEnrichment` on `ShoptetApiExpeditionListSource` (a static helper used by the packing client) accepts a `Dictionary<string, Cooling>`. This call site remains unchanged — the client still builds `coolingByCode` from its `PackingProductInfo` results.

### Interfaces to remove from `ShoptetApiPackingOrderClient`

Remove:
- `using Anela.Heblo.Domain.Features.Catalog;`
- `using Anela.Heblo.Domain.Features.Logistics;`
- `ICatalogRepository _catalog`
- `ICarrierCoolingRepository _carrierCooling`

Add:
- `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;`
- `IPackingProductSource _productSource`
- `IPackingCarrierCoolingSource _carrierCoolingSource`

### Architecture Boundary Test

The existing `ModuleBoundariesTests` inspects `InspectedAssembly: "Anela.Heblo.Application"` by default. `ShoptetApiPackingOrderClient` is in `Anela.Heblo.Adapters.ShoptetApi`. The new test must target that assembly.

Add a new `[Theory]` rule (same `ModuleBoundaryRule` shape):

```csharp
new ModuleBoundaryRule(
    Name: "ShoptetApi Adapters -> Catalog (Domain)",
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
    Name: "ShoptetApi Adapters -> Logistics (Domain)",
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

The allowlists must be empty after the fix is complete (zero violations). `ShoptetApiExpeditionListSource` still violates these rules — it must be added to the allowlist with a comment tracking it as a follow-up, exactly as the project does for pre-existing violations in other modules.

**Important:** The test framework uses `Assembly.Load(rule.InspectedAssembly)`. Verify that `"Anela.Heblo.Adapters.ShoptetApi"` is the correct assembly name (check the `.csproj` `AssemblyName`) and that the test project references this assembly.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ShoptetApiExpeditionListSource` has the same violation but is not in scope. New adapter boundary tests will surface it as a violation the moment tests run. | High | Add `ShoptetApiExpeditionListSource` entries to the allowlist with a follow-up comment, mirroring the LeafletAllowlist pattern. Track fix in a separate issue. |
| The `ResolveCarrierCooling` static helper on `ShoptetApiExpeditionListSource` uses `(Carriers, DeliveryHandling)` typed keys. Changing the packing client to string keys requires either a new overload or copying the lookup logic. | Medium | Introduce a second overload of `ResolveCarrierCooling` that accepts `IReadOnlyDictionary<(string, string), Cooling>`. Both overloads can be `internal static`, keeping existing tests intact. |
| `CarrierCoolingModule` does not currently have an `Infrastructure/` subfolder. | Low | Create it. Consistent with `Catalog/Infrastructure/` and `Logistics/Infrastructure/` patterns. |
| The `ModuleBoundariesTests` loads the Adapters assembly by name. If the test project does not reference `Anela.Heblo.Adapters.ShoptetApi`, `Assembly.Load` will fail at runtime. | Medium | Add a project reference from `Anela.Heblo.Tests` to `Anela.Heblo.Adapters.ShoptetApi` (verify it is not already there). |
| Weight fallback logic (GrossWeight → NetWeight → null) moves from client to adapter. If a future developer adds a new weight source, they must look in the adapter, not the client. | Low | Document the fallback logic in an XML `<summary>` on `CatalogPackingProductSourceAdapter`. |
| `PackingCarrierCoolingSetting` uses string enum names as keys. Renaming `Carriers` or `DeliveryHandling` enum members will silently break the lookup. | Medium | Consider using `nameof(Carriers.Zasilkovna)` in the adapter rather than `carrier.ToString()`, and document that the string values are derived from enum member names. A unit test that asserts the expected string values for all known carriers mitigates drift. |

## Specification Amendments

1. **FR-5 and FR-6 (module placement):** The spec places the adapter in `Logistics/Infrastructure/` and registers it in `LogisticsModule`. This is incorrect. `ICarrierCoolingRepository` is registered by `CarrierCoolingModule` (not `LogisticsModule`). The adapter must live in `CarrierCooling/Infrastructure/` with class name `CarrierCoolingPackingCarrierCoolingAdapter` and be registered in `CarrierCoolingModule.AddCarrierCoolingModule()`. The spec name `LogisticsPackingCarrierCoolingAdapter` should be updated accordingly.

2. **FR-4 (`PackingCarrierCoolingSetting` shape):** The spec specifies `string CarrierGuid` but the current client uses `(Carriers, DeliveryHandling)` tuple keys. The DTO needs either string enum name fields (`string CarrierName`, `string DeliveryHandlingName`) so the client can build a `(string, string)` lookup, or the spec should clarify how the client resolves the GUID back to a `(Carrier, DeliveryHandling)` pair without accessing Logistics types. The string-enum-name approach (Option B above) is recommended and must be spelled out explicitly in the spec.

3. **FR-7 (`ResolveCarrierCooling` call site):** The spec says to remove `ICatalogRepository` and `ICarrierCoolingRepository` injections but does not address the `ResolveCarrierCooling` static call which currently accepts `IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling>`. After the fix the dictionary key type changes. The spec should explicitly state: introduce a new `ResolveCarrierCooling` overload accepting `IReadOnlyDictionary<(string, string), Cooling>`, or inline the lookup in `GetPackingOrderAsync` and retire the shared static.

4. **FR-8 (assembly reference for architecture test):** The spec says "add a separate adapter boundary test". It should also specify that `Anela.Heblo.Tests.csproj` must reference `Anela.Heblo.Adapters.ShoptetApi` for `Assembly.Load` to succeed, if not already present.

## Prerequisites

- Verify `Anela.Heblo.Tests.csproj` already references (directly or transitively) `Anela.Heblo.Adapters.ShoptetApi`. If not, add the project reference before writing the test.
- Confirm `CarrierCooling/Infrastructure/` directory does not already exist (it does not per the current codebase scan).
- Confirm `ShoptetOrders/Contracts/` directory does not already exist (it does not — current contracts are at the module root).
