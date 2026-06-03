I have enough grounding. Writing the review now.

# Architecture Review: Decouple Manufacture from `ICatalogRepository` via Consumer-Owned Contract

## Skip Design: true

## Architectural Fit Assessment

This change is a textbook application of an already-codified pattern in this repository, not a new architectural decision. The reverse direction (`ICatalogManufactureSource` owned by Catalog, implemented by `ManufactureCatalogSourceAdapter` in Manufacture/Infrastructure, registered in `ManufactureModule.cs:59` as `Scoped`) is fully functional and tested. The proposed `IManufactureCatalogSource` + Catalog-side adapter is a mirror of it. The pattern is also explicitly normative — `docs/architecture/development_guidelines.md:195` describes "Cross-Module Communication Example: ILeafletKnowledgeSource" with the same three-step recipe (consumer defines contract → provider implements adapter → provider registers DI). `ModuleBoundariesTests.cs` is the architectural-test enforcement layer that closes the loop.

Integration points are narrow and well-bounded:
- **Consumer side (Manufacture):** 11 files, all listed in FR-3, replace one constructor parameter type each. No call-site behavior change.
- **Provider side (Catalog):** one new adapter class, one DI line in `CatalogModule.AddCatalogModule()`, no change to `CatalogRepository`, no change to background refresh tasks (they live inside Catalog and consume `ICatalogRepository` directly — correct).
- **Tests:** Architectural rule add + mock-target type swap in ~15 Manufacture test files.

The only architectural decision worth scrutinizing is the same one already accepted on the symmetric contract: the deliberate `CatalogAggregate` leak through the contract surface, allowlisted in the boundary test, with the DTO extraction deferred as a follow-up. The `CatalogManufactureAllowlist` (`ModuleBoundariesTests.cs:123-159`) sets the precedent for this exact trade-off — accept the leak now, capture the follow-up in the allowlist comment, move on. Doing this here is consistent and right.

## Proposed Architecture

### Component Overview

```
            ┌─────────────────────────────────────────┐
            │ Manufacture (consumer)                  │
            │                                         │
            │  Application/Features/Manufacture/      │
            │    Contracts/                           │
            │      IManufactureCatalogSource  ◄──┐    │
            │    Services/, UseCases/...         │    │
            │      depend on ──────────────────►─┘    │
            └─────────────────────────────────────────┘
                            ▲ implements
                            │
            ┌───────────────┴─────────────────────────┐
            │ Catalog (provider)                      │
            │                                         │
            │  Application/Features/Catalog/          │
            │    Infrastructure/                      │
            │      CatalogManufactureCatalogSource    │
            │      Adapter  ──── delegates ──►        │
            │                ICatalogRepository       │
            │    CatalogModule.AddCatalogModule       │
            │      services.AddScoped<                │
            │        IManufactureCatalogSource,       │
            │        ...Adapter>()                    │
            └─────────────────────────────────────────┘
```

Symmetric to the existing inversion at `Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` ⇆ `Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`. No new architectural surface — same shape, opposite direction.

### Key Design Decisions

#### Decision 1: Contract surface stays at three methods only
**Options considered:**
- (A) Mirror the full `IReadOnlyRepository<CatalogAggregate, string>` (six methods).
- (B) Expose only the three methods Manufacture currently calls (`GetByIdAsync`, `GetByIdsAsync`, `GetAllAsync`).
- (C) Expose three methods *and* preemptively add anticipated needs (e.g., `FindAsync`).

**Chosen approach:** B.
**Rationale:** Matches the YAGNI guidance in the consumer-owned-contract recipe ("exposing only the operations it actually consumes (no speculative methods)" — `development_guidelines.md:200`). The symmetric `ICatalogManufactureSource` exposes exactly three methods, no more. If a new Manufacture caller needs `FindAsync` later, add it then.

