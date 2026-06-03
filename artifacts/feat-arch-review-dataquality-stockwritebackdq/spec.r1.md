Now I have enough context to write a complete spec.

```markdown
# Specification: Decouple `StockWriteBackDqtComparer` from Catalog repository interfaces

## Summary
`StockWriteBackDqtComparer` in the DataQuality module directly injects two Catalog-owned repository interfaces (`IStockUpOperationRepository`, `IStockTakingRepository`), violating the module-boundary rules defined in `docs/architecture/development_guidelines.md`. This spec defines two narrow, DataQuality-owned read contracts plus Catalog-side adapters that implement them, mirroring the existing `ILogisticsStockOperationQueryService` precedent. The change is purely structural — runtime behavior of the DQT comparer is preserved.

## Background
`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs` consumes:
- `IStockUpOperationRepository` — a full CRUD repository (`AddAsync`, `UpdateAsync`, `SaveChangesAsync`, `GetByDocumentNumberAsync`, `GetBySourceAsync`, `GetAll`, …). The comparer calls only `GetAll()` to read operations within a date window.
- `IStockTakingRepository` — extends `IRepository<StockTakingRecord, int>`, exposing full CRUD. The comparer calls only `GetByDateRangeAsync`.

This violates two of the development guidelines:
1. **"Communication between modules exclusively through `contracts/`"** — DataQuality reaches into Catalog's domain namespace (`Anela.Heblo.Domain.Features.Catalog.Stock`).
2. **"No direct access to another module's entities"** — `StockUpOperation` and `StockTakingRecord` are Catalog aggregates with private setters and domain methods (`MarkAsSubmitted`, `Reset`, `AcceptFailure`, …) that DataQuality must not depend on.

Additional concerns:
- **ISP** — testing the comparer requires mocking the full CRUD repository contracts (`Mock<IStockUpOperationRepository>` and `Mock<IStockTakingRepository>` in `StockWriteBackDqtComparerTests.cs`).
- **Future microservice readiness** — any deployment of DataQuality without Catalog's persistence layer is impossible today.
- **Refactor blast radius** — renaming or splitting `IStockUpOperationRepository` (already a likely future change given its mixed read/write responsibilities) silently breaks DataQuality.

The codebase already has a precedent for the consumer-owned-contract / provider-side-adapter pattern: `ILogisticsStockOperationQueryService` is owned by Logistics; `LogisticsStockOperationQueryAdapter` lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure` and is registered in `CatalogModule.AddCatalogModule`. The same pattern applies here.

## Functional Requirements

