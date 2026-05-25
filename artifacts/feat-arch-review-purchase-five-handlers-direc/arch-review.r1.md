# Architecture Review: Decouple Purchase Handlers from Catalog Domain via Consumer-Owned Contract

## Skip Design: true

## Architectural Fit Assessment

The proposed work is an exact application of the codebase's already-documented and already-tested **Cross-Module Communication pattern** (`docs/architecture/development_guidelines.md` §"Cross-Module Communication Example: ILeafletKnowledgeSource"). The pattern is alive and enforced today by `ModuleBoundariesTests` for three other pairs (Leaflet→KnowledgeBase, Logistics→Manufacture, PackingMaterials→Invoices) and additionally Logistics→Purchase via a dedicated `[Fact]`. The spec extends this pattern to the Purchase→Catalog edge — no new architecture, no new conventions.

Integration points verified:
- Consumer-side contract location matches the precedent (`Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` → `Application/Features/Purchase/Contracts/IMaterialCatalogService.cs`).
- Provider-side adapter location and visibility match the precedent (`Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` is `internal sealed` → same for `PurchaseMaterialCatalogAdapter`).
- DI registration in the provider module matches the documented step ("Provider (B) registers the DI binding").
- `ICatalogRepository.GetByIdsAsync` bulk method exists (line 54) — adapter can delegate directly without an N+1 fallback.
- `InternalsVisibleTo("Anela.Heblo.Tests")` is already declared in `AssemblyInfo.cs:3` — adapter unit tests can instantiate the internal class directly.
- `StockSeverity` enum lives in Purchase (`GetPurchaseStockAnalysisResponse.cs:96`), and `IStockSeverityCalculator` lives in `Purchase/Services/` — neither crosses any module boundary; no relocation needed.