#### Decision 2: `CatalogAggregate` flows through the contract unchanged
**Options considered:**
- (A) Introduce a Manufacture-owned `ProductCatalogSnapshot` DTO now, with a mapper in the adapter.
- (B) Pass `CatalogAggregate` through as a pragmatic leak and allowlist it in `ModuleBoundariesTests`.

**Chosen approach:** B (spec is correct).
**Rationale:** `CatalogAggregate` is the root aggregate used by 11 services and handlers across Manufacture, often via half a dozen properties per call (Type, ProductType, Stock subfields, ManufactureHistory, etc.). A correct snapshot DTO would either (a) duplicate large portions of `CatalogAggregate` and force a mapper big enough to dwarf this refactor or (b) be incomplete and require the consumer to fall back to the full type for some calls. The exact same trade-off was accepted on `ICatalogManufactureSource` leaking `ManufactureHistoryRecord` and is the only reason `CatalogManufactureAllowlist` exists. Defer the DTO work as a tracked follow-up.

#### Decision 3: Adapter is `Scoped`, consumes `ICatalogRepository` (currently `Transient`)
**Options considered:**
- (A) `Scoped` adapter, matches symmetric `ICatalogManufactureSource` registration.
- (B) `Transient` adapter, matches the underlying repository lifetime.
- (C) Promote `ICatalogRepository` to `Scoped` while we're here.

**Chosen approach:** A (spec is correct).
**Rationale:** Mirrors `ManufactureModule.cs:59`. The lifetime mismatch (`Scoped` adapter → `Transient` repo) is harmless because `CatalogRepository` is a thin wrapper over the `Singleton` `CatalogCacheStore`; the per-resolution allocation is constant-time. Per ASP.NET Core rules, a `Scoped` service may safely depend on a `Transient` dependency — the transient is materialized into the scope's container and reused for the duration of the scope, which is what we want.  C is out of scope for this refactor.

## Implementation Guidance

### Directory / Module Structure

**New files (2):**

```
backend/src/Anela.Heblo.Application/Features/
├── Manufacture/
│   └── Contracts/
│       └── IManufactureCatalogSource.cs              # NEW (consumer-owned)
└── Catalog/
    └── Infrastructure/
        └── CatalogManufactureCatalogSourceAdapter.cs # NEW (provider-owned)
```

The adapter name is deliberately disambiguated with a `Catalog` prefix because `ManufactureCatalogSourceAdapter` is already taken by the reverse adapter in `Manufacture/Infrastructure/`. Tolerate the awkward name in exchange for grep-unique class names across the solution. (Don't rename the existing adapter — out of scope.)

**Modified files:**

- `CatalogModule.cs` — add one `AddScoped` line in the cross-module adapter block (~lines 48–57), with the comment style already used at `ManufactureModule.cs:57-58` mirrored.
- 11 Manufacture files in FR-3 — constructor parameter type swap, field rename, `using` swap.
- `ModuleBoundariesTests.cs` — new `ManufactureCatalogAllowlist` + new `Rules()` entry.
- ~15 Manufacture test files (one `Mock<ICatalogRepository>` → `Mock<IManufactureCatalogSource>` per file).

**New test file (recommended addition — see Specification Amendments):**

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs` — three trivial pass-through tests, mirroring the existing `PurchaseMaterialCatalogAdapterTests.cs` and `LogisticsCatalogSourceAdapterTests.cs` precedents.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read abstraction over Catalog products. Implemented by the Catalog
/// module via CatalogManufactureCatalogSourceAdapter. Returns CatalogAggregate as a
/// deliberate pragmatic leak — symmetric to ICatalogManufactureSource returning
/// ManufactureHistoryRecord. Allowlisted in ModuleBoundariesTests under
/// "Manufacture -> Catalog". Follow-up: introduce Manufacture-owned ProductCatalogSnapshot.
/// </summary>
public interface IManufactureCatalogSource
{
    Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids, CancellationToken cancellationToken = default);
    Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
```

**Important contract correction (see Specification Amendments §1).** `IReadOnlyRepository<,>.GetAllAsync` returns `Task<IEnumerable<TEntity>>`, not `Task<IReadOnlyList<TEntity>>` as FR-1 claims. The chosen return type is `Task<IEnumerable<CatalogAggregate>>` so that the adapter can be a literal one-line delegation and no Manufacture call site requires editing beyond the receiver rename.

**Adapter:**

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogManufactureCatalogSourceAdapter : IManufactureCatalogSource
{
    private readonly ICatalogRepository _repository;
    public CatalogManufactureCatalogSourceAdapter(ICatalogRepository repository) => _repository = repository;

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
        => _repository.GetByIdsAsync(ids, ct);

    public Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);
}
```

**DI registration** in `CatalogModule.AddCatalogModule` next to the existing cross-module adapter block:

```csharp
// Cross-module contract: Catalog implements Manufacture's IManufactureCatalogSource via adapter.
// DI registration is owned by the provider (Catalog), not the consumer (Manufacture).
services.AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>();
```

### Data Flow

Unchanged. For every method, the request path is:

```
Manufacture handler/service
  → IManufactureCatalogSource (DI-resolved → adapter)
  → ICatalogRepository (constructor capture)
  → CatalogCacheStore.GetCatalogData() / cached snapshot
  → returns CatalogAggregate / dictionary / enumerable
