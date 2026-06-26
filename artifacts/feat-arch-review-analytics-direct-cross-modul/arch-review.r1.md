# Architecture Review: Remove Analytics → Catalog Direct Cross-Module Dependency

## Skip Design: true

Backend-only refactor with no UI/UX work — internal contract changes only, no HTTP API surface or DTO changes, no new screens or visual components.

## Architectural Fit Assessment

The proposed refactor aligns precisely with the canonical pattern already established in this codebase. Two existing exemplars validate the approach:

- **Leaflet → KnowledgeBase**: `ILeafletKnowledgeSource` (Leaflet-owned contract) implemented by `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned adapter), registered in `KnowledgeBaseModule`.
- **Purchase → Catalog**: `IMaterialCatalogService` (Purchase-owned contract) implemented by `PurchaseMaterialCatalogAdapter` (Catalog-owned adapter), registered in `CatalogModule` (line 45 of `CatalogModule.cs`).

The same wiring shape applies here. The reflection-based test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` is the canonical guardrail and **must be extended** with an `Analytics → Catalog` rule — the spec does not call this out explicitly, but the existing rules for Leaflet, Article, Logistics, PackingMaterials, Purchase, and ExpeditionListArchive set the precedent.

**Key gap in the spec:** The spec describes refactoring `AnalyticsRepository.cs` (impl) but does not mention `IAnalyticsRepository.cs` (interface, lines 18–22, 28–32), which itself imports `Anela.Heblo.Domain.Features.Catalog` and exposes `ProductType[] productTypes` on `StreamProductsWithSalesAsync` and `GetGroupMarginTotalsAsync`. The interface must change too — otherwise `GetMarginReportHandler` and `GetProductMarginSummaryHandler` (which call these methods with `new[] { ProductType.Product, ProductType.Goods }` at line 57 and line 37 respectively) cannot stop importing Catalog.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application.Features.Analytics                         │
│                                                                     │
│  ┌─────────────────────────────────────────┐                        │
│  │ UseCases/                               │                        │
│  │   GetMarginReportHandler                │ ← injects              │
│  │   GetProductMarginSummaryHandler        │   IAnalyticsRepository │
│  │   GetProductMarginAnalysisHandler       │                        │
│  └────────────────────┬────────────────────┘                        │
│                       │                                             │
│  ┌────────────────────▼────────────────────┐                        │
│  │ Infrastructure/                         │                        │
│  │   IAnalyticsRepository  (Analytics-owned, NO Catalog types)      │
│  │   AnalyticsRepository                   │ ← injects              │
│  └────────────────────┬────────────────────┘   IAnalyticsProductSource│
│                       │                                             │
│  ┌────────────────────▼────────────────────┐                        │
│  │ Contracts/                              │                        │
│  │   IAnalyticsProductSource  (NEW)        │                        │
│  └────────────────────▲────────────────────┘                        │
└───────────────────────┼─────────────────────────────────────────────┘
                        │  implemented by (DI)
┌───────────────────────┼─────────────────────────────────────────────┐
│  Anela.Heblo.Application.Features.Catalog                           │
│                       │                                             │
│  ┌────────────────────┴────────────────────┐                        │
│  │ Infrastructure/                         │                        │
│  │   CatalogAnalyticsSourceAdapter  (NEW)  │ ← injects              │
│  └────────────────────┬────────────────────┘   ICatalogRepository   │
│                       │                                             │
│  ┌────────────────────▼────────────────────┐                        │
│  │ CatalogModule.cs                        │                        │
│  │   AddScoped<IAnalyticsProductSource,    │                        │
│  │             CatalogAnalyticsSourceAdapter>()                     │
│  └─────────────────────────────────────────┘                        │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Domain.Features.Analytics                              │
│    AnalyticsProduct  (Type retyped: AnalyticsProductType)           │
│    AnalyticsProductType  (NEW enum, mirrors used Catalog values)    │
│    MarginCalculator, SalesDataPoint, DateRange, ...                 │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Interface lives in `Contracts/`, not `Infrastructure/`

**Options considered:**
- (A) Place `IAnalyticsProductSource` in `Application/Features/Analytics/Contracts/` (consumer-owned contract folder, per spec FR-1).
- (B) Place it in `Application/Features/Analytics/Infrastructure/` next to `IAnalyticsRepository`.

**Chosen approach:** (A) — `Contracts/`.

