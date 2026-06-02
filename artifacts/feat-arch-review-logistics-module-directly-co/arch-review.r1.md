I have enough context. Now writing the architecture review.

```markdown
# Architecture Review: Decouple Logistics Module From Catalog-Owned Interfaces (TransportBoxCompletionService residue)

## Skip Design: true

Backend-only refactor: no UI/UX work, no new HTTP endpoints, no schema migration. All changes are internal type relocations and DI wiring.

## Architectural Fit Assessment

The spec is a textbook application of the **consumer-owns-the-contract** pattern already in force in this codebase, finishing PR #2201's work for the one file it missed (`TransportBoxCompletionService`). Three signals confirm fit:

1. **Pattern precedent is identical and recent.** `ILogisticsStockOperationService` + `LogisticsStockOperationAdapter` (write side) was introduced by PR #2201 with exactly the directional shape this spec proposes for the query side. Adding a sibling read-side contract mirrors that — no new architectural primitives required.
2. **Integration points are already isolated.** `TransportBoxCompletionService` is the *only* file in the Logistics module still importing `Anela.Heblo.Domain.Features.Catalog.Stock`. The architecture test at `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already has a `Logistics -> Catalog` rule (lines 223–232) with the two known violations explicitly allowlisted (lines 83, 89); removing those two lines once the refactor lands is the natural completion.
3. **Boundary is verifiable.** The architecture test enumerates fields, properties, ctor params, method signatures, and attribute usage on every type in `Anela.Heblo.Application.Features.Logistics.*` and asserts no references to `Anela.Heblo.{Domain,Application,Persistence}.Features.Catalog.*` slip through (except via the per-rule allowlist). Regressions fail CI.

**One spec inconsistency to flag:** the existing sibling binding in `CatalogModule.cs:51` uses `AddTransient<ILogisticsStockOperationService, LogisticsStockOperationAdapter>()`. The spec's FR-3 says `AddScoped`. The adapter is stateless and delegates to a repository that is itself scoped to the request; either lifetime works, but the new binding should match the existing one (`AddTransient`) for consistency.

**One spec restatement to flag:** FR-5 describes adding a "new test `Logistics_DoesNotReferenceCatalog_ExceptAllowList`". The required infrastructure is already present — the `Logistics -> Catalog` rule (line 224) and its `LogisticsCatalogAllowlist` (lines 77–90). The actual work is to **remove** the two pre-existing allowlist entries for `IStockUpOperationRepository` and `StockUpOperation`, not to create a new test method.

## Proposed Architecture

### Component Overview

```
                           Logistics module                              │  Catalog module
                                                                         │
  TransportBoxCompletionService                                          │
        │                                                                │
        │ depends on (constructor-injected)                               │
        ▼                                                                │
  ILogisticsStockOperationQueryService  ◄────── implemented by ────────► LogisticsStockOperationQueryAdapter
  (Contracts/)                                                           │  (Catalog/Infrastructure/)
        │                                                                │       │
        │ returns IReadOnlyList<LogisticsStockOperationStatus>           │       │ delegates to
        ▼                                                                │       ▼
  LogisticsStockOperationStatus { DocumentNumber, State }                │  IStockUpOperationRepository
  LogisticsStockOperationState  { Pending, Submitted,                    │  (Catalog-internal — never crosses boundary)
                                  Completed, Failed }                    │
  (Contracts/ + Contracts/Models/)                                       │
                                                                         │
                                                                         │  DI binding registered here
                                                                         │  in CatalogModule.AddCatalogModule()
```

The Catalog side already owns one adapter for the write path (`LogisticsStockOperationAdapter`). This refactor adds a sibling adapter for the read path. Both could in principle live in one class — the spec keeps them split, which I endorse: CQRS-style separation, smaller surfaces, smaller test fixtures, and the existing write adapter stays untouched (no regression risk on the much higher-traffic `CreateOperationAsync` path).

### Key Design Decisions

