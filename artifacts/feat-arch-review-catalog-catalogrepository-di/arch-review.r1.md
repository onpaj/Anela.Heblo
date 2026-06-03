# Architecture Review: Decouple `CatalogRepository` from Logistics, Purchase, and Manufacture Modules

## Skip Design: true

Backend-only refactor. No new UI components, no visual or layout decisions, no API contract surface changes. UX/design work is not applicable.

## Architectural Fit Assessment

The proposal aligns cleanly with the established consumer-owned contract / provider-owned adapter pattern already in this codebase. Two existing exemplars validate the shape:

- **`ILeafletKnowledgeSource`** (`Application/Features/Leaflet/Contracts/`) + `KnowledgeBaseLeafletSourceAdapter` (`Application/Features/KnowledgeBase/Infrastructure/`) — registered in `KnowledgeBaseModule.cs:39` with the documented "Cross-module contract" comment.
- **`IInventoryReservationService`** (Logistics-owned) + `ManufactureInventoryReservationAdapter` — same pattern (`ManufactureModule.cs:54`).

The boundary rule is enforced by `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` for several module pairs (Leaflet→KnowledgeBase, Article→KnowledgeBase, Logistics→Manufacture, etc.) — but **there is currently no rule for `Catalog → Logistics/Manufacture/Purchase`**. Without adding one, the refactor is a one-time cleanup with no regression guard.

Integration points:
1. Three new contracts in `Features/Catalog/Contracts/`.
2. Three new adapters, each in the respective provider's `Features/<Module>/Infrastructure/`.
3. Three new DI lines (one per provider module).
4. `CatalogRepository` constructor shrinks by 3 (−6 provider interfaces + 3 contracts; the dead `IManufactureClient` accounts for one of the six).
5. New `Catalog → {Logistics, Manufacture, Purchase}` rule in `ModuleBoundariesTests`.

The one residual leak — `ManufactureHistoryRecord` (a `Domain.Features.Manufacture` type) appearing in `ICatalogManufactureSource.GetManufactureHistoryAsync` and in Catalog's `CachedManufactureHistoryData` field — is a deliberate pragmatic exception consistent with FR-1/FR-7 of the spec. It must be allowlisted in the new boundary rule and tracked as a follow-up.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────── Catalog (Consumer) ──────────────────────────┐
│                                                                          │
│   CatalogRepository                                                      │
│     ├─ ICatalogTransportSource       (Catalog/Contracts/)                │
│     ├─ ICatalogPurchaseSource        (Catalog/Contracts/)                │
│     └─ ICatalogManufactureSource     (Catalog/Contracts/)                │
│                                                                          │
└─────────▲────────────────────────▲────────────────────────▲──────────────┘
          │ adapter binding        │ adapter binding         │ adapter binding
          │ (provider DI)          │ (provider DI)           │ (provider DI)
