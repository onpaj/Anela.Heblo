I have enough context to write the architecture review. Two key findings drive my recommendations: the precedent (`LogisticsStockOperationQueryAdapter`) is structurally identical to what the spec proposes, and the spec's FR-7 empty-allowlist requirement conflicts with the still-existing leaks in `ProductPairingDqtComparer`.

```markdown
# Architecture Review: Decouple `StockWriteBackDqtComparer` from Catalog repository interfaces

## Skip Design: true

Pure backend refactor — no new or changed UI, no API surface change, no visual components.

## Architectural Fit Assessment

The spec is a textbook application of the **consumer-owned contract / provider-side adapter** pattern that is already canonized in `docs/architecture/development_guidelines.md` (§ "Cross-Module Communication Example: ILeafletKnowledgeSource") and demonstrated in this codebase by:

- `ILogisticsStockOperationQueryService` (Logistics-owned contract)
- `LogisticsStockOperationQueryAdapter` (Catalog-Infrastructure adapter, `internal sealed`)
- Registration in `CatalogModule.AddCatalogModule` (line 53)
- Architecture test coverage in `ModuleBoundariesTests.cs` (`CatalogLogisticsAllowlist`)

The new DataQuality contracts (`IStockOperationQuery`, `IStockTakingQuery`) and the two adapters slot into the existing structure with **zero** new architectural concepts. Integration points are local: a single consumer (`StockWriteBackDqtComparer`), two adapters, one module file edit, and one architecture-test row.

Risk profile: **low**. Behavior-preserving refactor, no schema change, no public surface change. The only architectural risk is the spec's FR-7 contradicting itself — addressed in *Specification Amendments* below.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────── DataQuality module ────────────────────────────┐
│                                                                            │
│  Application/Features/DataQuality/                                         │
│  ├── Contracts/                                                            │
│  │   ├── IStockOperationQuery                  (NEW — read-only contract)  │
│  │   ├── IStockTakingQuery                     (NEW — read-only contract)  │
│  │   ├── StockOperationSnapshot                (NEW — DTO class, immutable)│
│  │   ├── StockOperationStateSnapshot           (NEW — enum)                │
│  │   └── StockTakingSnapshot                   (NEW — DTO class, immutable)│
│  └── Services/                                                             │
│      └── StockWriteBackDqtComparer             (MODIFIED — depends only    │
│                                                  on the two contracts)     │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
                ▲                                ▲
                │  IStockOperationQuery          │  IStockTakingQuery
                │                                │
┌───────────────┴────────────────── Catalog module ──┴───────────────────────┐
│                                                                            │
│  Application/Features/Catalog/Infrastructure/                              │
│  ├── DataQualityStockOperationQueryAdapter      (NEW — internal sealed)    │
│  │   └── wraps IStockUpOperationRepository                                 │
│  └── DataQualityStockTakingQueryAdapter         (NEW — internal sealed)    │
│      └── wraps IStockTakingRepository                                      │
│                                                                            │
│  CatalogModule.cs                              (MODIFIED — 2 new bindings) │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────── Test boundary ─────────────────────────────────┐
│  test/Architecture/ModuleBoundariesTests.cs    (MODIFIED — new rule       │
│      DataQuality -> Catalog, with ProductPairingDqtComparer allowlist)    │
│  test/Features/DataQuality/StockWriteBackDqtComparerTests.cs (MODIFIED)   │
│  test/Features/Catalog/Infrastructure/                                    │
│      DataQualityStockOperationQueryAdapterTests.cs    (NEW — mapping)     │
│      DataQualityStockTakingQueryAdapterTests.cs       (NEW — mapping)     │
└───────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Two contracts, not one combined `IStockWriteBackQuery`

**Options considered:**
- (A) Two narrow contracts: `IStockOperationQuery` + `IStockTakingQuery` (spec proposal)
- (B) One combined `IStockWriteBackReadQuery` returning both lists

**Chosen approach:** (A), as proposed in the spec.

**Rationale:** Each contract maps cleanly to one Catalog repository and has one responsibility (ISP). Combining them would couple two independent read paths and force any future consumer to take both, even when only one is needed. Two interfaces are equally cheap given there is exactly one consumer today.

#### Decision 2: Adapters live in `Catalog.Infrastructure`, registered in `CatalogModule`

**Options considered:**
- (A) Adapters in `Catalog.Infrastructure`, registered in `CatalogModule` (spec proposal — matches `LogisticsStockOperationQueryAdapter` precedent)
- (B) Adapters in `DataQuality.Infrastructure`, registered in `DataQualityModule`
- (C) Adapters in a shared infrastructure project

**Chosen approach:** (A).

**Rationale:** `development_guidelines.md` is explicit: "**Provider (B) registers the DI binding.** Module B's `{Module}.cs` registers `services.AddScoped<IConsumerContract, ProviderAdapter>();`." The Catalog module owns the underlying repositories and is the natural provider. (B) would force DataQuality to import Catalog domain types in the adapter, defeating the entire decoupling. (C) introduces a new project without need.

#### Decision 3: DataQuality-owned `StockOperationStateSnapshot` enum, not reuse `StockUpOperationState`

**Options considered:**
- (A) Define a parallel enum in DataQuality.Contracts and map in the adapter (spec proposal)
- (B) Re-export the Catalog enum via a type alias or expose its int value
- (C) Use only `string State` in the DTO

**Chosen approach:** (A).

**Rationale:** Exposing the Catalog enum from a DataQuality contract leaks the Catalog namespace — the architecture test (FR-7) would still flag it. Using `string` would be type-unsafe for the three state checks the comparer performs. The mirrored-enum precedent (`LogisticsStockOperationState` ↔ `StockUpOperationState`) is already established in this codebase.

#### Decision 4: DTOs as `class` with init-only members, not `record`

**Options considered:**
- (A) `public class StockOperationSnapshot { public required string ProductCode { get; init; } … }` (spec proposal)
- (B) `public sealed record StockOperationSnapshot(string ProductCode, …)`

**Chosen approach:** (A).

**Rationale:** Project-wide rule in `CLAUDE.md`: "DTOs are classes, never C# records." The rule exists because the OpenAPI generator mishandles record parameter order. Although these contracts are **internal** (no OpenAPI exposure), the consistency rule still applies, and switching to `record` would require justifying the deviation in code review.

#### Decision 5: Push date filter into the adapter, materialize once

**Options considered:**
- (A) Adapter applies `.Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc).Select(...).ToListAsync(ct)` (spec NFR-1)
- (B) Adapter returns `IQueryable<StockOperationSnapshot>` and lets the comparer filter

**Chosen approach:** (A).

**Rationale:** (B) leaks `IQueryable` into the DataQuality contract, which transitively binds DataQuality to the EF query provider — defeating the future-microservice goal. (A) keeps the contract synchronous-list-shaped, pushes the filter to SQL, and is identical in cost to today's behavior.

#### Decision 6: Adapter DI lifetime — `Scoped` (matching the underlying repos)

The spec proposes `Scoped`. The codebase precedent `LogisticsStockOperationQueryAdapter` is registered as `Transient`. Both work because adapter instances are stateless and the underlying repos are `Scoped` (resolved per request). Recommend **`Scoped`** as the spec states — it documents intent and avoids the cognitive cost of "why is the adapter Transient when the repo is Scoped." This is a minor divergence from the prior adapter; either is correct.

## Implementation Guidance

### Directory / Module Structure

**New files (DataQuality-owned):**
```
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/
├── IStockOperationQuery.cs              (FR-1)
├── IStockTakingQuery.cs                 (FR-2)
├── StockOperationSnapshot.cs            (FR-1)
├── StockOperationStateSnapshot.cs       (FR-1)
└── StockTakingSnapshot.cs               (FR-2)
```

**New files (Catalog-owned):**
```
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/
├── DataQualityStockOperationQueryAdapter.cs   (FR-3, internal sealed)
└── DataQualityStockTakingQueryAdapter.cs      (FR-3, internal sealed)
```

**Modified files:**
```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs        (FR-4)
backend/src/Anela.Heblo.Application/Features/DataQuality/Services/
    StockWriteBackDqtComparer.cs                                              (FR-5)
