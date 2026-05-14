Now I have enough context to produce the spec.

# Specification: Decouple ProductEnrichmentCache from Catalog Domain via Module Contract

## Summary
Remove the cross-module coupling where `ProductEnrichmentCache` (KnowledgeBase) directly consumes `ICatalogRepository` and `CatalogAggregate` (Catalog domain). Introduce a narrow read-only query contract (`IProductCatalogQueryService`) owned by the Catalog module and inject it into the cache, eliminating the domain-layer dependency from KnowledgeBase while preserving identical behavior.

## Background
`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` reaches across module boundaries: it resolves `ICatalogRepository` from the DI container, calls `FindAsync` with a predicate over `CatalogAggregate`, and reads `ProductCode`, `ProductName`, and `Url` from those domain entities.

This violates two rules from `docs/architecture/development_guidelines.md`:
- "Communication between modules **exclusively through `contracts/`**"
- "**No direct access to another module's entities**"

Concrete consequences observed today:
1. Changes to `CatalogAggregate` or `ICatalogRepository` can silently break the KnowledgeBase ingestion pipeline.
2. KnowledgeBase tests must mock the full `ICatalogRepository` surface (see `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs`) and depend on `Anela.Heblo.Domain.Features.Catalog` types just to set up the test.
3. The two modules can never be split into independently deployable services, which is the stated long-term architectural goal.

The fix is surgical and low risk: introduce a narrow contract that returns a Catalog-owned DTO, replace the dependency, and update tests.

## Functional Requirements

### FR-1: Introduce Catalog-owned query contract for product enrichment
Create a new public read-only interface in the Catalog module that returns the minimal product projection needed by KnowledgeBase.

- Interface location: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs`
- DTO location: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs`
- Per project rule, the DTO is a **class** (not a `record`), matching all other files in `Features/Catalog/Contracts/`.
- The DTO exposes only `ProductCode`, `ProductName`, `Url`.
- The interface exposes one async method that returns the filtered, active product set (currently `Type == Product || Type == Goods`).

**Acceptance criteria:**
- New file `IProductCatalogQueryService.cs` exists in `Features/Catalog/Contracts/`.
- New file `ProductCatalogEntry.cs` exists in `Features/Catalog/Contracts/`.
- `ProductCatalogEntry` is a class with `ProductCode` (string, non-null), `ProductName` (string, non-null), `Url` (string?).
- Interface signature: `Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default);`.
- Neither file references any `Anela.Heblo.Domain.*` namespace.

### FR-2: Implement contract inside Catalog module
Implement `IProductCatalogQueryService` inside Catalog so the filter and mapping logic remain owned by the module that owns `CatalogAggregate`.

- Implementation location: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs`.
- The implementation wraps `ICatalogRepository`, applies the existing predicate (`p.Type == ProductType.Product || p.Type == ProductType.Goods`), and maps to `ProductCatalogEntry`.
- No behavior change: same predicate, same field projection, same null handling for `Url`.

**Acceptance criteria:**
- `ProductCatalogQueryService` is `internal sealed` and lives in `Features/Catalog/Services/`.
- Constructor takes `ICatalogRepository` only.
- `GetActiveProductsAsync` calls `repository.FindAsync(p => p.Type == ProductType.Product || p.Type == ProductType.Goods, ct)` and maps each `CatalogAggregate` to `ProductCatalogEntry { ProductCode, ProductName, Url }`.
- Returns `IReadOnlyList<ProductCatalogEntry>` (e.g., via `.Select(...).ToList()` cast to `IReadOnlyList<>`).
- Propagates the `CancellationToken`.

### FR-3: Register contract in CatalogModule
The new service must be registered in `CatalogModule.AddCatalogModule` so the KnowledgeBase module can resolve it without referencing Catalog internals.

- Registration lifetime: **Transient**, consistent with `ICatalogRepository` (line 39 of `CatalogModule.cs`).
- Placement: alongside other catalog-specific service registrations (after line ~62, before feature-flags block).

**Acceptance criteria:**
- `services.AddTransient<IProductCatalogQueryService, ProductCatalogQueryService>();` is present in `CatalogModule.cs`.
- The registration uses the contract interface, not the concrete type.

### FR-4: Refactor ProductEnrichmentCache to depend on the contract
Replace direct `ICatalogRepository` usage in `ProductEnrichmentCache` with `IProductCatalogQueryService`.

- File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs`.
- Remove `using Anela.Heblo.Domain.Features.Catalog;`.
- Add `using Anela.Heblo.Application.Features.Catalog.Contracts;`.
- Replace `scope.ServiceProvider.GetRequiredService<ICatalogRepository>()` with `GetRequiredService<IProductCatalogQueryService>()`.
- Replace the predicate call with `await service.GetActiveProductsAsync(ct)`.
- Map directly from `ProductCatalogEntry` (no `CatalogAggregate` reference).
- Keep the `IServiceScopeFactory` indirection — `IProductEnrichmentCache` is a **singleton** (see `KnowledgeBaseModule.cs:55`) and must not capture a scoped/transient service in its constructor.

**Acceptance criteria:**
- `ProductEnrichmentCache.cs` contains no reference to `Anela.Heblo.Domain.Features.Catalog`, `ICatalogRepository`, `CatalogAggregate`, or `ProductType`.
- The TTL/double-check-locking flow and `_cache` dictionary semantics are unchanged.
- Build succeeds with `dotnet build`.

### FR-5: Update unit tests to consume the new contract
Existing tests in `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs` mock `ICatalogRepository` and `CatalogAggregate`. They must be rewritten to mock `IProductCatalogQueryService` and assert on `ProductCatalogEntry`.