#### Decision 1: Separate query interface vs. extending `ILogisticsStockOperationService`
**Options considered:**
- (A) Add `GetOperationsBySourceAsync` to the existing `ILogisticsStockOperationService`. Same adapter implements both.
- (B) Introduce a new `ILogisticsStockOperationQueryService` interface and a new adapter. (Spec's choice.)

**Chosen approach:** (B), per spec.

**Rationale:** (B) is the right call for three reasons. First, the read and write responsibilities are independent — only `TransportBoxCompletionService` needs the query side; `CreateOperationAsync` callers (gift-package and transport-box use cases) don't. Splitting keeps consumers depending on the minimum surface and prevents lazy "let me also add a query method" extensions to a contract that's deliberately a thin write-side handle. Second, it isolates the change from PR #2201's existing code — the write adapter and its tests are untouched. Third, it makes future move-to-microservice cleaner: a stock-operation **command** service and a stock-operation **status** query service may very plausibly land on different transports (e.g., command goes through a queue, query through HTTP). Keeping them separate at the interface level today costs one file and earns optionality later.

#### Decision 2: Adapter location — Catalog `Infrastructure/` co-located with existing write adapter
**Options considered:**
- (A) New file `Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs`. (Spec's preferred wording.)
- (B) Extend the existing `Catalog/Infrastructure/LogisticsStockOperationAdapter.cs` to implement both `ILogisticsStockOperationService` and `ILogisticsStockOperationQueryService`.

**Chosen approach:** (A).

**Rationale:** Aligns with Decision 1's interface split. Two small, single-responsibility adapter classes are easier to read than one class with both write and query methods, and they map 1:1 to the interfaces, so a reader doesn't have to scan a multi-purpose adapter to find what implements what. The existing `LogisticsCatalogSourceAdapter` already shows the codebase's preference for per-interface adapters.

#### Decision 3: Explicit integer values on `LogisticsStockOperationState`
**Options considered:**
- (A) `enum LogisticsStockOperationState { Pending, Submitted, Completed, Failed }` (implicit values).
- (B) `enum LogisticsStockOperationState { Pending = 0, Submitted = 1, Completed = 2, Failed = 3 }` (explicit). (Spec's choice.)

**Chosen approach:** (B), per spec, with one strengthening: the adapter's enum mapping must be an exhaustive `switch` expression that throws `ArgumentOutOfRangeException` on unmapped values.

**Rationale:** The Logistics enum mirrors `StockUpOperationState` (Catalog-owned). Today the values happen to align numerically (0..3). Explicit values fix the Logistics-side contract so a future Catalog addition (e.g., a new state `Reconciling = 4`) does not silently renumber Logistics. Combined with an exhaustive `switch` in the mapper, an unmapped Catalog state will throw at the adapter boundary — loud failure, not a silent default to `Pending` or `Failed`.

#### Decision 4: DTO shape — only `DocumentNumber` and `State`
**Options considered:**
- (A) Project the full Catalog `StockUpOperation` entity into a wide DTO with all fields.
- (B) Project only the two fields `TransportBoxCompletionService` actually reads today. (Spec's choice.)

**Chosen approach:** (B), per spec.

**Rationale:** YAGNI. Today the service reads `State` and `DocumentNumber`. Adding more fields speculatively re-introduces the same coupling problem the contract is meant to solve. Future callers can extend the DTO when they have a concrete need.

#### Decision 5: DI lifetime — match the existing sibling
**Options considered:**
- (A) `AddScoped` (spec text in FR-3).
- (B) `AddTransient` (matches the sibling `LogisticsStockOperationAdapter` registration on `CatalogModule.cs:51`).

**Chosen approach:** (B). Use `AddTransient`.

**Rationale:** The adapter holds no per-request state; the repository it delegates to is itself scoped. `AddTransient` is what the existing write-side adapter uses, and consistency in module registrations matters more than the negligible lifetime distinction here. This is an amendment to the spec — see "Specification Amendments" below.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/
├── Logistics/
│   ├── Contracts/
│   │   ├── ILogisticsStockOperationService.cs          (existing — unchanged)
│   │   ├── ILogisticsStockOperationQueryService.cs     (NEW — FR-1)
│   │   ├── LogisticsStockOperationSource.cs            (existing — unchanged)
│   │   ├── LogisticsStockOperationState.cs             (NEW — FR-1)
│   │   └── Models/
│   │       └── LogisticsStockOperationStatus.cs        (NEW — FR-1)
│   └── Services/
│       └── TransportBoxCompletionService.cs            (rewired — FR-4)
└── Catalog/
    └── Infrastructure/
        ├── LogisticsStockOperationAdapter.cs           (existing — unchanged)
        └── LogisticsStockOperationQueryAdapter.cs      (NEW — FR-2)

backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
    (add one AddTransient line — FR-3)

backend/test/Anela.Heblo.Tests/
├── Architecture/ModuleBoundariesTests.cs               (remove 2 allowlist entries — FR-5)
└── Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
    (swap mock from IStockUpOperationRepository to ILogisticsStockOperationQueryService — NFR-4)
```

### Interfaces and Contracts

**`ILogisticsStockOperationQueryService`** (Logistics-owned, query-side):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationQueryService
{
    Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

**`LogisticsStockOperationStatus`** (DTO, class per the project rule):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public class LogisticsStockOperationStatus
{
    public string DocumentNumber { get; init; } = string.Empty;
    public LogisticsStockOperationState State { get; init; }
}
```

**`LogisticsStockOperationState`** (explicit integer values):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public enum LogisticsStockOperationState
{
    Pending = 0,
    Submitted = 1,
    Completed = 2,
    Failed = 3,
}
```

**Adapter contract (Catalog-side):** `internal sealed`, ctor-injects `IStockUpOperationRepository`, uses two exhaustive `switch` expressions (source-type in, state out), throws `ArgumentOutOfRangeException` on unmapped values. The state mapper sits at the boundary; project loss only happens here.

### Data Flow

```
Background scheduler
  └─► ITransportBoxCompletionService.CompleteReceivedBoxesAsync
        └─► TransportBoxCompletionService.ProcessBoxAsync(box)
              ├─► ILogisticsStockOperationQueryService.GetOperationsBySourceAsync
              │     (LogisticsStockOperationSource.TransportBox, box.Id)
              │     ─────────────────────────────────────────────────────
              │     │ adapter boundary
              │     ▼
              │   LogisticsStockOperationQueryAdapter
              │     ├─► IStockUpOperationRepository.GetBySourceAsync
              │     │     (StockUpSourceType.TransportBox, sourceId)
              │     │     returns List<StockUpOperation>
              │     └─► project each StockUpOperation → LogisticsStockOperationStatus
              │         (StockUpOperationState → LogisticsStockOperationState via exhaustive switch)
              │     returns IReadOnlyList<LogisticsStockOperationStatus>
              │     ─────────────────────────────────────────────────────
              ├─► all Completed?  → box.ToPick(...) → repo.UpdateAsync + SaveChangesAsync
              ├─► any Failed?     → box.Error(...)  → repo.UpdateAsync + SaveChangesAsync
              └─► any Pending/Submitted? → skip (no state change)
```

No change to the persistence layer. No change to `TransportBox` domain behavior. Only the dependency between the orchestration service and the stock-operation query is inverted.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec specifies `AddScoped`, but the existing sibling uses `AddTransient`. Mismatched lifetimes can cause `InvalidOperationException` if a transient consumer captures a scoped dependency. | Medium | Use `AddTransient` (matches existing). The adapter is stateless; lifetime correctness is unaffected. Documented in Decision 5 and Specification Amendments below. |
| Test asks for a new test method `Logistics_DoesNotReferenceCatalog_ExceptAllowList`, but the existing test rule already enforces this — confusion could lead to a duplicate `[Fact]` that doesn't run. | Low | Don't add a new test method. Remove the two existing entries from `LogisticsCatalogAllowlist` (lines 83, 89 of `ModuleBoundariesTests.cs`). Verify the parameterized `Consumer_types_should_not_reference_provider_owned_namespaces` test row for `Logistics -> Catalog` (line 224) still passes. |
| Catalog adds a new `StockUpOperationState` member after this lands (e.g. `Reconciling = 4`). Adapter's exhaustive `switch` throws on the unmapped value — production stack trace at runtime. | Low | Acceptable. Loud failure at the boundary is preferable to a silent miscategorisation. The architecture test won't catch this (it inspects type references, not enum value parity). Add a contract test in Catalog's test suite that asserts every `StockUpOperationState` member maps to a `LogisticsStockOperationState` member — see Specification Amendments. |
| Existing `TransportBoxCompletionServiceTests` constructs real `StockUpOperation` entities via a `CreateOperation` helper; rewiring drops that dependency, but mock setups must change to return `LogisticsStockOperationStatus` instances instead. | Low | NFR-4 already calls this out. Replace the `Mock<IStockUpOperationRepository>` with `Mock<ILogisticsStockOperationQueryService>` and replace the `CreateOperation(...)` helper with a `CreateStatus(string documentNumber, LogisticsStockOperationState state)` helper that constructs the DTO directly. Test count and AAA structure stay the same. |
| `TransportBoxCompletionService` is invoked by `LogisticsModule` background-refresh wiring. Constructor signature change must compile after substitution. | Low | DI resolves by interface, not ctor shape. `services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>()` will pick up the new ctor params via constructor injection automatically. No `Program.cs` change. |
| Module ordering — `LogisticsModule.AddTransportModule(...)` is called separately from `CatalogModule.AddCatalogModule(...)`. If a future composition root accidentally adds Logistics without Catalog, the `ILogisticsStockOperationQueryService` registration is missing → DI resolution exception at first request. | Low | Existing pattern already has this property for the write adapter; nothing changes. Existing API composition registers both modules. No mitigation needed beyond keeping registration co-located with the existing write adapter binding. |

## Specification Amendments

1. **FR-3 lifetime correction.** Replace `services.AddScoped<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();` with `services.AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();` to match the existing sibling registration on `CatalogModule.cs:51`. The adapter is stateless; transient is the right lifetime.

2. **FR-5 reframing.** The spec describes a "new test `Logistics_DoesNotReferenceCatalog_ExceptAllowList`". The required infrastructure already exists at `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — the parameterized `Consumer_types_should_not_reference_provider_owned_namespaces` rule named "Logistics -> Catalog" (line 224) with `LogisticsCatalogAllowlist` (lines 77–90). The actual work is to **remove the two pre-existing allowlist entries** (lines 83 and 89) that whitelist `IStockUpOperationRepository` and `StockUpOperation` for `TransportBoxCompletionService`, and confirm the rule passes. No new test method is needed. The acceptance criterion "deliberately re-adding the using causes the test to fail" remains valid against the existing test.

3. **FR-2 amendment — enum-parity contract test (new, in Catalog tests).** Add a small Catalog-side test that asserts every `StockUpOperationState` member maps to some `LogisticsStockOperationState` member in `LogisticsStockOperationQueryAdapter`'s state mapper. Use `Enum.GetValues<StockUpOperationState>()` and assert the adapter's mapper does not throw for any of them. This catches the "Catalog adds a new state, Logistics quietly breaks" risk row above. Location: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs` (new file).

4. **FR-4 grep coverage.** The spec's grep at FR-4 acceptance excludes `Domain/Features/Logistics`, which is correct, but should also explicitly exclude the `Infrastructure/LogisticsCatalogTransportSourceAdapter.cs` file when interpreting the namespace `Anela.Heblo.Application.Features.Logistics`. That adapter implements a Catalog-owned contract (`ICatalogTransportSource`) and *correctly* lives in Logistics — the architecture test allowlists this via the reverse-direction `Catalog -> Logistics` rule (lines 95–102). Document this in the FR-4 acceptance criteria comment so a future reader doesn't try to "fix" the Catalog import.

## Prerequisites

- **PR #2201 merged to main** (already verified: commit `802a66f8` on this branch's base).
- **No infrastructure changes** required: no migrations, no config, no secret rotation, no external service updates.
- **Build tooling**: `dotnet build`, `dotnet format`, `dotnet test` — all standard.
- **No NuGet additions.**
- **No dependent specs.** This work is independent of the `ExpeditionList` / `ExpeditionListArchive` work mentioned in "Out of Scope" and of the Phase 2 module-isolation (`.csproj` split) work.
```