backend/test/Anela.Heblo.Tests/Features/DataQuality/
    StockWriteBackDqtComparerTests.cs                                         (FR-6)
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs          (FR-7)
```

**New test files:**
```
backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
    DataQualityStockOperationQueryAdapterTests.cs
    DataQualityStockTakingQueryAdapterTests.cs
```

Place them under `Features/Catalog/Infrastructure/` (mirroring source), not under `Features/DataQuality/`, because the adapters belong to Catalog.

### Interfaces and Contracts

**Strict naming/shape rules to follow:**

1. **`IStockOperationQuery` and `IStockTakingQuery`** must be `public interface` (consumed by Catalog at DI time and by tests via mocks).
2. **`StockOperationSnapshot`** and **`StockTakingSnapshot`** must be `public sealed class` with `required init` members. Example shape:
   ```csharp
   public sealed class StockOperationSnapshot
   {
       public required string ProductCode { get; init; }
       public required int Amount { get; init; }
       public required string DocumentNumber { get; init; }
       public required StockOperationStateSnapshot State { get; init; }
       public required DateTime CreatedAtUtc { get; init; }
       public string? ErrorMessage { get; init; }
   }
   ```
3. **`StockOperationStateSnapshot`** — values `Pending = 0, Submitted = 1, Completed = 2, Failed = 3`. Match the integer values of `StockUpOperationState` so any persisted projections remain comparable; the architecture test will not see this because the enum is DataQuality-owned.
4. **Adapter classes** must be `internal sealed` (matching `LogisticsStockOperationQueryAdapter`) and live in the `Anela.Heblo.Application.Features.Catalog.Infrastructure` namespace.
5. **Adapters must not expose any Catalog domain type via the contract surface** — all Catalog types stay inside the adapter implementation.

### Data Flow

```
StockWriteBackDqtJob (existing, unchanged)
        │
        ▼