The original test "PredicateFiltersToProductAndGoodsOnly" verified the predicate composition; that responsibility now belongs to the new `ProductCatalogQueryService`. A corresponding test must move with the responsibility.

**Acceptance criteria:**
- `ProductEnrichmentCacheTests.cs` no longer references `Anela.Heblo.Domain.Features.Catalog`, `CatalogAggregate`, or `ProductType`.
- Tests mock `IProductCatalogQueryService.GetActiveProductsAsync` and verify:
  - The lookup correctly maps `ProductCatalogEntry` → `ProductEnrichmentEntry`.
  - TTL caching: repository is called once within TTL.
  - TTL expiry: repository is called again after TTL.
- A **new** test class `ProductCatalogQueryServiceTests` is added under `backend/test/Anela.Heblo.Tests/Catalog/Services/` (or equivalent existing Catalog test folder) covering:
  - Predicate filters to `Product` and `Goods` only (excludes `Material`, `SemiProduct`, `Set`, `UNDEFINED`).
  - Projection maps `ProductCode`, `ProductName`, `Url` correctly, preserving `null` for `Url`.
  - `CancellationToken` is passed through to the repository.
- All existing other tests that depend on `ProductEnrichmentCache` (e.g., `AskQuestionHandlerTests`, `PostAnswerEnrichmentMiddlewareTests`) continue to pass without changes (the cache's public contract `IProductEnrichmentCache` is unchanged).

### FR-6: No behavioral change to downstream consumers
`IProductEnrichmentCache` and `ProductEnrichmentEntry` are not modified. `PostAnswerEnrichmentMiddleware` and `AskQuestionHandler` continue to consume the same dictionary shape.

**Acceptance criteria:**
- `IProductEnrichmentCache.cs` and `ProductEnrichmentEntry.cs` are unchanged.
- Grepping the codebase for `IProductEnrichmentCache` shows no signature changes at any call site.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression is acceptable. The refactor adds one allocation per cache reload (the `ProductCatalogEntry` list inside Catalog) but cache reloads happen at most once per `ProductEnrichmentCacheTtlMinutes` (default operational TTL). The hot path (cache hit) is untouched.

- Cache hit path executes zero additional allocations vs. baseline.
- Cache reload path: one additional projection pass over the filtered product set (O(n) where n is the number of products and goods).

### NFR-2: Security
No new authorization surface, no new external calls, no PII handling change. The new contract returns the same product metadata that KnowledgeBase already reads.

### NFR-3: Maintainability
- KnowledgeBase must no longer reference `Anela.Heblo.Domain.Features.Catalog` from production code (verified via grep).
- New types follow project conventions: classes for contract DTOs, `IXxx` interfaces, `internal sealed` for implementations.
- Nullable reference types remain enabled; `ProductCatalogEntry.Url` is the only nullable field.

### NFR-4: Testability
After the refactor:
- `ProductEnrichmentCache` can be unit tested with a single mock (`IProductCatalogQueryService`) and zero Catalog-domain types.
- `ProductCatalogQueryService` carries the predicate test responsibility and can be tested with `ICatalogRepository` mocked, owned by Catalog tests.

## Data Model

No persistence or schema changes. Two new in-memory types only:

```
ProductCatalogEntry (class, Catalog/Contracts)
├── ProductCode : string (non-null, "")
├── ProductName : string (non-null, "")
└── Url         : string?

IProductCatalogQueryService (interface, Catalog/Contracts)
└── GetActiveProductsAsync(CancellationToken ct = default)
        : Task<IReadOnlyList<ProductCatalogEntry>>
```

Mapping from existing `CatalogAggregate`:
- `ProductCode` ← `CatalogAggregate.ProductCode`
- `ProductName` ← `CatalogAggregate.ProductName`
- `Url`         ← `CatalogAggregate.Url`

Filter: `Type == ProductType.Product || Type == ProductType.Goods` (unchanged from current behavior).

## API / Interface Design

No HTTP endpoint, controller, or MediatR request changes. This is purely a backend internal-contract refactor.

DI wiring (added in `CatalogModule.cs`):
```
services.AddTransient<IProductCatalogQueryService, ProductCatalogQueryService>();
```

DI consumption (refactored in `ProductEnrichmentCache.cs`):
```
var service = scope.ServiceProvider.GetRequiredService<IProductCatalogQueryService>();
var products = await service.GetActiveProductsAsync(ct);
```

## Dependencies

- `Anela.Heblo.Application.Features.Catalog.Contracts` (new namespace usage from KnowledgeBase). This is permitted; it is the documented inter-module communication surface.
- No new NuGet packages.
- No configuration changes; `KnowledgeBaseOptions.ProductEnrichmentCacheTtlMinutes` continues to govern cache TTL.

## Out of Scope

- Refactoring other Catalog → outside-module integrations (e.g., dashboard tiles, margin services). This spec is limited to the KnowledgeBase pipeline.
- Splitting `Anela.Heblo.Application.Features.Catalog.Contracts` into a separate project/assembly.
- Removing the `IServiceScopeFactory` indirection. The singleton-resolves-transient pattern is the existing design; changing it is out of scope.
- Adding caching, batching, or pagination inside `IProductCatalogQueryService`. The cache layer already handles freshness via TTL.
- Renaming `ProductEnrichmentEntry` or changing its shape.
- Updating `AskQuestionHandlerTests` or `PostAnswerEnrichmentMiddlewareTests` beyond what is strictly necessary to keep them green.
- Migration of any other `ICatalogRepository` consumer outside KnowledgeBase.

## Open Questions

None.

## Status: COMPLETE