┌─────────┴──────────┐   ┌─────────┴──────────┐    ┌─────────┴──────────┐
│ Logistics          │   │ Purchase           │    │ Manufacture        │
│  Infrastructure/   │   │  Infrastructure/   │    │  Infrastructure/   │
│   LogisticsCatalog │   │   PurchaseCatalog  │    │   ManufactureCatalog│
│   TransportSource  │   │   SourceAdapter    │    │   SourceAdapter    │
│   Adapter          │   │                    │    │                    │
│      │             │   │      │             │    │      │             │
│      ▼             │   │      ▼             │    │      ▼             │
│ ITransportBoxRepo  │   │ IPurchaseOrderRepo │    │ IManufactureOrder  │
│ (Domain.Logistics) │   │ (Domain.Purchase)  │    │  Repo + History    │
│                    │   │                    │    │  Client + Inventory│
│                    │   │                    │    │  Repo              │
└────────────────────┘   └────────────────────┘    └────────────────────┘
```

Dependency direction: Consumer (Catalog) owns the contract types in its own namespace. Provider modules **reference** Catalog contracts to implement adapters — but the reverse dependency that exists today (Catalog → provider Domain interfaces) is severed.

### Key Design Decisions

#### Decision 1: Single grouped source per provider vs. per-method micro-contract
**Options considered:**
- (A) One coarse contract per provider (`ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`) — spec's proposal.
- (B) Six fine-grained contracts (one per method) for maximum interface segregation.

**Chosen approach:** (A).

**Rationale:** Matches the precedent (`ILeafletKnowledgeSource` aggregates one provider's read surface; `IInventoryReservationService` groups consume+restore together). Per-method contracts would add five files for no consumer benefit (`CatalogRepository` is the only caller of all of them). Coarse-but-minimal contracts keep the seams visible without proliferation. Each contract surface remains tiny (1–3 methods).

#### Decision 2: Where the transport-box aggregation lives
**Options considered:**
- (A) Adapter owns the `SelectMany → GroupBy → ToDictionary` reshape (spec's choice).
- (B) Move the aggregation onto `ITransportBoxRepository` as a new method, adapter just delegates.

**Chosen approach:** (A).

**Rationale:** (B) leaks Catalog's projection shape into the Logistics domain interface — same coupling problem in a different direction. Putting the reshape in the adapter is the whole point of the adapter pattern: it's the **only** place that knows both shapes (Logistics' `TransportBox` and Catalog's `Dictionary<string, int>`).

#### Decision 3: Lifetime of new DI bindings
**Options considered:**
- (A) `AddScoped` uniformly.
- (B) Match each underlying repository's lifetime.

**Chosen approach:** (A) with verification per provider.

**Rationale:** `ITransportBoxRepository` is `AddScoped` (`LogisticsModule.cs:18`); `IManufactureOrderRepository` and `IManufacturedProductInventoryRepository` are `AddScoped` (`ManufactureModule.cs:49-50`). `IPurchaseOrderRepository` is registered in PersistenceModule, not `PurchaseModule.cs` — verify its lifetime during implementation; `AddScoped` is the safe default and matches the rest. `IManufactureHistoryClient` lifetime should be confirmed too (likely Scoped via Flexi adapter registration).

#### Decision 4: Preserve `ManufactureHistoryRecord` as a contract type
**Options considered:**
- (A) Reuse `Domain.Features.Manufacture.ManufactureHistoryRecord` in the contract (spec's choice).
- (B) Define a Catalog-owned `CatalogManufactureHistoryRecord` DTO and map in the adapter.

**Chosen approach:** (A) for this refactor; flag (B) as a follow-up.

**Rationale:** `CachedManufactureHistoryData` (line 692), the public property `ManufactureHistoryLoadDate` semantics, and downstream `CatalogAggregate.ManufactureHistory` already traffic in `ManufactureHistoryRecord` instances throughout Catalog (line 265). Migrating the type ripples beyond the file in scope. (B) is the right end state and should be tracked as a follow-up; allowlist the leak in `ModuleBoundariesTests` with that follow-up referenced in the comment.

#### Decision 5: Enforce the new boundary in `ModuleBoundariesTests`
**Chosen approach:** Add a `Catalog -> Logistics`, `Catalog -> Manufacture`, and `Catalog -> Purchase` rule trio (three new `ModuleBoundaryRule` entries). The Manufacture rule's allowlist gets one entry for `ManufactureHistoryRecord`; the other two have empty allowlists.

**Rationale:** Without this, the work is one-shot cleanup with no regression guard. Existing tests already prove the harness handles this exact shape — adding rules is mechanical, ~30 lines.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/
├── Catalog/
│   ├── Contracts/
│   │   ├── ICatalogTransportSource.cs            ← NEW
│   │   ├── ICatalogPurchaseSource.cs             ← NEW
│   │   └── ICatalogManufactureSource.cs         ← NEW
│   └── CatalogRepository.cs                      ← MODIFIED (constructor, helpers, usings)
├── Logistics/
│   ├── Infrastructure/                            ← NEW FOLDER
│   │   └── LogisticsCatalogTransportSourceAdapter.cs   ← NEW
│   └── LogisticsModule.cs                        ← MODIFIED (1 line)
├── Purchase/
│   ├── Infrastructure/                            ← exists (only Jobs/ subfolder today)
│   │   └── PurchaseCatalogSourceAdapter.cs       ← NEW
│   └── PurchaseModule.cs                         ← MODIFIED (1 line)
└── Manufacture/
    ├── Infrastructure/                            ← exists
    │   └── ManufactureCatalogSourceAdapter.cs    ← NEW
    └── ManufactureModule.cs                      ← MODIFIED (1 line)

backend/test/Anela.Heblo.Tests/
├── Architecture/
│   └── ModuleBoundariesTests.cs                  ← MODIFIED (3 new rules)
├── Domain/Catalog/CatalogRepositoryTests.cs      ← MODIFIED (ctor wiring)
└── Features/Catalog/
    ├── CatalogRepositoryCacheOptimizationTests.cs   ← MODIFIED
    └── CatalogRepositoryDebugTest.cs               ← MODIFIED
+ NEW adapter unit tests under each provider's test folder
```

