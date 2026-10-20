# Architecture Review: Split CatalogDataRefreshService by source family

## Skip Design: true

This is a backend-only, internal application-layer refactor with no UI, no controller, and no MediatR request/response surface touched. There is nothing for the designer agent to produce beyond confirming component boundaries, which this review already fixes precisely.

## Architectural Fit Assessment

The Catalog module already follows the "decomposed collaborators" pattern the codebase uses elsewhere in this exact area: `CatalogRepository` (query/merge orchestration + `ICatalogRepository` implementation) delegates to `CatalogCacheStore` (cache state), `CatalogMergeService` (priority merge), `ICatalogMergeScheduler` (merge scheduling), `BundleSalesExpander` (bundle sales expansion), and — today — a single `CatalogDataRefreshService` (data fetch + cache population). All of `CatalogCacheStore`, `CatalogMergeService`, `BundleSalesExpander` are registered as `Singleton` in `CatalogModule.cs`; `CatalogDataRefreshService` alone is `Transient`. Splitting the refresh responsibility into four more collaborators of the same shape (constructor-injected into `CatalogRepository`, each doing one cohesive slice of fetch-then-cache work) is a direct continuation of this established pattern, not a new one.

`docs/architecture/development_guidelines.md` requires DI registration to live in the owning module's `{Feature}Module.cs` (`CatalogModule.AddCatalogModule`), which this change already respects — no `PersistenceModule.cs` or cross-module registration is touched. It also requires module communication only through `contracts/`; `CatalogHistoryRefreshService`/`CatalogStockRefreshService`/`CatalogMetaRefreshService`/`CatalogReferenceRefreshService` are all internal to `Catalog/Infrastructure/` and never crossed a module boundary before or after this change — the interfaces they consume (`ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`) are unchanged cross-module contracts owned by other modules (Logistics/Purchase/Manufacture), consumed exactly as before.

The single integration point that matters is `ICatalogRepository`: `CatalogModule.RegisterBackgroundRefreshTasks` schedules 19 background tasks exclusively against `ICatalogRepository` methods (via `nameof(ICatalogRepository.RefreshXxxData)`), and `CatalogRepository` is the only class that references `CatalogDataRefreshService` today. That means the "one hop of indirection" the codebase already has between the scheduler and the fetch logic is exactly the seam this refactor needs — no scheduler code, task naming, or tiering logic needs to know that one class became four.

## Proposed Architecture

### Component Overview

Before:
```
CatalogModule.RegisterBackgroundRefreshTasks
        │  (schedules 19 tasks against ICatalogRepository)
        ▼
   ICatalogRepository  ◄────────────────── CatalogRepository (implementation)
                                                    │
                                                    │ 1:1 delegate, all 19 methods
                                                    ▼
                                       CatalogDataRefreshService  (22 ctor params)
                                          ├─ 11 API clients
                                          ├─ 3 cross-module sources
                                          ├─ 2 repositories
                                          └─ resilience + time + options + cache + logger
```

After:
```
CatalogModule.RegisterBackgroundRefreshTasks        (UNCHANGED — still targets ICatalogRepository)
        │
        ▼
   ICatalogRepository  ◄────────────────── CatalogRepository (implementation)
                                                    │
                    ┌───────────────┬───────────────┼───────────────┐
                    │ routes each   │ Refresh*Data   │ call to the   │ owning service
                    ▼               ▼                ▼               ▼
     CatalogHistoryRefresh   CatalogStockRefresh  CatalogMetaRefresh  CatalogReferenceRefresh
       Service (10 params)   Service (8 params)   Service (8 params)  Service (5 params)
       sales, set-parts,     ERP/eshop stock,      attributes, lots,   stock-taking,
       purchase history,     transport/reserve/    eshop/ERP prices,   manufacture-difficulty
       consumed history,     quarantine, ordered,  eshop URLs          settings, manufacture-
       manufacture history   manufactured/planned                     cost cross-reference
```

