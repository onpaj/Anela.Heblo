# Architecture Review: Decouple ProductEnrichmentCache from Catalog Domain via Module Contract

## Skip Design: true

Backend-only refactor. No UI/UX surface change, no new screens, no visual components.

## Architectural Fit Assessment

**Excellent fit.** The proposal directly implements the documented inter-module communication pattern from `docs/architecture/development_guidelines.md`:

> *"Communication between modules **exclusively through `contracts/`** (e.g., `IProductQueryService`)"*
> *"**No direct access to another module's entities**"*

The current code violates both rules: `ProductEnrichmentCache` imports `Anela.Heblo.Domain.Features.Catalog`, resolves `ICatalogRepository`, builds a predicate over `CatalogAggregate`, and reads three fields from the domain entity. The refactor introduces precisely the `IProductQueryService`-style contract the guidelines mandate.

**Integration points (all preserved unchanged):**
- `IProductEnrichmentCache` consumer surface (used by `PostAnswerEnrichmentMiddleware`, `AskQuestionHandler`).
- `KnowledgeBaseOptions.ProductEnrichmentCacheTtlMinutes` config.
- Singleton lifetime of `IProductEnrichmentCache` resolving a transient via `IServiceScopeFactory`.

**One convention deviation to fix (see Amendments §1).** Spec calls for `internal sealed` implementation, but all four sibling files in `Features/Catalog/Services/` (`MarginCalculationService`, `StockUpProcessingService`, `ProductWeightRecalculationService`, `EshopStockDomainService`) are `public class`. Consistency matters more than the marginal encapsulation win.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│ Application/Features/KnowledgeBase                                │
│                                                                   │
│   ProductEnrichmentCache (singleton)                              │
│     │                                                             │
│     │ uses (via IServiceScopeFactory.CreateScope())               │
│     ▼                                                             │
│   IProductCatalogQueryService  ◄────── contract boundary ────┐    │
│                                                              │    │
└──────────────────────────────────────────────────────────────┼────┘
                                                               │
┌──────────────────────────────────────────────────────────────┼────┐
│ Application/Features/Catalog                                 │    │
│                                                              │    │
│   Contracts/                                                 │    │
│     IProductCatalogQueryService  ────────────────────────────┘    │
│     ProductCatalogEntry          (class, three string fields)     │
│                                                                   │
│   Services/                                                       │
│     ProductCatalogQueryService                                    │
│       │ depends on                                                │
│       ▼                                                           │
│   ICatalogRepository  ──►  CatalogAggregate (domain entity)       │
│                            ProductType (domain enum)              │
└───────────────────────────────────────────────────────────────────┘
```

The dotted boundary above is the only line KnowledgeBase may cross to read product data. `CatalogAggregate`, `ProductType`, and `ICatalogRepository` stay private to the Catalog module.

### Key Design Decisions

#### Decision 1: DTO as `class`, not `record`
**Options considered:** `record` (C# default for immutable DTOs per global rules), `class` (project convention).
**Chosen approach:** `public class` with mutable get/set properties.
**Rationale:** Project rule (`CLAUDE.md`): *"DTOs are classes, never C# records."* Every existing file in `Features/Catalog/Contracts/` (e.g., `CatalogItemDto.cs`, `PriceDto.cs`) follows this. Driven by OpenAPI client generator handling of record constructor parameter order. Even though this DTO isn't exposed via HTTP today, future reuse over the wire stays safe.

#### Decision 2: Filter and projection live in Catalog, not in the contract
**Options considered:** (a) Expose `FindAsync(predicate)` to KnowledgeBase via the contract; (b) Catalog owns the predicate and returns a pre-filtered, pre-projected list.
**Chosen approach:** (b). Method = `GetActiveProductsAsync()`. Predicate (`Type == Product || Type == Goods`) and mapping live inside `ProductCatalogQueryService`.
**Rationale:** A contract that takes a predicate would leak `CatalogAggregate` through the expression tree, defeating the whole refactor. Catalog owns what "active product" means; KnowledgeBase consumes the result.

#### Decision 3: Implementation is `public class` (deviation from spec)
**Options considered:** `internal sealed` (spec); `public class` (sibling convention).
**Chosen approach:** `public class ProductCatalogQueryService : IProductCatalogQueryService`.
**Rationale:** All four sibling implementations in `Features/Catalog/Services/` are `public class`. The whole `Anela.Heblo.Application` assembly ships as one unit today, so `internal` adds no real isolation but breaks pattern consistency and complicates any future direct-construction usage in tests. Mark `sealed` if desired — that part is cheap and harmless.

#### Decision 4: Keep `IServiceScopeFactory` indirection in the cache
**Options considered:** Inject `IProductCatalogQueryService` directly into the singleton cache; keep the scope-factory indirection.
**Chosen approach:** Keep `IServiceScopeFactory`.
**Rationale:** `IProductEnrichmentCache` is registered **singleton** (`KnowledgeBaseModule.cs:55`). The contract implementation depends on `ICatalogRepository`, registered **transient** (`CatalogModule.cs:39`). Capturing a transient in a singleton constructor is a captive-dependency bug. Spec correctly preserves this.

#### Decision 5: Lifetime of `IProductCatalogQueryService` = Transient
**Options considered:** Transient, Scoped, Singleton.
**Chosen approach:** Transient — matches `ICatalogRepository` (its only dependency) at `CatalogModule.cs:39`.
**Rationale:** Stateless wrapper; transient is the simplest correct choice and matches the dependency's lifetime so no captive-dependency warnings.

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Application/Features/Catalog/
├── Contracts/
│   ├── IProductCatalogQueryService.cs    ← new
│   └── ProductCatalogEntry.cs            ← new
└── Services/
    └── ProductCatalogQueryService.cs     ← new

backend/test/Anela.Heblo.Tests/Features/Catalog/
└── Services/
    └── ProductCatalogQueryServiceTests.cs  ← new
```

