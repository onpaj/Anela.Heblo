# Specification: Remove Analytics → Catalog Direct Cross-Module Dependency

## Summary
The Analytics module currently violates the project's module-boundary rules by depending directly on Catalog's domain types (`ProductType`) and repository (`ICatalogRepository`). This work introduces an Analytics-owned contract (`IAnalyticsProductSource`) implemented by a Catalog-side adapter, removes all Catalog imports from Analytics, and aligns the module with the adapter pattern documented in `development_guidelines.md`.

## Background
`docs/architecture/development_guidelines.md` explicitly forbids cross-module direct entity access and shared repositories. Two files violate this rule today:

- `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` imports `Anela.Heblo.Domain.Features.Catalog.ProductType` and exposes it as a property (`Type`).
- `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` injects `ICatalogRepository` and consumes `CatalogAggregate` (with its `MarginData`, `PurchaseHistory`, and other nested types) directly, performing field-level mapping inline.

Downstream consumers `GetMarginReportHandler` and `GetProductMarginSummaryHandler` re-import Catalog's `ProductType` because the public Analytics surface leaks it.

Consequences:
- Analytics cannot be built, tested, or evolved in isolation from Catalog.
- Internal refactors of `CatalogAggregate` silently break the manual mappings on lines 52–116 of `AnalyticsRepository.cs`.
- The dependency is hidden from architectural review because it is bidirectional in spirit (Analytics pulls Catalog data) but unidirectional in code (Analytics depends on Catalog).

The fix follows the canonical Cross-Module Communication pattern: Analytics owns the contract, Catalog implements the adapter, the DI registration lives in Catalog's composition root.

## Functional Requirements

### FR-1: Analytics-owned contract `IAnalyticsProductSource`
Introduce a new interface owned by the Analytics module that exposes only the data Analytics needs, expressed entirely in Analytics-owned types.

**Location:** `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/IAnalyticsProductSource.cs`

**Shape (informative, may be adjusted during implementation):**
```csharp
public interface IAnalyticsProductSource
{
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime from,
        DateTime to,
        AnalyticsProductType[] types,
        CancellationToken cancellationToken = default);

    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
```

**Acceptance criteria:**
- Interface lives under `Application/Features/Analytics/Contracts/`.
- Interface references no Catalog-owned types in its signature.
- All current call sites in Analytics that today hit `ICatalogRepository.GetProductsWithSalesInPeriod` and `ICatalogRepository.GetByIdAsync` for analytics purposes go through this interface.
- Method names and parameter shapes preserve the semantics of the current Analytics queries (period filter, optional product-type filter, single-product lookup).

### FR-2: Analytics-owned `AnalyticsProductType` enum
Mirror the values from `Catalog.ProductType` that Analytics consumes today, as an Analytics-owned enum.

**Location:** `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductType.cs`

**Acceptance criteria:**
- Enum exists in the Analytics domain namespace.
- Enum contains exactly the set of values Analytics currently uses from `Catalog.ProductType` (no extras, no omissions — see Open Questions for confirmation).
- `AnalyticsProduct.Type` is retyped to `AnalyticsProductType`.
- `GetMarginReportHandler` and `GetProductMarginSummaryHandler` no longer import from `Anela.Heblo.Domain.Features.Catalog`.

### FR-3: Catalog-side adapter `CatalogAnalyticsSourceAdapter`
Implement `IAnalyticsProductSource` inside the Catalog application layer. The adapter owns the `CatalogAggregate → AnalyticsProduct` mapping that currently lives in `AnalyticsRepository`.

**Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`

**Acceptance criteria:**
- Class implements `IAnalyticsProductSource`.
- Adapter consumes `ICatalogRepository` (legitimate, same-module access).
- Mapping logic from `AnalyticsRepository.cs` lines 52–116 (and the equivalent single-product mapping) moves verbatim into the adapter, behavior-preserving.
- The `AnalyticsProductType[] types` filter is translated to the Catalog-side `ProductType[]` at the adapter boundary.
- Adapter has no knowledge of MediatR handlers, controllers, or other Analytics application code.

### FR-4: Catalog module registers the binding
The DI registration for `IAnalyticsProductSource → CatalogAnalyticsSourceAdapter` must live in Catalog's module wiring, not Analytics'.

**Location:** `CatalogModule.AddCatalogModule()` (or equivalent extension method in the Catalog module composition root).

**Acceptance criteria:**
- Registration is `services.AddScoped<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();` (lifetime to match existing Catalog repository registrations).
- Analytics module wiring contains no reference to the adapter implementation.
- Application boots and resolves `IAnalyticsProductSource` correctly via DI.

### FR-5: `AnalyticsRepository` drops `ICatalogRepository`
Refactor `AnalyticsRepository` to depend only on `IAnalyticsProductSource` and `ApplicationDbContext`.

**Location:** `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`

**Acceptance criteria:**
- No `using Anela.Heblo.Domain.Features.Catalog;` in the file.
- Constructor signature replaces `ICatalogRepository` with `IAnalyticsProductSource`.
- The previous mapping code (lines 52–116) is removed from this file (it now lives in the adapter).
- `GetProductsWithSalesInPeriod`-style calls go through `StreamProductsWithSalesAsync`.
- `GetByIdAsync`-style calls go through `GetProductAnalysisDataAsync`.
- Streaming semantics: if `StreamProductsWithSalesAsync` returns `IAsyncEnumerable`, the repository materializes only what each handler needs (do not regress memory usage by always `ToListAsync()`-ing if the prior code was already streaming; preserve current behavior).

### FR-6: Downstream handler cleanup
Remove Catalog imports from `GetMarginReportHandler` and `GetProductMarginSummaryHandler`.

**Acceptance criteria:**
- Neither file imports from `Anela.Heblo.Domain.Features.Catalog`.
- Any `ProductType` references become `AnalyticsProductType`.
- Handler unit/integration tests still pass.

### FR-7: Behavior preservation
The end-to-end behavior of every Analytics endpoint must remain identical before and after the refactor.

**Acceptance criteria:**
- All existing Analytics unit and integration tests pass without modification to assertions (only injection/mocking targets change).
- For each affected endpoint, response payloads for an identical request are byte-equivalent (or semantically identical if ordering is unspecified) before and after the change.
- No new SQL queries are introduced (the adapter performs the same underlying repository calls).

## Non-Functional Requirements

### NFR-1: Performance
- No regression in query count, query shape, or memory profile for `GetMarginReportHandler` and `GetProductMarginSummaryHandler`.
- If the current code streams via `IAsyncEnumerable` or paged enumeration, the adapter preserves streaming. If it currently materializes a full list, that is acceptable to preserve.
- Adapter mapping must remain O(n) over the input set with no additional allocations beyond what the current mapping does.

### NFR-2: Architecture compliance
- After the change, `grep -r "Anela.Heblo.Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Analytics/` and the same against `backend/src/Anela.Heblo.Domain/Features/Analytics/` return zero matches.
- No Analytics file references `CatalogAggregate`, `MarginData`, `PurchaseHistory`, `ProductType`, or `ICatalogRepository`.

### NFR-3: Test isolation
- Analytics tests no longer need a Catalog test double for `ICatalogRepository`; they mock `IAnalyticsProductSource` instead.
- A new adapter test verifies the `CatalogAggregate → AnalyticsProduct` mapping in isolation (this is the highest-risk surface, since it was previously untested at the mapper level).

### NFR-4: Backwards compatibility
- Public HTTP API surface of Analytics endpoints is unchanged (request/response DTOs identical).
- Internal contract changes only — no API client regeneration required, no frontend changes.

## Data Model

**New types (Analytics-owned):**
- `AnalyticsProductType` (enum) — values mirror current usage of `Catalog.ProductType` within Analytics.
- `IAnalyticsProductSource` (interface) — contract for product-with-sales data, expressed entirely in Analytics types.

**Modified types:**
- `AnalyticsProduct.Type` retyped from `Catalog.ProductType` to `AnalyticsProductType`.

**New types (Catalog-owned):**
- `CatalogAnalyticsSourceAdapter` (class) — implements `IAnalyticsProductSource` using `ICatalogRepository` internally; owns the `CatalogAggregate → AnalyticsProduct` mapping.

**Unchanged:**
- `CatalogAggregate`, `MarginData`, `PurchaseHistory`, `Catalog.ProductType`, `ICatalogRepository` — no changes to Catalog's internal model.
- Database schema — no migrations.

## API / Interface Design

**Internal interface (new):** `IAnalyticsProductSource` (see FR-1).

**Dependency injection wiring:**
- Catalog module: `services.AddScoped<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();`
- Analytics module: no change (the framework resolves the interface from the Catalog registration).

**HTTP endpoints:** No changes. All existing Analytics controllers/handlers continue to expose the same routes, requests, and responses.

**Module wiring order:** `CatalogModule.AddCatalogModule()` must be invoked before any code resolves `IAnalyticsProductSource`. Confirm the current `Program.cs` / module composition already invokes Catalog registration before Analytics consumption (it should — Catalog has no dependency on Analytics).

## Dependencies

**Internal:**
- Catalog module (`ICatalogRepository`, `CatalogAggregate`, and related types remain the data source — only the access path changes).
- Existing Analytics MediatR handlers (`GetMarginReportHandler`, `GetProductMarginSummaryHandler`) and their unit/integration tests.

**External:** None. No new NuGet packages, no infrastructure changes, no migrations.

**Documentation:**
- `docs/architecture/development_guidelines.md` § *Cross-Module Communication* is the canonical reference for the pattern being applied.

## Out of Scope

- Refactoring of `CatalogAggregate`, `MarginData`, `PurchaseHistory`, or any other Catalog-owned type.
- Splitting Analytics into its own deployable assembly or solution.
- Introducing message-bus / async communication between modules; this is an in-process adapter only.
- Changes to Analytics endpoints' HTTP surface, request/response DTOs, or frontend code.
- Auditing other modules for similar violations (a separate arch-review pass).
- Performance optimizations beyond preserving current behavior.
- Database migrations of any kind.

## Open Questions

None.

## Status: COMPLETE