`CatalogCacheStore` and `ICatalogResilienceService` are injected into every one of the four new classes that needs them (all four need `CatalogCacheStore`; three of four need `ICatalogResilienceService` — `CatalogReferenceRefreshService`'s three methods do not call it today, matching the original code, so it is **not** added there).

### Key Design Decisions

#### Decision 1: Grouping boundary — by shared private helper and natural read-shape, not by the issue's literal suggested split
**Options considered:**
1. Follow the issue body's suggested grouping verbatim (`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`, `CatalogReferenceRefreshService` with the exact method lists it proposes).
2. Re-derive the grouping from the actual 18 method bodies, their private helpers, and which cache setters/clients they touch, keeping the same four class names (since they're reasonable and already load-bearing in the issue title) but correcting membership.
3. Split into more, smaller classes (one per method) for maximal SRP.

**Chosen approach:** Option 2. The issue's literal method-to-class mapping has two problems: it omits `RefreshManufactureCostData` entirely (an 18th/20th method that exists on the class but isn't mentioned), and it groups `RefreshSetPartsData` away from its private helper `FetchSetPartsPerBundleAsync` conceptually (both must move together since the helper is private and only called by that one method — no ambiguity there, but the issue's "5 clients" grouping for `CatalogMetaRefreshService` double-counts `ICatalogAttributesClient` differently than the real dependency graph does).

**Rationale:** A refactor whose job is to fix an SRP violation must not introduce a class with an incomplete or wrong method list — that would just move the confusion rather than resolve it. Option 3 (one class per method) was rejected: several methods share a cross-module source (`ICatalogManufactureSource` is used by both a "history" method and two "stock" methods) so per-method classes would either duplicate that dependency's constructor wiring four times or reintroduce a shared base class, adding ceremony for no testability gain over the four-class split, which already gets every class under 10 constructor parameters.

#### Decision 2: `ICatalogManufactureSource` is shared between two of the four classes, not exclusively owned
**Options considered:**
1. Give `CatalogHistoryRefreshService` exclusive ownership of `ICatalogManufactureSource` and have `CatalogStockRefreshService` reach it through some indirection (e.g. a thin wrapper, or having `CatalogHistoryRefreshService` expose `RefreshManufacturedData`/`RefreshPlannedData` even though they're stock-shaped operations).
2. Inject `ICatalogManufactureSource` directly into both `CatalogHistoryRefreshService` (for `RefreshManufactureHistoryData`) and `CatalogStockRefreshService` (for `RefreshManufacturedData`/`RefreshPlannedData`).

**Chosen approach:** Option 2.

**Rationale:** `ICatalogManufactureSource` is a stateless, side-effect-free cross-module contract (already `Scoped`/injected freely elsewhere); injecting it into two classes costs nothing and is exactly what "each class only depends on what it actually calls" means. Forcing it into one class to satisfy a purity rule about "one owner per interface" would either misplace a stock-shaped method into the history class or add an unnecessary layer. The spec's FR-1 documents this exception explicitly so it isn't mistaken for an oversight during implementation or review.

#### Decision 3: Keep the four new classes as plain `Transient` services registered directly in `CatalogModule`, matching the original lifetime
**Options considered:**
1. Match `CatalogCacheStore`/`CatalogMergeService`/`BundleSalesExpander`'s `Singleton` lifetime.
2. Keep `Transient`, matching the original `CatalogDataRefreshService` registration.

**Chosen approach:** Option 2.

**Rationale:** This is a pure structural refactor (NFR-1 in the spec) — changing lifetime is a behavioral/perf-risk change with its own considerations (e.g. captive dependency risk if any injected dependency is `Scoped`) that is out of scope here and not requested by the issue. Preserving `Transient` for all four new registrations keeps the change mechanically verifiable as "same behavior, different shape."

## Implementation Guidance

### Directory / Module Structure
All four new classes go directly in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`, next to the file being deleted:
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs`

Delete: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`

Modify:
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — constructor (swap 1 field for 4), and the 19 one-line `Refresh*` delegate methods (lines 124–143) each re-pointed to the correct new service. `RefreshMarginData` (lines 145–170, implemented inline, not delegated) is untouched.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — line 96 (`services.AddTransient<CatalogDataRefreshService>();`) replaced with four `AddTransient` lines. `RegisterBackgroundRefreshTasks` (lines 166–297) is **not** touched.

Test files — modify/split:
- Delete `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs` (495 lines) after moving every test case into:
  - `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMetaRefreshServiceTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs`
- Other test files that construct a `CatalogRepository` directly (`CatalogRepositoryCacheOptimizationTests.cs`, `CatalogRepositoryStaleDataAndChangesPendingTests.cs`, `CatalogRepositoryTests.cs` under `Domain/Catalog/`, `MarginCostWindowAlignmentTests.cs`) must update their `CatalogRepository` construction call sites to pass 4 new-service mocks/instances instead of 1 `CatalogDataRefreshService` mock/instance — grep for `new CatalogRepository(` and `new CatalogDataRefreshService(` across `backend/test/` to find every call site before starting (do not rely on the list above being exhaustive; verify it against the actual repo state at implementation time, since architect exploration is a point-in-time read).

### Interfaces and Contracts
No new or changed interfaces. All four new classes are concrete `sealed class`, exactly like the original — no new abstraction (`ICatalogHistoryRefreshService` etc.) is introduced, because nothing external needs to substitute them; only `CatalogRepository` constructs/holds them directly, matching how `CatalogDataRefreshService` was held today (a concrete class, no interface). Do not add interfaces speculatively — YAGNI, and it would be scope creep beyond what the issue asks for.

Exact method-to-class assignment (authoritative — supersedes the issue body's grouping):

| Method | New class |
|---|---|
| `RefreshSalesData` | `CatalogHistoryRefreshService` |
| `RefreshSetPartsData` (+ private `FetchSetPartsPerBundleAsync`) | `CatalogHistoryRefreshService` |
| `RefreshPurchaseHistoryData` | `CatalogHistoryRefreshService` |
| `RefreshConsumedHistoryData` | `CatalogHistoryRefreshService` |
| `RefreshManufactureHistoryData` | `CatalogHistoryRefreshService` |
| `RefreshErpStockData` | `CatalogStockRefreshService` |
| `RefreshEshopStockData` | `CatalogStockRefreshService` |
| `RefreshTransportData` | `CatalogStockRefreshService` |
| `RefreshReserveData` | `CatalogStockRefreshService` |
| `RefreshOrderedData` | `CatalogStockRefreshService` |
| `RefreshManufacturedData` | `CatalogStockRefreshService` |
| `RefreshPlannedData` | `CatalogStockRefreshService` |
| `RefreshAttributesData` | `CatalogMetaRefreshService` |
| `RefreshLotsData` | `CatalogMetaRefreshService` |
| `RefreshEshopPricesData` | `CatalogMetaRefreshService` |
| `RefreshErpPricesData` | `CatalogMetaRefreshService` |
| `RefreshEshopUrlData` | `CatalogMetaRefreshService` |
| `RefreshStockTakingData` | `CatalogReferenceRefreshService` |
| `RefreshManufactureDifficultySettingsData` | `CatalogReferenceRefreshService` |
| `RefreshManufactureCostData` | `CatalogReferenceRefreshService` |

### Data Flow
Unaffected. Each `Refresh*` method's data flow (external client / cross-module source / repository → optional resilience wrapper → `CatalogCacheStore.Set*Data`) is copied verbatim; only the class that owns the method call changes. `CatalogRepository.GetCatalogDataAsync`'s cache-valid/stale/merge logic, `CatalogMergeService.ExecutePriorityMergeAsync`, and `ICatalogMergeScheduler` are all downstream readers of `CatalogCacheStore` and never referenced `CatalogDataRefreshService` — they are entirely unaffected.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A test file outside `CatalogDataRefreshServiceTests.cs` constructs `CatalogRepository` or `CatalogDataRefreshService` directly and is missed, breaking the build | Medium | Grep for both type names across `backend/test/` before starting (see Implementation Guidance); `dotnet build` on the test project will fail loudly and immediately if any call site is missed — this is a compile-time-caught risk, not a silent one |
| `RefreshManufactureCostData` is dead in production wiring (not called by `RegisterBackgroundRefreshTasks` or `CatalogRepository`) and its home is a judgment call | Low | Documented explicitly in the spec (FR-1, `CatalogReferenceRefreshService`) and in this review's method table; preserved verbatim, not removed — removing dead code is out of scope for an SRP-only refactor |
| Moving `ICatalogManufactureSource` usage into two classes could be misread by a future maintainer as "should be split" | Low | Spec FR-1 and this review's Decision 2 both call out explicitly that the shared interface is an intentional exception, not an oversight |
| DI registration order/lifetime mismatch if a new class is accidentally registered `Singleton` while depending on a `Scoped`/`Transient` collaborator | Low | All four new classes only depend on already-`Singleton`-or-stateless collaborators (API clients are typically `Transient`/`Scoped` HTTP clients, which is exactly the same shape the original `Transient` `CatalogDataRefreshService` already had) — keeping `Transient` (Decision 3) avoids introducing a new captive-dependency class of bug |

## Specification Amendments
None needed — the spec (as re-derived from actual code, not the issue's literal text) already matches this architecture. Two clarifications worth calling out for the planner:
1. The method-to-class table in this review's "Interfaces and Contracts" section is authoritative over the issue body's grouping language; the spec's FR-1 already reflects it.
2. The planner should size tasks per new class (4 implementation tasks + 1 `CatalogRepository`/`CatalogModule` wiring task + 1 test-migration task is a reasonable shape) rather than per original method, since methods within one class share constructor wiring and are cheapest to move together.

## Prerequisites
None. No migrations, no config, no infrastructure changes. This can start immediately — it only touches files already in the repository.