DriftDqtJobRunner.RunAsync (existing)
        │
        ▼
StockWriteBackDqtComparer.CompareAsync(from, to, ct)   ◄── MODIFIED
        │
        ├──► IStockOperationQuery.GetByCreatedDateRangeAsync(fromUtc, toUtc, ct)
        │        │
        │        ▼   (DataQualityStockOperationQueryAdapter — Catalog.Infrastructure)
        │    IStockUpOperationRepository.GetAll()
        │        .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
        │        .Select(o => new StockOperationSnapshot { … })
        │        .ToListAsync(ct)
        │
        └──► IStockTakingQuery.GetByDateRangeAsync(fromUtc, toUtc, ct)
                 │
                 ▼   (DataQualityStockTakingQueryAdapter — Catalog.Infrastructure)
             IStockTakingRepository.GetByDateRangeAsync(fromUtc, toUtc, ct)
                 .Select(r => new StockTakingSnapshot { … })
                 .ToList()
```

Two SQL queries per comparison (unchanged from today). Both filters execute on the database side.

### Mapping rules (adapter implementation)

`DataQualityStockOperationQueryAdapter`:
- Project inside the EF query with `Select` (so EF translates `Amount`, `ProductCode`, etc. into SELECT columns; no full entity load).
- State mapping is exhaustive: any unmatched value throws `ArgumentOutOfRangeException` (precedent: `LogisticsStockOperationQueryAdapter.MapState`).
- `CreatedAt` flows through unchanged into `CreatedAtUtc` — the Catalog field is already UTC (`DateTime.UtcNow` at construction).

`DataQualityStockTakingQueryAdapter`:
- Delegate to `GetByDateRangeAsync`, then `.Select` in-memory to `StockTakingSnapshot` (the repository already materializes a `List<StockTakingRecord>`, so in-memory projection is unavoidable without altering the repository contract — and altering the repository is explicitly out of scope).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-7 architecture test fails on existing `ProductPairingDqtComparer` Catalog references (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`) — spec says allowlist must be empty but `ProductPairingDqtComparer` is out of scope | **HIGH** | Add explicit allowlist entries for `ProductPairingDqtComparer` with follow-up comments (see *Specification Amendments*). This matches the documented pattern in `LeafletAllowlist` and `CatalogPurchaseAllowlist`. |
| EF `Select` projection inside adapter may not translate cleanly for `string?` `ErrorMessage` or enum cast | LOW | Test with the real `ApplicationDbContext` in `DataQualityStockOperationQueryAdapterTests` using SQLite in-memory, or fall back to `.AsEnumerable().Select(...)` after the `Where` (negligible perf cost — date-bounded result set) |
| Test mocks for `IStockOperationQuery` may diverge silently from real adapter behavior (e.g., null `ErrorMessage` handling) | LOW | New adapter unit tests (FR-6 last bullet) provide that coverage; one happy path + one null-ErrorMessage path is enough |
| Future consumer adds methods to `IStockOperationQuery` (scope creep) | LOW | Keep the interface XML-doc-explicit that it serves only DQT read scenarios |
| `Scoped` adapter divergence from `Transient` precedent (`LogisticsStockOperationQueryAdapter`) | LOW | Cosmetic; document the choice in the registration line or align with precedent during code review |