### Interfaces and Contracts

All three live in `namespace Anela.Heblo.Application.Features.Catalog.Contracts`.

```csharp
public interface ICatalogTransportSource
{
    Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken);
}

public interface ICatalogPurchaseSource
{
    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken);
}

public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    // Returns Domain.Features.Manufacture.ManufactureHistoryRecord — deliberate pragmatic
    // leak; allowlisted in ModuleBoundariesTests. Track full decoupling as follow-up.
    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
```

Adapters: each is `internal sealed`, constructor-injects only the provider-side dependencies it needs, delegates one-line per method. The Logistics adapter also owns the three predicates' aggregation (the `SelectMany/GroupBy/Sum` block currently at lines 892–914). The Manufacture history adapter converts `IManufactureHistoryClient.GetHistoryAsync`'s `Task<List<...>>` to `IReadOnlyList<...>` (one cast or `.AsReadOnly()`; no copy needed — `List<T>` already satisfies `IReadOnlyList<T>`).

### Data Flow

Cache refresh cycle (example: planned manufacture quantities):

```
ICatalogMergeScheduler triggers
  → CatalogRepository.RefreshPlannedData(ct)
    → _manufactureSource.GetPlannedQuantitiesAsync(ct)
      → ManufactureCatalogSourceAdapter.GetPlannedQuantitiesAsync(ct)
        → _manufactureOrderRepository.GetPlannedQuantitiesAsync(ct)   [unchanged]
    ← Dictionary<string, decimal>
  → CachedPlannedData = result
```

Identical shape for transport, purchase, and inventory paths. Only difference: the Logistics transport adapter performs the aggregation that today sits in `CatalogRepository.GetProductsInTransport/Reserve/Quarantine` (lines 892–914). Public method semantics, cache keys, and `ExecuteBackgroundMergeAsync` merging are untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Aggregation behavior changes when relocated to adapter (different sum cast, predicate behavior, includeDetails flag) | High | Move code verbatim — keep `(int)s.Amount` cast, `includeDetails: true`, same three static predicates. Add an adapter unit test that asserts byte-identical output against a synthetic `TransportBox` fixture (golden test). |
| Spec's FR-5 acceptance criterion "No reference to `IManufactureClient` remains in the Catalog feature folder" is **false as written** — three other Catalog handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) inject `IManufactureClient` directly. | High | Amend the criterion: scope it to `CatalogRepository.cs` only. The handlers' coupling is a separate, larger boundary violation; mention it as a follow-up. (See Specification Amendments.) |
| New `Catalog → Logistics/Manufacture/Purchase` boundary tests catch the `ManufactureHistoryRecord` leak and fail. | Medium | Pre-populate the new rule's allowlist with the single `ManufactureHistoryRecord` entry, with a comment referencing the follow-up to introduce a Catalog-owned record type. |
| DI lifetime mismatch on adapter vs. underlying repository causes captive-dependency bugs (esp. Purchase, where repo registration is in PersistenceModule). | Medium | Verify each underlying repo's lifetime before registering. `AddScoped` matches what's been confirmed for Logistics/Manufacture; verify Purchase. Add a one-line code comment if any lifetime deviates. |
| Hidden behavior in the dead `IManufactureClient` assignment (`_manufactureClient ?? throw …`, line 103) — removing the null check is observationally inert only if no caller depends on the throw timing. | Low | The null-check is the only "use" — it's pure defensive code with no observable side effect outside `ArgumentNullException` for an unused field. Safe to drop. |
| Test files still reference removed constructor parameters → compile failures. | Low | Update all three CatalogRepository test files in the same commit; `dotnet build` catches missed sites. |
| Provider-side `Features/<X>/Infrastructure/` folder doesn't exist (Logistics has none today; Purchase has only `Infrastructure/Jobs/`). | Low | Create the folders — matches the documented filesystem convention. |
| `Manufacture` namespace `using` directive remains in `CatalogRepository.cs` for `ManufactureHistoryRecord` — may confuse readers. | Low | Annotate with a comment: `// kept only for ManufactureHistoryRecord return-type allowance; see contract` and reference the follow-up. |