```

Compared to today, one method-call frame is added per call. No allocation beyond the per-scope adapter instance; the JIT inlines through the single-field forwarding methods under release optimization.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Contract return-type mismatch (FR-1 declares `IReadOnlyList<CatalogAggregate>` but `IReadOnlyRepository.GetAllAsync` returns `IEnumerable`). Adapter would need `.ToList()`, causing an extra allocation per call (NFR-1 violation) and breaking pass-through symmetry. | High | Amend FR-1 to `Task<IEnumerable<CatalogAggregate>>`. Confirmed call sites (e.g. `GetManufacturingStockAnalysisHandler.cs:53-56` does `.Where().ToList()`) require no change either way. |
| Allowlist incomplete on first run — `IManufactureCatalogSource` itself surfaces `CatalogAggregate` in three method signatures, so the contract type is itself a `Manufacture -> Catalog` violation that must be allowlisted. Spec mentions "consumer of `IManufactureCatalogSource`" but not the contract type itself. | Medium | Implementation workflow: add the boundary rule with an empty allowlist first, run the test, copy the printed violations verbatim into the allowlist with a single shared comment header. The reflection walker in `EnumerateReferencedTypes` will enumerate the contract's method return types and parameters, so the contract appears in the violation list. The first entry will be `Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`. |
| `ProductType` enum and other catalog domain types are referenced by Manufacture handlers (e.g. `GetManufacturingStockAnalysisHandler.cs:55`: `item.Type == ProductType.Product`). These remain after migration and the new boundary rule will flag them. | Medium | Same workflow — pasted into allowlist. Group these under one comment block: "Domain types reached via CatalogAggregate properties (pragmatic leak); follow-up tracked with the IManufactureCatalogSource DTO extraction." |
| Tests for the adapter itself are not in scope per FR-5 wording, but every other Catalog cross-module adapter has a dedicated test file. Skipping this creates an inconsistency. | Low | Add `CatalogManufactureCatalogSourceAdapterTests.cs` with three pass-through tests. Mirrors the existing precedent and adds ~20 LOC. |
| Name collision with `ManufactureCatalogSourceAdapter` (reverse direction). Solution-wide grep for "ManufactureCatalogSourceAdapter" matches both. | Low | Accept the disambiguating `Catalog` prefix (`CatalogManufactureCatalogSourceAdapter`). Document in adapter XML comment which direction it serves. |
| Brief listed mappers/calculators (`ManufactureAnalysisMapper`, `ConsumptionRateCalculator`, etc.) as containing `ICatalogRepository` references; current grep shows zero. Risk: implementer trusts the brief and breaks something that doesn't exist. | Low | Spec already calls this out and instructs a fresh grep at implementation time. Honor that — the file list in FR-3 is authoritative as of `grep -l "ICatalogRepository" backend/src/Anela.Heblo.Application/Features/Manufacture/`. |

## Specification Amendments

1. **FR-1 — fix `GetAllAsync` return type.** Change the contract from `Task<IReadOnlyList<CatalogAggregate>>` to `Task<IEnumerable<CatalogAggregate>>`. The justification text in the spec ("signatures match `IReadOnlyRepository.GetAllAsync`'s existing return type and avoid changing call-site code") is **correct in spirit but wrong about the type** — `IReadOnlyRepository<CatalogAggregate, string>.GetAllAsync` actually returns `Task<IEnumerable<CatalogAggregate>>` (verified in `backend/src/Anela.Heblo.Xcc/Persistance/IReadOnlyRepository.cs:11`). Keeping `IEnumerable` makes the adapter a literal pass-through and preserves NFR-1 by avoiding a `.ToList()` allocation.

2. **FR-2 — clarify allowlist must include the contract type itself.** Add an explicit acceptance criterion: "`Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate` must appear in `ManufactureCatalogAllowlist` (in addition to consumer entries) — the contract surface is the primary point at which `CatalogAggregate` enters Manufacture's namespace."

3. **FR-5 — add adapter unit test (Low severity, but high consistency value).** Add a new test file `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs` with three pass-through tests (one per method) verifying that the adapter returns exactly what the mocked `ICatalogRepository` returns and forwards the `CancellationToken`. Mirrors `PurchaseMaterialCatalogAdapterTests.cs` and `LogisticsCatalogSourceAdapterTests.cs` precedents.

4. **Add explicit ordering guidance to the implementation plan.** The "fail boundary test, paste violations, migrate, re-run" workflow needs to happen in this order to catch any reference the static walker may surface that handler-by-handler migration would miss:
   1. Add `IManufactureCatalogSource`.
   2. Add `CatalogManufactureCatalogSourceAdapter` + DI registration.
   3. Add the `Manufacture -> Catalog` rule with an **empty** allowlist.
   4. Run `dotnet test --filter ModuleBoundariesTests` — read violations.
   5. Migrate the 11 files (per FR-3) and the ~15 test files (per FR-5).
   6. Re-run the test — paste the residual violations into `ManufactureCatalogAllowlist`, grouped by `CatalogAggregate` / `ProductType` / other domain types, each with a one-line `//` comment.
   7. Final `dotnet build` + `dotnet test --filter "Manufacture|ModuleBoundariesTests"` green.

5. **Documentation cross-reference.** Add the new pattern instance to `docs/architecture/development_guidelines.md` Section "Cross-Module Communication Example: ILeafletKnowledgeSource" as a second canonical example, or as a brief "see also" note pointing at `IManufactureCatalogSource`. This makes the pattern self-documenting for the next refactor (likely the three `CatalogManufactureAllowlist` `IManufactureClient` follow-ups). Out of scope for this PR if the spec author prefers — but trivially small and reduces future drift.

## Prerequisites

None blocking. All dependencies exist in the worktree today:

- `ICatalogRepository`, `CatalogAggregate`, `IReadOnlyRepository<,>` — unchanged.
- `CatalogModule.AddCatalogModule(IServiceCollection, IConfiguration)` — unchanged signature, additive registration.
- `ModuleBoundariesTests` infrastructure (`ModuleBoundaryRule`, `Rules()` `TheoryData`, reflection walker) — already supports adding a new rule.
- xUnit + Moq + FluentAssertions — already present in `Anela.Heblo.Tests`.
- No new NuGet packages, no DB migration, no config, no infrastructure change.

Implementation can begin immediately.