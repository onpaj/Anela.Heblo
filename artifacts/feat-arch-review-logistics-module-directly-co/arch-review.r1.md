I have the picture I need. The existing `Leaflet → KnowledgeBase` and `Purchase → Catalog` patterns (via `PurchaseMaterialCatalogAdapter`) provide ready-made templates, and `ModuleBoundariesTests` is the right enforcement mechanism. Two spec deviations stand out: the adapters folder path is wrong, and `GetTotalSold` is a method on the aggregate — the DTO surface needs deliberate handling.

# Architecture Review: Decouple Logistics from Catalog-Owned Interfaces

## Skip Design: true

## Architectural Fit Assessment

The feature aligns perfectly with two already-established patterns in this codebase:

1. **Leaflet → KnowledgeBase** (`ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter`) — the canonical example explicitly cited in `docs/architecture/development_guidelines.md` §Cross-Module Communication Example.
2. **Purchase → Catalog** (`IMaterialCatalogService` / `PurchaseMaterialCatalogAdapter` in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`) — the closest structural analog: same provider (Catalog), same adapter style, same DI registration mechanism (inside `CatalogModule.AddCatalogModule`).

The producer-side adapter and DI registration are already established practice in `CatalogModule.cs` (lines 46–48), and a reflection-based architecture test (`ModuleBoundariesTests`) is the existing mechanism for enforcing the boundary in CI — superior to the spec's grep check. No new architectural primitives are required.

Two corrections to the spec emerge from the code reality:
- The spec says adapters live in `Infrastructure/Features/Catalog/`. The actual convention is `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/` (Application-layer Infrastructure folder, not a separate Infrastructure project). All existing Catalog adapters live there.
- The Catalog module entry point is `CatalogModule.AddCatalogModule(IServiceCollection, IConfiguration)` — not a separately named file.

## Proposed Architecture

### Component Overview

```
┌───────────────────────────────────────────────────────────────────┐
│ Application/Features/Logistics  (CONSUMER — owns contracts)       │
│                                                                   │
│   Contracts/                                                      │
│     ILogisticsCatalogSource              ◄── injected             │
│     ILogisticsStockOperationService      ◄── injected             │
│     LogisticsStockOperationSource (enum)                          │
│     Models/                                                       │
│       LogisticsGiftPackageItem                                    │
│       LogisticsCatalogItem                                        │
│                                                                   │
│   UseCases/GiftPackageManufacture/Services/                       │
│     GiftPackageManufactureService                                 │
│   UseCases/ChangeTransportBoxState/                               │
│     ChangeTransportBoxStateHandler                                │
│   UseCases/GetTransportBoxByCode/                                 │
│     GetTransportBoxByCodeHandler                                  │
└───────────────────────────────────────────────────────────────────┘
                          ▲                       ▲
                          │ implements            │ implements
                          │                       │
┌───────────────────────────────────────────────────────────────────┐
│ Application/Features/Catalog  (PROVIDER — owns adapters & DI)     │
│                                                                   │
│   Infrastructure/                                                 │
│     LogisticsCatalogSourceAdapter                                 │
│       → delegates to ICatalogRepository                           │
│     LogisticsStockOperationAdapter                                │
│       → delegates to IStockUpProcessingService                    │
│       → maps LogisticsStockOperationSource → StockUpSourceType    │
│                                                                   │
│   CatalogModule.AddCatalogModule                                  │
│     services.AddScoped<ILogisticsCatalogSource, …>()              │
│     services.AddScoped<ILogisticsStockOperationService, …>()      │
└───────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Adapter folder lives in Application/Features/Catalog/Infrastructure/
**Options considered:** (a) `Application/Features/Catalog/Infrastructure/` matching the existing convention; (b) the spec-proposed `Infrastructure/Features/Catalog/` (separate top-level Infrastructure project).
**Chosen approach:** (a).
**Rationale:** `PurchaseMaterialCatalogAdapter`, `CatalogPurchasePriceRecalculationAdapter`, and `CatalogAnalyticsSourceAdapter` already live in `Application/Features/Catalog/Infrastructure/`. The Application-layer `Infrastructure/` folder is the codebase-wide adapter location. There is no separate top-level Infrastructure project for these. Deviating would split the pattern.

#### Decision 2: Push date-range filtering and `GetTotalSold` math behind the adapter
**Options considered:** (a) Project `CatalogAggregate.SalesHistory` rows into Logistics DTOs and have Logistics compute totals; (b) take `(from, to)` as adapter parameters and return a pre-computed `TotalSoldInPeriod` in the DTO.
**Chosen approach:** (b).
**Rationale:** `GetTotalSold(from, to)` is a method on `CatalogAggregate` — it is Catalog domain behavior, not raw data. Projecting raw sales rows would leak the most volatile part of the Catalog aggregate into Logistics. Pushing the totalization into the adapter keeps the Catalog domain logic on the Catalog side and gives Logistics a flat, easy-to-mock DTO. The `salesCoefficient` (1.0m default in current code) stays on the Logistics side because it is presentation-layer scaling, not Catalog domain.

#### Decision 3: Filter `ProductType.Set` inside the adapter; do not expose `ProductType` to Logistics
**Options considered:** (a) expose all catalog items and let Logistics filter; (b) name the interface method `GetGiftPackageSetsAsync` and return only Set-typed items.
**Chosen approach:** (b).
**Rationale:** `ProductType` is a Catalog-owned enum; re-declaring it on the Logistics side just to filter would duplicate domain vocabulary without adding meaning. The Logistics use case is "give me gift-package candidates," not "give me everything and let me filter." For `GetGiftPackageDetailAsync`, the adapter returns `null` when the requested code is not a Set, and Logistics interprets `null` as "not found or not a gift package" (preserving the existing `ArgumentException` semantics one level up).

#### Decision 4: Use the existing `ModuleBoundariesTests` mechanism, not a grep check
**Options considered:** (a) FR-7's documented grep, (b) add a new `Logistics → Catalog` rule to `ModuleBoundariesTests`.
**Chosen approach:** (b), supplemented by the grep as PR-time evidence.
**Rationale:** `ModuleBoundariesTests` already enforces `Logistics → Manufacture`, `Purchase → Catalog`, etc., via reflection. It catches indirect references (generic args, attribute types) that a grep on `using` statements misses, and it runs in CI. The same `LogisticsAllowlist` infrastructure is already in place. Adding a `Logistics → Catalog` rule is the natural extension.

#### Decision 5: Two methods on `ILogisticsCatalogSource`, no more
**Options considered:** (a) one general-purpose method per call site, (b) a small focused surface mirroring the three concrete needs.
**Chosen approach:** (b): `GetGiftPackageSetsAsync(DateTime from, DateTime to, CancellationToken)`, `GetGiftPackageAsync(string code, DateTime from, DateTime to, CancellationToken)`, and `GetCatalogItemAsync(string code, CancellationToken)`. The first two serve `GiftPackageManufactureService`; the third serves both `GetTransportBoxByCodeHandler` (per-item image + eshop stock) and the ingredient lookup in `GetGiftPackageDetailAsync`.
**Rationale:** Matches the "expose only what is actually consumed" rule from the development guidelines. Refuses speculative breadth.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/
├── Features/Logistics/
│   └── Contracts/
│       ├── ILogisticsCatalogSource.cs              # NEW
│       ├── ILogisticsStockOperationService.cs      # NEW
│       ├── LogisticsStockOperationSource.cs        # NEW (enum)
│       └── Models/
│           ├── LogisticsGiftPackageItem.cs         # NEW
│           └── LogisticsCatalogItem.cs             # NEW
└── Features/Catalog/
    ├── Infrastructure/
    │   ├── LogisticsCatalogSourceAdapter.cs        # NEW
    │   └── LogisticsStockOperationAdapter.cs       # NEW
    └── CatalogModule.cs                            # MODIFIED — 2 lines added
```

Test-side:
```
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs   # MODIFIED — add Logistics → Catalog rule
backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
    LogisticsCatalogSourceAdapterTests.cs                              # NEW
    LogisticsStockOperationAdapterTests.cs                             # NEW
```

The three Logistics files at:
- `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- `Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- `Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs`

are modified to swap constructor parameters only.

### Interfaces and Contracts

```csharp
// Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsCatalogSource
{
    Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code, CancellationToken cancellationToken);
}
```

```csharp
// Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