## Specification Amendments

1. **FR-5 acceptance criterion correction.** The statement *"No reference to `IManufactureClient` remains in the Catalog feature folder"* is incorrect. `IManufactureClient` is also injected by:
   - `Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs:10`
   - `Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs:9`
   - `Features/Catalog/UseCases/GetProductUsage/GetProductUsageHandler.cs:10`

   Rewrite as: *"No reference to `IManufactureClient` remains in `CatalogRepository.cs`. Other Catalog handlers still consume `IManufactureClient` and represent a separate, larger boundary violation — track as a follow-up; this work does not modify them."*

2. **Add FR-10: Add architecture boundary tests.** Add three new rules to `ModuleBoundariesTests.cs::Rules()`:
   - `Catalog -> Logistics` (forbidden prefixes: `Anela.Heblo.Domain.Features.Logistics`, `Anela.Heblo.Application.Features.Logistics`, `Anela.Heblo.Persistence.Logistics`) — empty allowlist.
   - `Catalog -> Purchase` (forbidden: `…Domain.Features.Purchase`, `…Application.Features.Purchase`, `…Persistence.Purchase`) — empty allowlist.
   - `Catalog -> Manufacture` (forbidden: `…Domain.Features.Manufacture`, `…Application.Features.Manufacture`, `…Persistence.Manufacture`) — **non-empty** allowlist containing exactly the three known carry-overs:
     - `Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient`
     - `Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient`
     - `Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient`
     - `Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord` (plus any compiler-generated nested types — the existing `DeclaringType` allowlist check handles those automatically).
     - Likely also: references reachable through `CachedManufactureHistoryData` typing on private property / fields — confirm by running the test once before deciding final allowlist contents.
   - Each allowlist entry must include a comment with the justification (matching the existing precedent in the file).

3. **FR-7 clarification.** Add an explicit instruction: when removing the four cross-module `using` directives, the `using Anela.Heblo.Domain.Features.Manufacture;` directive **stays** (it is required by `ManufactureHistoryRecord`); annotate it with `// retained for ManufactureHistoryRecord; see ICatalogManufactureSource follow-up`.

4. **FR-9 expansion.** Add one DI smoke test that asserts each of the three new contracts resolves from `IServiceProvider` (the existing `KnowledgeBaseLeafletSourceAdapter`-style smoke tests serve as a template).

5. **Out of scope** — add explicit follow-ups list:
   - Migrating other Catalog handlers off `IManufactureClient`.
   - Introducing a Catalog-owned `CatalogManufactureHistoryRecord` DTO to remove the final `Manufacture` namespace dependency.

## Prerequisites

None. All required pieces already exist:

- `Application/Features/Catalog/Contracts/` folder exists.
- `Application/Features/Manufacture/Infrastructure/` and `Application/Features/Purchase/Infrastructure/` folders exist; only `Application/Features/Logistics/Infrastructure/` must be created.
- All six underlying provider interfaces (`ITransportBoxRepository`, `IPurchaseOrderRepository`, `IManufactureOrderRepository`, `IManufactureHistoryClient`, `IManufacturedProductInventoryRepository`, `IManufactureClient`) and their concrete implementations are already DI-registered in their respective modules' `Module.cs` files or in `PersistenceModule` — no new registrations beyond the three adapter bindings.
- `ModuleBoundariesTests` infrastructure is already in place; only new `ModuleBoundaryRule` entries needed.
- No EF migrations, no Azure Key Vault changes, no CI/CD or Docker changes, no NuGet additions.