Modified files:
```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs
backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs
```

Note: the spec's suggested test path `backend/test/Anela.Heblo.Tests/Catalog/Services/` does not exist. The actual catalog test root is `backend/test/Anela.Heblo.Tests/Features/Catalog/` (verified). Use that.

### Interfaces and Contracts

```csharp
// Features/Catalog/Contracts/IProductCatalogQueryService.cs
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface IProductCatalogQueryService
{
    Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(
        CancellationToken ct = default);
}
```

```csharp
// Features/Catalog/Contracts/ProductCatalogEntry.cs
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class ProductCatalogEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; }
}
```

**Hard constraints:**
- Neither file may `using Anela.Heblo.Domain.*`. Enforce via review; consider adding a one-line grep gate in CI later (out of scope here).
- `ProductCatalogEntry` is a `class`, not a `record` (project rule).
- `IReadOnlyList<>` return type — keeps the contract immutable from the consumer's POV without forcing a copy on the caller.

### Data Flow

**Cache miss (after TTL expiry):**
```
PostAnswerEnrichmentMiddleware / AskQuestionHandler
  └─► IProductEnrichmentCache.GetProductLookupAsync(ct)              [singleton]
        ├─ TTL expired → acquire SemaphoreSlim, re-check
        ├─ scope = _scopeFactory.CreateScope()
        ├─ svc = scope.ServiceProvider.GetRequiredService<IProductCatalogQueryService>()  [transient]
        │     └─ ctor injects ICatalogRepository (transient)
        ├─ entries = await svc.GetActiveProductsAsync(ct)
        │     └─ repo.FindAsync(p => p.Type == Product || p.Type == Goods, ct)
        │     └─ .Select(p => new ProductCatalogEntry { ProductCode, ProductName, Url })
        │     └─ .ToList()  // → IReadOnlyList<ProductCatalogEntry>
        ├─ _cache = entries.ToDictionary(e => e.ProductCode, e => new ProductEnrichmentEntry {...})
        └─ release lock, return _cache
```