```csharp
// Application/Features/Logistics/Contracts/LogisticsStockOperationSource.cs
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public enum LogisticsStockOperationSource
{
    TransportBox = 0,
    GiftPackageManufacture = 1,
}
```

```csharp
// Application/Features/Logistics/Contracts/Models/LogisticsGiftPackageItem.cs
public sealed class LogisticsGiftPackageItem
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Image { get; init; }
    public decimal AvailableStock { get; init; }     // CatalogAggregate.Stock.Available
    public double TotalSoldInPeriod { get; init; }   // result of CatalogAggregate.GetTotalSold(from, to)
    public int StockMinSetup { get; init; }          // Properties.StockMinSetup, currently cast to int
    public int OptimalStockDaysSetup { get; init; }  // Properties.OptimalStockDaysSetup
}
```

```csharp
// Application/Features/Logistics/Contracts/Models/LogisticsCatalogItem.cs
public sealed class LogisticsCatalogItem
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Image { get; init; }
    public decimal AvailableStock { get; init; }     // Stock.Available
    public decimal EshopStock { get; init; }         // Stock.Eshop (used by GetTransportBoxByCodeHandler)
}
```

Verify exact field types against call-site casts (`(int)`, `(double)`) when implementing — DTO field types should match what the call sites need so the casts can be removed.