The one **drift from precedent** worth naming: the spec extends the pattern beyond a single thin pass-through (Leaflet's adapter is ~25 LOC) into a richer projection layer that pre-computes consumption snapshots and last-purchase summaries. This is justified — the alternative (exposing `CatalogAggregate.GetConsumed` semantics through the contract) would force Purchase to know domain methods. The richer adapter is the correct trade-off.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application.Features.Purchase                     │
│                                                                │
│  UseCases/                                                     │
│    ├─ CreatePurchaseOrderHandler  ──┐                          │
│    ├─ UpdatePurchaseOrderHandler  ──┤                          │
│    ├─ GetPurchaseOrderByIdHandler ──┼─→ IMaterialCatalogService│
│    ├─ GetPurchaseStockAnalysis…   ──┤   (Purchase-owned)       │
│    └─ RecalculatePurchasePrice…   ──┘                          │
│                                                                │
│  Contracts/  (NEW)                                             │
│    ├─ IMaterialCatalogService.cs                               │
│    ├─ MaterialInfo.cs                                          │
│    ├─ MaterialBomReference.cs                                  │
│    ├─ MaterialStockSnapshot.cs                                 │
│    ├─ MaterialStockLevels.cs                                   │
│    ├─ MaterialPurchaseSnapshot.cs                              │
│    └─ MaterialProductType.cs   (enum)                          │
└──────────────────────────────┬─────────────────────────────────┘
                               │ (DI: bound in CatalogModule)
                               ▼
┌────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application.Features.Catalog                      │
│                                                                │
│  Infrastructure/                                               │
│    └─ PurchaseMaterialCatalogAdapter.cs  (NEW, internal sealed)│
│            │                                                   │
│            │  • projects CatalogAggregate → MaterialInfo       │
│            │  • applies Material/Goods filter                  │
│            │  • pre-computes GetConsumed/GetTotalSold          │
│            │  • maps ProductType → MaterialProductType         │
│            ▼                                                   │
│        ICatalogRepository  (existing, unchanged)               │
└────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│  backend/test/Anela.Heblo.Tests/Architecture/                  │
│    └─ ModuleBoundariesTests.cs  (EXTENDED)                     │
│         + "Purchase -> Catalog" rule (mirrors Leaflet rule)    │
│         + PurchaseAllowlist with 1 entry (IProductPriceErpClient│
│           — pre-existing, deferred to follow-up arch-review)   │
└────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Adapter performs projection, not just delegation
**Options considered:**
- (A) Thin pass-through adapter exposing methods like `GetByIdAsync`/`GetAllAsync` returning `MaterialInfo`; Purchase handlers continue to do filtering, `GetConsumed`/`GetTotalSold`, and last-purchase computation locally on richer DTOs.
- (B) Richer adapter that pre-computes Material/Goods filtering, per-period consumption, and last-purchase snapshots inside `GetStockAnalysisSnapshotsAsync` and `GetMaterialsWithBomAsync`.

**Chosen approach:** B (the spec's choice).

**Rationale:** Option A leaks Catalog domain semantics (knowing that `Material` uses `GetConsumed` while `Goods` uses `GetTotalSold`, knowing the shape of `PurchaseHistory`, knowing the `HasBoM`/`BoMId.HasValue` invariant) into Purchase. The whole point of the contract is to hide Catalog's domain rules. The adapter pays a small complexity cost so the contract stays narrow and Purchase stops co-evolving with `CatalogAggregate`.

#### Decision 2: Contract returns `MaterialInfo?` and `IReadOnlyDictionary<string, MaterialInfo>` (missing IDs omitted)
**Options considered:**
- (A) Return `MaterialInfo?` for single lookup; dictionary for batch with missing keys omitted (matches `ICatalogRepository.GetByIdsAsync` shape).
- (B) Throw `NotFoundException` for misses.

**Chosen approach:** A.

**Rationale:** The existing handlers already treat "material not in catalog" as a soft fallback (`material?.ProductName ?? lineRequest.Name ?? "Unknown Material"`). Preserving null-semantics keeps behavior parity and avoids changing error responses. This is also how `ICatalogRepository.GetByIdsAsync` already behaves (line 54 in `ICatalogRepository.cs`).

#### Decision 3: One allowlist entry for `IProductPriceErpClient`, follow-up arch-review for full decoupling
**Options considered:**
- (A) Decouple `IProductPriceErpClient` in the same PR (introduce `IPurchasePriceRecalculator` Purchase-owned contract + Catalog Price-side adapter).
- (B) Allowlist `IProductPriceErpClient` and defer to a follow-up arch-review, mirroring the Leaflet `IDocumentTextExtractor` precedent (lines 36–37 of `ModuleBoundariesTests.cs`).

**Chosen approach:** B (the spec's choice).

**Rationale:** `IProductPriceErpClient` is an ERP integration boundary, not a domain repository. Decoupling it requires a different design conversation (whether the contract belongs in Purchase at all, or is shared infra). Bundling it into this PR would balloon scope. The allowlist mechanism with explicit comment + tracking date is the codebase's documented escape hatch for exactly this case.

#### Decision 4: DI lifetime — `Scoped` adapter
**Options considered:**
- (A) `Scoped` (per the spec).
- (B) `Transient` (to mirror `ICatalogRepository`'s registration at `CatalogModule.cs:40`).

**Chosen approach:** A (Scoped), with the spec's rationale corrected.

**Rationale:** `ICatalogRepository` is actually registered as `Transient` today, not `Scoped` as the spec's FR-3 acceptance criterion implies. However, MediatR handlers are `Scoped` and the adapter is stateless, so a Scoped adapter that depends on a Transient `CatalogRepository` produces the same observable behavior as today (one instance per HTTP request). Scoped is the right choice — it matches how the handlers are resolved and avoids per-call allocation of the adapter itself. The spec's rationale should be amended (see "Specification Amendments" below).

## Implementation Guidance

### Directory / Module Structure

New files (with exact paths):

```
backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/
  ├── IMaterialCatalogService.cs       (public interface)
  ├── MaterialInfo.cs                  (public sealed class)
  ├── MaterialBomReference.cs          (public sealed class)
  ├── MaterialStockSnapshot.cs         (public sealed class)
  ├── MaterialStockLevels.cs           (public sealed class)
  ├── MaterialPurchaseSnapshot.cs      (public sealed class)
  └── MaterialProductType.cs           (public enum)

backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/
  └── PurchaseMaterialCatalogAdapter.cs   (internal sealed class)

backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
  └── PurchaseMaterialCatalogAdapterTests.cs
```

Modified files:
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — add one `services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();` line inside `AddCatalogModule`, after the existing `AddScoped` block (around line 54).
- All five Purchase handlers — replace `ICatalogRepository` ctor parameter, field, and call sites; remove `using Anela.Heblo.Domain.Features.Catalog;`.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add `PurchaseAllowlist` field + new row in `Rules()`.
- Existing Purchase handler tests that mock `ICatalogRepository`.

Files **not** touched:
- `PurchaseModule.cs` — no Purchase-side registration of `IMaterialCatalogService` (binding belongs to the provider per the documented pattern).
- `ICatalogRepository.cs`, `CatalogAggregate.cs`, `CatalogRepository.cs` — adapter delegates to existing surface; no Catalog-side changes.
- `IStockSeverityCalculator`, `StockSeverity` — already Purchase-owned.

### Interfaces and Contracts

The `IMaterialCatalogService` signature in the spec (FR-1) is sound and final. Reproduced here for emphasis on what implementations must NOT reference:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IMaterialCatalogService
{
    Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken);
}
```

**Hard rule for implementers:** Open `IMaterialCatalogService.cs` and grep for `using Anela.Heblo.Domain.Features.Catalog` or `using Anela.Heblo.Application.Features.Catalog` — both must return zero matches. Same for every DTO file under `Contracts/`. The architectural test (FR-5) will fail the build if this leaks.

DTO style — per project convention (CLAUDE.md "DTOs are classes, never C# records"):
```csharp
public sealed class MaterialInfo
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Note { get; init; }
    public bool HasBoM { get; init; }
    public int? BoMId { get; init; }
}
```
`init` setters are acceptable (and preferable for immutability) because these contracts are **not** serialized through the OpenAPI generator — the generator-incompatible-with-records rule applies to request/response DTOs that round-trip through HTTP. `Contracts/IMaterialCatalogService.cs` and its companions are internal-to-the-process boundary, never serialized; classes with `init` are fine.

### Data Flow

**Flow A: GetPurchaseOrderByIdHandler (N+1 → batch)**

```
Controller → MediatR
  → GetPurchaseOrderByIdHandler
      → IPurchaseOrderRepository.GetByIdWithDetailsAsync
      → ISupplierRepository.GetByIdAsync
      → IMaterialCatalogService.GetByIdsAsync(distinct materialIds)  ◄── ONE call
          → CatalogRepository.GetByIdsAsync                           ◄── ONE underlying call
          → projection: IReadOnlyDictionary<string, CatalogAggregate>
                        → IReadOnlyDictionary<string, MaterialInfo>
      → Response.Lines.Select(...).CatalogNote = lookup.GetValueOrDefault(...)?.Note
```

**Flow B: GetPurchaseStockAnalysisHandler (richest path)**

```
Controller → MediatR
  → GetPurchaseStockAnalysisHandler
      → IMaterialCatalogService.GetStockAnalysisSnapshotsAsync(fromDate, toDate)
          → CatalogRepository.GetAllAsync                              ◄── ONE call
          → filter: Type in {Material, Goods}
          → per-item projection inside the adapter:
              ├─ ProductType mapping (Material/Goods → MaterialProductType)
              ├─ consumption: Material → GetConsumed(from,to); Goods → GetTotalSold(from,to)
              ├─ stock levels (Available, Ordered, EffectiveStock)
              ├─ properties (StockMinSetup, OptimalStockDaysSetup)
              └─ last purchase: PurchaseHistory.OrderByDescending(Date).FirstOrDefault()
      → handler runs pure-in-memory analysis on IReadOnlyList<MaterialStockSnapshot>
        (severity, stockout days, recommended qty, summary, paging, sort, search)
```

The critical invariant: **the adapter must execute consumption pre-computation in the same numeric way the handler does today**, or summary counts drift. The unit tests in FR-6 must include a "behavior parity" test that compares the adapter's `ConsumptionInPeriod` against `CatalogAggregate.GetConsumed`/`GetTotalSold` for known fixtures.

**Flow C: RecalculatePurchasePriceHandler (split paths)**

```
Single-product path  (request.ProductCode != null):
  → IMaterialCatalogService.GetByIdAsync(productCode)
  → validate: result != null && HasBoM && BoMId.HasValue
  → IProductPriceErpClient.RecalculatePurchasePrice(BoMId.Value)
     [^^^ allowlisted; out of scope to decouple in this PR]

All-products path  (RecalculateAll == true):
  → IMaterialCatalogService.GetMaterialsWithBomAsync()
     → returns IReadOnlyList<MaterialBomReference> with non-nullable BoMId
  → for each: IProductPriceErpClient.RecalculatePurchasePrice(BoMId)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Behavior drift in stock analysis** — adapter's pre-computed `ConsumptionInPeriod`, `LastPurchase`, and Material/Goods filter must produce byte-identical results to the current per-item computation, or `Summary.CriticalCount`, `OptimalCount`, etc. shift. | **HIGH** | FR-7 calls for updating existing handler tests; explicitly extend `GetPurchaseStockAnalysisHandlerTests` to assert summary counts against fixtures with known consumption values. Adapter unit tests in FR-6 must include a Material-vs-Goods split test. |
| **`GetPurchaseStockAnalysisHandlerDiacriticsTests` may break** — that test exists (`backend/test/.../GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`) and references `ICatalogRepository` per the grep above. It tests `ProductNameNormalized` search. Normalization currently happens inside `CatalogAggregate.ProductName` setter. | **MEDIUM** | `MaterialStockSnapshot.ProductNameNormalized` must be populated by the adapter directly from `CatalogAggregate.ProductNameNormalized` (already exposed, line 31 of CatalogAggregate.cs). Test must be updated to mock `IMaterialCatalogService` and return snapshots with pre-normalized names. |
| **DI lifetime mismatch with existing `ICatalogRepository` registration** — spec FR-3 claims "Scoped, consistent with how `ICatalogRepository` is consumed today," but `ICatalogRepository` is actually registered Transient at `CatalogModule.cs:40`. | **LOW** | Functionally a non-issue because handlers are Scoped and `CatalogRepository` is cache-backed and stateless per resolution. Correct the spec rationale (see Amendments below). |
| **Adapter materializes large collections per request** — `GetStockAnalysisSnapshotsAsync` calls `GetAllAsync` and projects every Material/Goods item. Today's handler does the same; no regression, but it's a known hot path. | **LOW** | NFR-1 already addresses this — adapter must not introduce extra materialization. Add a comment in the adapter pointing to the existing pattern; defer any query-side filtering to a separate work item. |
| **`PurchaseAllowlist` accidentally bloats** as developers discover other Purchase ↔ Catalog references during implementation. | **MEDIUM** | The spec is explicit (FR-5 acceptance criteria, last bullet): exactly one entry. Reviewer must reject PRs that add new entries; any newly-discovered violations get a follow-up issue, not an allowlist line. Enforce in code review. |
| **Test assembly access to internal adapter** — assumes `InternalsVisibleTo` is set up correctly. | **LOW** | Verified: `backend/src/Anela.Heblo.Application/AssemblyInfo.cs:3` declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]`. No new attribute needed. |
| **Compiler-generated state machines** (`async` methods, captured-locals classes) inside Purchase handlers may reference `CatalogAggregate` after the refactor if any handler still has a local of that type, triggering the architectural test. | **MEDIUM** | The `ModuleBoundariesTests` already handles this via the `DeclaringType` check (lines 129–135 of `ModuleBoundariesTests.cs`). However, the test does **not** inspect method bodies, only signatures — so the risk is actually low. Verify post-refactor with `dotnet test --filter Consumer_types_should_not_reference_provider_owned_namespaces`. |

## Specification Amendments

The spec is comprehensive and aligned with the codebase. Three small corrections:

1. **FR-3 lifetime rationale (CORRECTION).** The spec states "Lifetime is `Scoped`, consistent with how `ICatalogRepository` is consumed today." `ICatalogRepository` is registered as `Transient` (see `CatalogModule.cs:40`), not Scoped. Replace the rationale with: *"Lifetime is `Scoped` to match MediatR handlers and to avoid per-call adapter allocation. The underlying `ICatalogRepository` is `Transient` and stateless, so a Scoped adapter holding a per-request `ICatalogRepository` instance is behaviorally equivalent to today's pattern."*

2. **FR-7 scope clarification (ADDITION).** Add an explicit mention of `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` to the handler tests that must be updated — that file references `ICatalogRepository` and tests diacritic-tolerant search via `ProductNameNormalized`. The adapter must populate `MaterialStockSnapshot.ProductNameNormalized` directly from `CatalogAggregate.ProductNameNormalized` for parity.

3. **FR-6 fixture guidance (ADDITION).** Add an explicit "behavior-parity" test to the adapter's unit-test list: for a synthetic `CatalogAggregate` with known `ConsumedHistory` and `SalesHistory`, assert that `MaterialStockSnapshot.ConsumptionInPeriod` equals `CatalogAggregate.GetConsumed(from, to)` for Material items and `CatalogAggregate.GetTotalSold(from, to)` for Goods items. This is the single highest-leverage test for catching the HIGH-severity regression risk above.

No structural changes to the spec are required. Open Questions remains "None."

## Prerequisites

None. All scaffolding required for the implementation already exists:

- ✅ `ICatalogRepository.GetByIdsAsync` bulk method (`ICatalogRepository.cs:54`) — no new repository methods needed.
- ✅ `InternalsVisibleTo("Anela.Heblo.Tests")` (`AssemblyInfo.cs:3`) — adapter tests can instantiate the internal class.
- ✅ `ModuleBoundariesTests` (`backend/test/.../ModuleBoundariesTests.cs`) infrastructure with reflection-based enforcement, `Theory`/`MemberData` extensibility, and `DeclaringType` compiler-generated-type handling.
- ✅ `StockSeverity` enum already in Purchase namespace (`GetPurchaseStockAnalysisResponse.cs:96`) — no relocation needed.
- ✅ `IStockSeverityCalculator` already in `Purchase/Services/` — no relocation.
- ✅ The Leaflet/KnowledgeBase precedent gives the implementer a working, runnable reference.
- ✅ No database migrations, no config additions, no new NuGet packages.

Implementation can start immediately.