**Rationale:** Both exemplars (`ILeafletKnowledgeSource`, `IMaterialCatalogService`) live in their consumer module's `Contracts/`. `Infrastructure/` is for the consumer's own data-access concerns (e.g., `IAnalyticsRepository`/`AnalyticsRepository` accessing `ApplicationDbContext`). A cross-module contract that the consumer owns and the provider implements belongs in `Contracts/`.

#### Decision 2: `IAnalyticsRepository` must also be detoxified

**Options considered:**
- (A) Change only `AnalyticsRepository` impl; leave the interface signature with `ProductType[]`.
- (B) Change the interface to take `AnalyticsProductType[]` and update all callers.

**Chosen approach:** (B).

**Rationale:** If the interface still exposes `Catalog.ProductType`, the handlers must still import Catalog to call it — defeating FR-6's acceptance criterion ("Neither file imports from `Anela.Heblo.Domain.Features.Catalog`"). The architectural fence (NFR-2's grep test) will fail at the handler level. This is an addition to the spec, not a contradiction.

#### Decision 3: `CatalogAnalyticsSourceAdapter` lifetime

**Options considered:**
- (A) `AddScoped` (per spec FR-4).
- (B) `AddTransient` (matches `ICatalogRepository` registration at `CatalogModule.cs:41`).

**Chosen approach:** (B) — `AddTransient`.

**Rationale:** The adapter is stateless and merely delegates. Its dependency (`ICatalogRepository`) is registered Transient. Using Scoped here introduces a lifetime mismatch (Transient inside Scoped is fine, but the inverse can produce captive-dependency warnings if anything ever changes). Match the dependency's lifetime; the spec's "lifetime to match existing Catalog repository registrations" wording supports this — `ICatalogRepository` is Transient, not Scoped.

#### Decision 4: Add `Analytics → Catalog` rule to `ModuleBoundariesTests`

**Options considered:**
- (A) Add a new `ModuleBoundaryRule` entry to the `Rules()` `TheoryData` in `ModuleBoundariesTests.cs`.
- (B) Rely on manual grep (per spec NFR-2) without a CI guard.

**Chosen approach:** (A).

**Rationale:** Every other module decoupling in this repo (Leaflet, Article, Logistics, PackingMaterials, Purchase, ExpeditionListArchive) is enforced by a reflection-based rule in this test. Without one, regressions are inevitable. The new rule must also include a check against `Anela.Heblo.Domain.Features.Analytics → Anela.Heblo.Domain.Features.Catalog` (the domain entity `AnalyticsProduct.cs` is the original violation site — the current test only inspects the Application assembly). Either extend the existing test to also load `Anela.Heblo.Domain`, or add a parallel test scoped to the Domain assembly.

#### Decision 5: Preserve current materialization semantics; do not claim "streaming"

**Options considered:**
- (A) Document the adapter as streaming (per spec NFR-1).
- (B) Acknowledge that the underlying `ICatalogRepository.GetProductsWithSalesInPeriod` returns `Task<List<CatalogAggregate>>` (eager) and that `IAsyncEnumerable` is a façade over an already-materialized list.

**Chosen approach:** (B) — be honest about current behavior.

**Rationale:** `AnalyticsRepository.cs:39` calls `await _catalogRepository.GetProductsWithSalesInPeriod(...)` which returns a fully materialized `List<CatalogAggregate>`. The `IAsyncEnumerable` yield loop and `GC.Collect()` calls (line 120) are theater over a list that is already in memory. The refactor must **preserve** this semantics (FR-7 behavior preservation), but the spec should not claim we are protecting streaming we don't have. True streaming is out of scope (it would require pushing changes into `ICatalogRepository` and the underlying provider).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Analytics/
  Contracts/
    IAnalyticsProductSource.cs              ← NEW (cross-module contract)
  Infrastructure/
    IAnalyticsRepository.cs                 ← MODIFIED (drop ProductType[])
    AnalyticsRepository.cs                  ← MODIFIED (drop ICatalogRepository,
                                              mapping moves to adapter)

backend/src/Anela.Heblo.Domain/Features/Analytics/
  AnalyticsProductType.cs                   ← NEW
  AnalyticsProduct.cs                       ← MODIFIED (Type: AnalyticsProductType)