### Data Flow

**`GetTransportBoxByCodeHandler` enrichment loop:**
```
Handler → ILogisticsCatalogSource.GetCatalogItemAsync(productCode)
        → adapter → ICatalogRepository.GetByIdAsync → CatalogAggregate
        → project to LogisticsCatalogItem { Image, EshopStock }
        → handler maps onto TransportBoxItemDto
```

**`GiftPackageManufactureService.GetAvailableGiftPackagesAsync`:**
```
Service → ILogisticsCatalogSource.GetGiftPackageSetsAsync(from, to)
        → adapter → ICatalogRepository.GetAllAsync
                  → filter Type == ProductType.Set
                  → for each, project to LogisticsGiftPackageItem with TotalSoldInPeriod = a.GetTotalSold(from, to)
        → service applies salesCoefficient, computes severity, builds GiftPackageDto
```

**`GiftPackageManufactureService.CreateManufactureAsync` (stock-up):**
```
Service → ILogisticsStockOperationService.CreateOperationAsync(
              docNum, code, amount, LogisticsStockOperationSource.GiftPackageManufacture, logId)
        → adapter → maps enum value → StockUpSourceType.GiftPackageManufacture
                  → IStockUpProcessingService.CreateOperationAsync(...)
```

**`ChangeTransportBoxStateHandler.HandleReceived`:** same shape as above with `LogisticsStockOperationSource.TransportBox`.