### FR-1: DataQuality-owned read contract for stock-up operations
Introduce a new interface `IStockOperationQuery` in `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockOperationQuery.cs`. The interface exposes exactly the read operation needed by `StockWriteBackDqtComparer`: fetch stock-up operations created within a given UTC date window, projected to DataQuality-owned DTOs.

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IStockOperationQuery
{
    Task<IReadOnlyList<StockOperationSnapshot>> GetByCreatedDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
```

A new DataQuality-owned DTO `StockOperationSnapshot` exposes only the fields the comparer reads (`ProductCode`, `Amount`, `DocumentNumber`, `State`, `CreatedAt`, `ErrorMessage`). Its `State` field is a DataQuality-owned enum (`StockOperationStateSnapshot`) with values `Pending`, `Submitted`, `Completed`, `Failed` — mirroring `StockUpOperationState` but not referencing it.

**Acceptance criteria:**
- `IStockOperationQuery`, `StockOperationSnapshot`, and `StockOperationStateSnapshot` live under `Anela.Heblo.Application.Features.DataQuality.Contracts` and do not transitively reference any `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*` type.
- `StockOperationSnapshot` is a class (per the repo-wide rule "DTOs are classes, never C# records") with `init` setters or constructor parameters; immutable.
- The DTO uses `string ProductCode`, `int Amount`, `string DocumentNumber`, `StockOperationStateSnapshot State`, `DateTime CreatedAtUtc`, `string? ErrorMessage`.

### FR-2: DataQuality-owned read contract for stock-taking records
Introduce a new interface `IStockTakingQuery` in `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockTakingQuery.cs`:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IStockTakingQuery
{
    Task<IReadOnlyList<StockTakingSnapshot>> GetByDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
```

A new DataQuality-owned DTO `StockTakingSnapshot` exposes only `Code` (string), `AmountNew` (double, matching the existing field type), and `Error` (`string?`) — the three fields used by the comparer.

**Acceptance criteria:**
- `IStockTakingQuery` and `StockTakingSnapshot` live under `Anela.Heblo.Application.Features.DataQuality.Contracts` and do not transitively reference any `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*` type.
- `StockTakingSnapshot` is a class with immutable members (init-only or constructor-set).

### FR-3: Catalog-side adapters implementing the DataQuality contracts
Add two adapters to `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`:

1. `DataQualityStockOperationQueryAdapter` — implements `IStockOperationQuery`. Injects `IStockUpOperationRepository`. Applies the date-window filter (`CreatedAt >= fromUtc && CreatedAt <= toUtc`) and projects each `StockUpOperation` to a `StockOperationSnapshot`, mapping `StockUpOperationState` → `StockOperationStateSnapshot`.
2. `DataQualityStockTakingQueryAdapter` — implements `IStockTakingQuery`. Injects `IStockTakingRepository`. Delegates to `GetByDateRangeAsync(from, to, ct)` and projects each `StockTakingRecord` to a `StockTakingSnapshot`.

Both adapters are `internal sealed`. Filtering happens on the database side wherever possible (the existing comparer materializes the IQueryable before filtering by date — the adapter should preserve at-most equivalent performance; see NFR-1).

**Acceptance criteria:**
- Adapters live in `Anela.Heblo.Application.Features.Catalog.Infrastructure` and are `internal sealed`.
- All state-enum mapping is exhaustive: unknown enum values throw `ArgumentOutOfRangeException` (matching the precedent in `LogisticsStockOperationQueryAdapter`).
- Adapters do not expose any Catalog domain type through their public surface — only through DI internals.

### FR-4: DI registration in Catalog module
Register both adapters in `CatalogModule.AddCatalogModule` alongside the existing cross-module adapter bindings (`IMaterialCatalogService`, `IPurchasePriceRecalculationService`, `ILogisticsStockOperationQueryService`, …):

```csharp
services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
```

Per the inversion-of-dependencies pattern documented in `docs/architecture/development_guidelines.md` ("Provider (B) registers the DI binding"), the registration belongs in Catalog's module file, not DataQuality's.

**Acceptance criteria:**
- DI registrations are in `CatalogModule.cs`, not `DataQualityModule.cs`.
- Lifetime is `Scoped` (matches the underlying repository lifetimes).
- The application boots end-to-end with the new bindings (verified by integration startup test or `dotnet run` smoke).

### FR-5: Update `StockWriteBackDqtComparer` to consume new contracts
Update `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs`:
- Replace constructor dependencies `IStockUpOperationRepository operationRepository, IStockTakingRepository stockTakingRepository` with `IStockOperationQuery stockOperations, IStockTakingQuery stockTakings`.
- Remove the `using Anela.Heblo.Domain.Features.Catalog.Stock;` import.
- Rewrite `CompareAsync` to call the new contracts. The date-range filter for operations moves into the adapter (FR-3); the comparer asks directly for operations in the window.
- Replace references to `StockUpOperationState.Failed`, `StockUpOperationState.Pending`, `StockUpOperationState.Submitted` with the DataQuality-owned `StockOperationStateSnapshot` equivalents.
- `BuildOperationDetails` accepts `StockOperationSnapshot` instead of `StockUpOperation`.

**Acceptance criteria:**
- `StockWriteBackDqtComparer.cs` contains no `using Anela.Heblo.Domain.Features.Catalog.*` directive.
- Public observable behavior (the `DriftMismatch` list and `TotalChecked` count for any given input) is identical to the current implementation for every existing test case in `StockWriteBackDqtComparerTests.cs`.
- The `_stuckThreshold` semantics and default of 1 hour are unchanged.

### FR-6: Update tests to use new contracts
Update `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs`:
- Mock `IStockOperationQuery` and `IStockTakingQuery` instead of the Catalog repository interfaces.
- Set up `GetByCreatedDateRangeAsync` to return `IReadOnlyList<StockOperationSnapshot>` (instead of the previous `GetAll()` returning `IQueryable<StockUpOperation>` materialized via `AsQueryable()`).
- The four existing test cases (`CompareAsync_ReturnsEmpty_WhenAllOperationsCompleted`, `…OperationFailed…`, `…OperationStuck…`, `…StockTakingErrored…`) must continue to assert the same outcomes.

**Acceptance criteria:**
- Tests no longer reference `IStockUpOperationRepository`, `IStockTakingRepository`, `StockUpOperation`, `StockTakingRecord`, or `Anela.Heblo.Domain.Features.Catalog.Stock`.
- All four existing tests pass against the refactored comparer.
- A new unit test verifies the adapter mapping: feed a `StockUpOperation` (built via its public constructor + state transitions) and assert the produced `StockOperationSnapshot` carries the same six fields (one happy-path test per adapter is enough).

### FR-7: Architecture boundary test for DataQuality → Catalog
Add a new `ModuleBoundaryRule` entry to `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforcing the new direction:

```csharp
new ModuleBoundaryRule(
    Name: "DataQuality -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

The allowlist must be empty after this spec is implemented (no pre-existing leaks remain — `StockWriteBackDqtComparer` was the only violator according to the brief; FR-7 implementation must confirm by running the new rule and resolving anything else discovered, or escalating via Open Questions if a leak is out of scope).

**Acceptance criteria:**
- A new entry in `Rules()` matches the format above.
- `Consumer_types_should_not_reference_provider_owned_namespaces` passes for the `DataQuality -> Catalog` rule with an empty allowlist.

## Non-Functional Requirements

### NFR-1: Performance
The current implementation calls `_operationRepository.GetAll()` to return an `IQueryable<StockUpOperation>` and applies the date-range filter via LINQ-to-EF. The adapter introduced in FR-3 must:
- Push the date filter into the underlying query (e.g., `_repository.GetAll().Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc).Select(...).ToListAsync(ct)`) so the database does the work, not the application.
- Materialize results once into `IReadOnlyList<StockOperationSnapshot>`.

Net effect: identical or better SQL than today (today the comparer also relies on EF translation of the `Where` clause to SQL after `GetAll()`). No new round-trips. No extra allocations beyond the DTO projection.

### NFR-2: Module boundaries (correctness)
After this change:
- The `Anela.Heblo.Application.Features.DataQuality` namespace contains zero references to any `Anela.Heblo.Domain.Features.Catalog.*`, `Anela.Heblo.Application.Features.Catalog.*`, or `Anela.Heblo.Persistence.Catalog.*` type.
- The new architecture test from FR-7 enforces this on every CI run.
- All other modules' boundary rules continue to pass.

### NFR-3: Backward compatibility
- The MediatR/REST behavior of all DQT endpoints (`/api/data-quality/...`) is unchanged.
- `DqtTestType.StockWriteBackReconciliation` is still produced by the same comparer.
- `DriftDqtJobRunner` continues to resolve `IDriftDqtComparer` instances without code change — only the comparer's constructor signature changes, and DI resolves the new dependencies.

### NFR-4: Coding standards
- Nullable reference types enabled (default in this repo).
- DTOs are classes (not records) per the OpenAPI-generator constraint in `CLAUDE.md`. Although these particular DTOs are internal contracts (not exposed via OpenAPI), the project-wide rule applies for consistency.
- `dotnet format` clean.
- `dotnet build` succeeds.
- All tests pass: existing unit tests (FR-6) + new adapter tests + new architecture rule (FR-7).

## Data Model
No database schema changes. No new entities. The change is entirely in the contract/adapter layer.

**New types introduced (DataQuality-owned, all in `Anela.Heblo.Application.Features.DataQuality.Contracts`):**
- `IStockOperationQuery` — interface (FR-1)
- `IStockTakingQuery` — interface (FR-2)
- `StockOperationSnapshot` — class DTO (FR-1)
- `StockOperationStateSnapshot` — enum: `Pending`, `Submitted`, `Completed`, `Failed`
- `StockTakingSnapshot` — class DTO (FR-2)

**New types introduced (Catalog-owned, all in `Anela.Heblo.Application.Features.Catalog.Infrastructure`):**
- `DataQualityStockOperationQueryAdapter` — implements `IStockOperationQuery` (FR-3)
- `DataQualityStockTakingQueryAdapter` — implements `IStockTakingQuery` (FR-3)

**Existing Catalog types unchanged:**
- `IStockUpOperationRepository`, `IStockTakingRepository`, `StockUpOperation`, `StockTakingRecord`, `StockUpOperationState` remain as they are. Other Catalog-internal consumers of these types are unaffected.

## API / Interface Design

No HTTP API changes. The work is purely an internal refactor — no controller, MediatR request, OpenAPI schema, TypeScript client, or frontend code is touched.

**Internal call graph after the change:**
```
StockWriteBackDqtComparer
  ├─ IStockOperationQuery          (DataQuality.Contracts, implemented in Catalog.Infrastructure)
  │     └─ IStockUpOperationRepository   (Catalog.Domain, unchanged)
  └─ IStockTakingQuery             (DataQuality.Contracts, implemented in Catalog.Infrastructure)
        └─ IStockTakingRepository        (Catalog.Domain, unchanged)
```

## Dependencies
- No new NuGet packages.
- No external services.
- Existing infrastructure already in the codebase:
  - `IStockUpOperationRepository` and its `Persistence` implementation
  - `IStockTakingRepository` and its `Persistence` implementation
  - `CatalogModule.AddCatalogModule` (the registration point)
  - `ModuleBoundariesTests` (the validation harness)

## Out of Scope
- Splitting `IStockUpOperationRepository` into read/write halves at the Catalog-internal level. The brief notes the repository is a mixed CRUD interface, but reshaping Catalog's internal repository is a separate concern.
- Removing the `Anela.Heblo.Application.Features.Catalog.Infrastructure.LogisticsStockOperationQueryAdapter` allowlist entries in `ModuleBoundariesTests.cs` (these are pre-existing and tracked separately per the inline comments).
- Introducing similar contracts for `ProductPairingDqtComparer` or any other DataQuality service. Only `StockWriteBackDqtComparer` is in scope.
- Adding a feature flag — this is a refactor with no behavioral change, no flag warranted.
- Database migration — no schema change.
- Frontend or E2E changes.
- Renaming or relocating `StockUpOperationState` / `StockTakingRecord` themselves.

## Open Questions
None.

## Status: COMPLETE
```