backend/src/Anela.Heblo.Application/Features/Catalog/
  Infrastructure/
    CatalogAnalyticsSourceAdapter.cs        ← NEW (internal sealed, like
                                              PurchaseMaterialCatalogAdapter)
  CatalogModule.cs                          ← MODIFIED (register binding)

backend/test/Anela.Heblo.Tests/
  Architecture/
    ModuleBoundariesTests.cs                ← MODIFIED (add Analytics→Catalog rule
                                              spanning Domain + Application)
  Features/Catalog/Infrastructure/
    CatalogAnalyticsSourceAdapterTests.cs   ← NEW (per NFR-3 — mapping was
                                              previously untested)
```

### Interfaces and Contracts

**`IAnalyticsProductSource`** (Analytics-owned, in `Contracts/`):
```csharp
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public interface IAnalyticsProductSource
{
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default);

    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
```

**`AnalyticsProductType`** (Analytics-owned, in `Domain.Features.Analytics`):
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public enum AnalyticsProductType
{
    Product,
    Goods,
}
```

Mirror only the values Analytics actually consumes today. Inspection of all Analytics call sites (`GetMarginReportHandler.cs:57` and `GetProductMarginSummaryHandler.cs:37`) shows only `ProductType.Product` and `ProductType.Goods` are passed. Do **not** mirror `Material`, `SemiProduct`, `Set`, `UNDEFINED` — they are unused in Analytics and including them is speculative.

**`IAnalyticsRepository`** (signature change):
```csharp
IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
    DateTime fromDate, DateTime toDate,
    AnalyticsProductType[] productTypes,            // was ProductType[]
    CancellationToken cancellationToken = default);

Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
    DateTime fromDate, DateTime toDate,
    AnalyticsProductType[] productTypes,            // was ProductType[]
    ProductGroupingMode groupingMode,
    CancellationToken cancellationToken = default);
```

**`CatalogAnalyticsSourceAdapter`** (Catalog-owned, mirrors `PurchaseMaterialCatalogAdapter` shape):
- `internal sealed class CatalogAnalyticsSourceAdapter : IAnalyticsProductSource`
- Constructor takes `ICatalogRepository` only
- Private `MapToAnalyticsProduct(CatalogAggregate, DateTime fromDate, DateTime toDate)` extracts the duplicated mapping at lines 80–116 and 196–231 of `AnalyticsRepository.cs` into a single helper (the current code violates DRY across the streaming and single-product paths). **Note on the single-product path**: line 223 of `AnalyticsRepository.cs` does not filter `SalesHistory` by the period — preserve that asymmetry verbatim (FR-7) or document it as a follow-up bug.
- Private `MapProductType(AnalyticsProductType) → Catalog.ProductType` at the adapter boundary

### Data Flow

```
Handler (e.g., GetMarginReportHandler)
  ↓ passes new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods }
  ↓
AnalyticsRepository.StreamProductsWithSalesAsync
  ↓ delegates (no mapping, no ProductType conversion here)
  ↓
IAnalyticsProductSource.StreamProductsWithSalesAsync       ← module boundary
  ↓
CatalogAnalyticsSourceAdapter
  ↓ MapProductType: AnalyticsProductType[] → ProductType[]
  ↓
ICatalogRepository.GetProductsWithSalesInPeriod
  ↓ returns List<CatalogAggregate>
  ↓
CatalogAnalyticsSourceAdapter.MapToAnalyticsProduct (per item, yield return)
  ↓ returns AnalyticsProduct
  ↓
Handler consumes AnalyticsProduct stream
```