**Enum mapping in adapter:** use exhaustive `switch` expression with `_ => throw new ArgumentOutOfRangeException(...)` so an unmapped value crashes loudly at the boundary — matches the convention in `PurchaseMaterialCatalogAdapter.MapProductType`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetTotalSold` semantics drift between aggregate and adapter projection | Medium | Adapter calls `aggregate.GetTotalSold(from, to)` directly; does not reimplement the math. DTO carries the result, not raw rows. |
| DTO field types (`int` vs `decimal` vs `double`) silently change cast behavior in Logistics | Medium | Match DTO field types to what call sites already cast to so the casts can be removed; verify by comparing call sites before/after. |
| `CatalogAggregate.GetAllAsync` is called twice in `CreateManufactureAsync` (once for detail, once per ingredient) — adapter projection multiplies work | Low | Existing behavior already loads `GetByIdAsync` per ingredient; new behavior preserves it via `GetCatalogItemAsync`. No new round trips. NFR-1 explicitly accepts this. |
| Enum drift: future `StockUpSourceType` values added without mirroring in `LogisticsStockOperationSource` | Low | Adapter `switch` on Logistics enum throws on unmapped — fail fast. Catalog-side addition does not affect Logistics; Logistics-side addition without adapter update fails at runtime in test. |
| `ModuleBoundariesTests` test allowlist for `Logistics → Manufacture` (existing) does not cover `Logistics → Catalog`; risk of regression | High | Add a new `Logistics → Catalog` rule to `Rules()` in `ModuleBoundariesTests` with an empty `LogisticsCatalogAllowlist`. This catches indirect references that the spec's grep misses. |
| DI lifetime mismatch (`ICatalogRepository` is `Transient`, `IStockUpProcessingService` is `Transient` — see `CatalogModule.cs:43,69`) | Low | Register the new adapters as `Transient` to match — not `Scoped` as the spec suggests. Avoids captive-dependency warnings. |
| `GetGiftPackageDetailAsync` currently throws `ArgumentException` when `Type != ProductType.Set` | Low | Adapter returns `null` for non-Set codes; Logistics raises the same `ArgumentException` based on null. Behavioral parity preserved. |
| Adapter unit tests are missing in the spec's acceptance criteria | Medium | Add Catalog-side adapter tests covering: aggregate→DTO projection field-by-field, enum mapping (every value), filter behavior (`null` for non-Set in `GetGiftPackageAsync`). |

## Specification Amendments

1. **Replace `Infrastructure/Features/Catalog/`** in spec FR-4 and §API/Interface Design with `Application/Features/Catalog/Infrastructure/` — the codebase has no separate Infrastructure project; existing Catalog adapters all live in the Application-layer Infrastructure folder.
2. **DI lifetime**: change spec's `AddScoped` to `AddTransient` in FR-5 / §DI registration. Both `ICatalogRepository` and `IStockUpProcessingService` are registered `Transient` in `CatalogModule.cs`; adapters should match.
3. **Strengthen FR-7**: in addition to the grep, **add a new `Logistics → Catalog` rule to `ModuleBoundariesTests`** (existing reflection-based enforcement at `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`). The grep stays as PR-description evidence; the test stays as CI enforcement. This catches indirect references (generic args, attribute types, return-type generics) that a grep on `using` lines misses.
4. **Add explicit interface method names** (FR-1 leaves these to "TBD"): `GetGiftPackageSetsAsync`, `GetGiftPackageAsync`, `GetCatalogItemAsync` on `ILogisticsCatalogSource`. Push `ProductType.Set` filtering and `GetTotalSold(from, to)` math into the adapter; pass `(from, to)` as parameters; do not project raw `SalesHistory`. This decouples Logistics from `ProductType` entirely and avoids leaking sales rows.
5. **Enum value confirmation**: today `StockUpSourceType` has exactly two members — `TransportBox = 0`, `GiftPackageManufacture = 1` (see `Domain/Features/Catalog/Stock/StockUpSourceType.cs`). `LogisticsStockOperationSource` should mirror these two values, with matching ordinals only as a defensive convention — the adapter still uses an explicit `switch` for mapping.
6. **Adapter visibility**: declare both adapters `internal sealed` (matches `KnowledgeBaseLeafletSourceAdapter` and `PurchaseMaterialCatalogAdapter`).
7. **Test coverage**: add to acceptance criteria adapter unit tests that verify (a) per-field DTO projection, (b) exhaustive enum mapping, (c) `null` return for `GetGiftPackageAsync` on a non-Set product code.

## Prerequisites

None. All required scaffolding exists:

- `Application/Features/Logistics/Contracts/` already exists and contains `IInventoryReservationService`, the consumer-owned contract from the prior Logistics-Manufacture decoupling — same pattern, same folder.
- `Application/Features/Catalog/Infrastructure/` already exists and houses three production adapters following the exact same pattern.
- `CatalogModule.AddCatalogModule` already registers adapter bindings; two more lines fit naturally next to lines 46–48.
- `ModuleBoundariesTests` already has the rules table; adding a `Logistics → Catalog` entry is a small edit.
- No DB migrations, no config changes, no infrastructure changes required.