## Specification Amendments

### Amendment 1 (MUST — fixes self-contradiction in FR-7)

The spec states FR-7's allowlist "must be empty" while also placing `ProductPairingDqtComparer` (which references `Anela.Heblo.Domain.Features.Catalog.Stock` and `Anela.Heblo.Domain.Features.Catalog`) **out of scope**. The architecture test will fail unless those references are allowlisted.

**Amend FR-7 acceptance criteria** to read:

> A new entry in `Rules()` matches the format above. After implementation, the only allowlist entries are pre-existing `ProductPairingDqtComparer` references (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`), each with an inline comment marking it a follow-up. Specifically:
>
> ```csharp
> private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
> {
>     // Follow-up: introduce DataQuality-owned IProductPairingQuery contract and
>     // refactor ProductPairingDqtComparer to consume it. Out of scope for the
>     // 2026-06-01 StockWriteBackDqtComparer decoupling.
>     "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
>     "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
>     "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
>     "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",
> };
> ```
>
> The implementer **must** run the new rule against the assembly first and add **all** discovered violations to the allowlist (compiler-generated async state machines may surface additional entries). Empty allowlist is the goal post-`ProductPairingDqtComparer` decoupling, not now.

### Amendment 2 (SHOULD — improves consistency with precedent)

NFR-1 says "The current implementation calls `_operationRepository.GetAll()` to return an `IQueryable<StockUpOperation>` and applies the date-range filter via LINQ-to-EF." Inspection shows the current code calls `.ToList()` immediately after `.Where()`, which means EF already does the date filter in SQL. NFR-1's statement is **correct** but the wording "today the comparer also relies on EF translation" should be tightened:

> The adapter must push the date filter to SQL via `_repository.GetAll().Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc).Select(...).ToListAsync(ct)`. Performance parity with the current implementation is guaranteed by this projection; the projected DTO is narrower than the entity load today, so end-to-end memory should improve marginally.

### Amendment 3 (SHOULD — adapter test file naming)

FR-6 says "A new unit test verifies the adapter mapping". Specify the file naming and placement explicitly:

> Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapterTests.cs` and `…/DataQualityStockTakingQueryAdapterTests.cs`. One happy-path test per adapter; for the operation adapter, additionally cover `ErrorMessage = null` (regression guard for nullable projection translation).

## Prerequisites

None. No migrations, no new packages, no config, no infrastructure. The change can be implemented and merged in a single PR.

**Validation gates before declaring done (per `CLAUDE.md`):**
- `dotnet build` clean
- `dotnet format` clean
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` passes (including the new `DataQuality -> Catalog` rule)
- `dotnet test --filter "FullyQualifiedName~StockWriteBackDqtComparerTests"` passes
- `dotnet test --filter "FullyQualifiedName~DataQualityStock"` passes (new adapter tests)
- Full suite green (the bindings touch DI startup — application boot is implicitly verified by integration tests that build the host)
```