`AnalyticsRepository` becomes a thin pass-through for the two product-source methods. Consider whether `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync` should remain on `IAnalyticsRepository` at all, or whether handlers should inject `IAnalyticsProductSource` directly. **Recommendation:** keep them on `IAnalyticsRepository` to minimize blast radius and preserve handler/test signatures; only the inner delegation changes. This is the conservative path per FR-7.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec omits `IAnalyticsRepository` interface signature — refactor leaves handlers still importing Catalog | HIGH | Amend spec FR-5 to include the interface; update handler call sites in same PR. |
| `ModuleBoundariesTests` not extended → silent regression two weeks later | HIGH | Mandatory rule addition; rule must cover both `Anela.Heblo.Domain.Features.Analytics` and `Anela.Heblo.Application.Features.Analytics` namespaces. |
| Existing `AnalyticsRepository` test mocks of `ICatalogRepository` break en masse | MEDIUM | Replace mocks of `ICatalogRepository` with mocks of `IAnalyticsProductSource` in `GetProductMarginAnalysisHandlerTests`, `GetMarginReportHandlerTests`, `GetProductMarginSummaryHandlerTests`. Verify all six test files in `backend/test/Anela.Heblo.Tests/Features/Analytics/`. |
| `AnalyticsProductType` enum drift (Catalog adds a value Analytics needs later) | LOW | Document in `AnalyticsProductType.cs` xmldoc that this is an Analytics-owned subset; if Analytics needs a new value, mirror it and update `MapProductType` in the adapter. No runtime conversion safety net needed since the adapter owns both sides of the mapping. |
| Single-product path (`GetProductAnalysisDataAsync`) does not filter `SalesHistory` by period (line 223 of current code), unlike streaming path (line 107) | LOW | Preserve verbatim per FR-7; file a separate ticket if this is actually a bug. Do **not** "fix" it during this refactor (surgical changes rule). |
| DI lifetime mismatch | LOW | Register adapter as `AddTransient` to match `ICatalogRepository` lifetime, not `AddScoped`. |
| `Catalog.CatalogAggregate` evolves after refactor, breaking adapter mapping silently | LOW | The new `CatalogAnalyticsSourceAdapterTests` (NFR-3) covers the mapping. This is a net improvement — currently no test exercises the mapping at all. |

## Specification Amendments

1. **FR-5 expansion:** Extend FR-5 to include `IAnalyticsRepository` (the interface, not just `AnalyticsRepository`). The two methods `StreamProductsWithSalesAsync` and `GetGroupMarginTotalsAsync` must take `AnalyticsProductType[]` instead of `ProductType[]`, and the file must drop `using Anela.Heblo.Domain.Features.Catalog;`.

2. **FR-6 expansion:** Add `GetProductMarginAnalysisHandler` to the scope. While it does not import `ProductType` today, it consumes `IAnalyticsRepository.GetProductAnalysisDataAsync` and its tests will need adapter mocking updates (NFR-3 implies this but the spec lists only two handlers in FR-6).

3. **New FR-8 — Architecture test rule:** Add an `Analytics → Catalog` `ModuleBoundaryRule` to `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. The rule must inspect both `Anela.Heblo.Application.Features.Analytics` (currently the test only loads the Application assembly) and `Anela.Heblo.Domain.Features.Analytics` (the `AnalyticsProduct.Type` violation lives in the Domain assembly — this requires loading `Anela.Heblo.Domain` as well). Empty allowlist.

4. **FR-4 lifetime correction:** Register `CatalogAnalyticsSourceAdapter` as `AddTransient`, not `AddScoped`. This matches `ICatalogRepository` (`CatalogModule.cs:41`) and avoids captive-dependency risk.

5. **NFR-1 wording correction:** Drop the claim about "preserving streaming." The current code is not streaming under the hood — `ICatalogRepository.GetProductsWithSalesInPeriod` returns a materialized `List<CatalogAggregate>`. Replace with: "The adapter must invoke the same `ICatalogRepository` methods and yield the same items in the same order; no new SQL queries; memory profile unchanged."

6. **Mapping consolidation (clarification):** The current `AnalyticsRepository.cs` duplicates the `CatalogAggregate → AnalyticsProduct` mapping across two methods (lines 80–116 and 196–231) with one subtle difference (sales filter applied in streaming path only). The adapter should extract a single private mapping helper and preserve the asymmetry as-is. Add this to FR-3.

7. **`AnalyticsModule.cs` cleanup (advisory):** Line 28's comment "`IMarginCalculationService` is registered by CatalogModule and injected here" is stale — no Analytics class injects it. No change required for this spec, but flag in a follow-up.

## Prerequisites

- **None blocking.** No migrations, no infrastructure, no config, no external dependencies.
- **Module wiring order verified:** `CatalogModule.AddCatalogModule()` must be called before any Analytics handler resolves `IAnalyticsProductSource`. Confirm `Program.cs` invokes `AddCatalogModule()` before `AddAnalyticsModule()` (it currently does, and Catalog has no inverse dependency on Analytics, so this is structurally sound).
- **Recommend bundling in one PR:** contract + adapter + DI registration + handler/interface changes + test updates + architecture rule. Splitting risks an intermediate state that fails the new architecture rule.