**Cache hit:** identical to today — direct dictionary read, no scope creation, no allocations.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Singleton captures transient (captive-dependency) | High | Spec already mandates keeping `IServiceScopeFactory` — verify in PR review that constructor of `ProductEnrichmentCache` still takes `IServiceScopeFactory` and not the new contract directly. |
| Duplicate product codes cause `ToDictionary` to throw | Low | Pre-existing risk, not new — current code does the same. Out of scope. If observed, switch to `GroupBy(x).First()` in a follow-up. |
| Contract used by other modules later with different "active" definition | Low | Method name `GetActiveProductsAsync` is generic enough; if a divergent need appears, add a second method rather than parameterizing. |
| Test predicate-capture pattern (Moq `Callback`) lost when test moves | Low | Predicate test moves to `ProductCatalogQueryServiceTests`. Use the same capture-and-compile pattern shown in the existing test (lines 43–70) to assert `Product`/`Goods` allowed, others rejected. |
| Convention drift — spec says `internal sealed`, siblings are `public class` | Low | Implement as `public class` per Decision 3. If `sealed` is desired, add it; do not make the type `internal`. |
| Stale test path in spec (`Tests/Catalog/Services/`) | Low | Use `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs` instead. |

## Specification Amendments

1. **§FR-2 acceptance criterion 1:** Change *"`ProductCatalogQueryService` is `internal sealed`"* to *"`ProductCatalogQueryService` is `public class` (or `public sealed class`), matching sibling services in `Features/Catalog/Services/`."* Rationale: consistency with `MarginCalculationService`, `StockUpProcessingService`, `ProductWeightRecalculationService`, `EshopStockDomainService`, all of which are `public class`.

2. **§FR-5 acceptance criterion (new test location):** Replace *"under `backend/test/Anela.Heblo.Tests/Catalog/Services/`"* with *"under `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/`"*. The path `Tests/Catalog/` does not exist; the existing pattern is `Tests/Features/Catalog/`.

3. **§FR-3 placement:** Spec says *"after line ~62, before feature-flags block."* Line 62 is currently `services.AddScoped<IEshopStockDomainService, EshopStockDomainService>();` (end of the "Register catalog-specific services" block). Place the new registration on a new line immediately after that one — same block, same lifetime style, transient alongside `IMarginCalculationService` at line 56.

4. **§FR-5 (clarify predicate-test method):** When porting the `PredicateFiltersToProductAndGoodsOnly` test to `ProductCatalogQueryServiceTests`, reuse Moq's `Callback` to capture the `Expression<Func<CatalogAggregate, bool>>`, compile it, and assert it accepts `Product`/`Goods` and rejects `Material`/`SemiProduct`/`Set`/`UNDEFINED` — same shape as the existing test at lines 43–70 of `ProductEnrichmentCacheTests.cs`. The new file is the only place still allowed to reference `CatalogAggregate` and `ProductType` for this assertion (it lives in the Catalog test tree).

5. **§FR-5 (KnowledgeBase test mocking):** When rewriting `ProductEnrichmentCacheTests.cs`, follow the same `IServiceScopeFactory` → `IServiceScope` → `IServiceProvider` mock chain already in place (lines 13–25); only the resolved service changes from `ICatalogRepository` to `IProductCatalogQueryService`, and `SetupRepository` is replaced by a setup on `service.GetActiveProductsAsync(...)` returning `ProductCatalogEntry[]`.

## Prerequisites

None. The refactor is self-contained:

- No DB migrations.
- No new configuration keys (`KnowledgeBaseOptions.ProductEnrichmentCacheTtlMinutes` already exists).
- No new NuGet packages.
- No new infrastructure.
- DI ordering in `Program.cs` is unchanged — `AddCatalogModule()` is already called before `AddKnowledgeBaseModule()` (and even if it weren't, registration order doesn't matter for resolution).
- Build gate: `dotnet build` after the change must compile with zero references to `Anela.Heblo.Domain.Features.Catalog` from anywhere under `Features/KnowledgeBase/` (verifiable by grep).