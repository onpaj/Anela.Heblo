# Architecture Review: Extract duplicated `HasSalesInPeriod` logic into a shared `AnalyticsProduct` extension

## Skip Design: true

## Architectural Fit Assessment

This is a textbook-clean refactor with an exact precedent already in the codebase. Verified against actual source:

- `GetMarginReportHandler.cs:125-128` and `GetProductMarginAnalysisHandler.cs:71-74` both define `private static bool HasSalesInPeriod(...)` with an identical body (`product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate)`), differing only in parameter name (`product` vs `productData`). Call sites are `GetMarginReportHandler.cs:95` and `GetProductMarginAnalysisHandler.cs:51`, exactly as the spec states.
- `AnalyticsProduct` and `SalesDataPoint` (`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`) are plain, behavior-free data classes populated by the repository layer — no EF mapping, no persistence concerns, no invariants to protect. Attaching a pure, stateless predicate to them via an extension method is a natural fit, not a stretch.
- The Domain layer already has exactly this pattern in three places: `CarrierExtensions.cs` (Logistics), `CurrentUserExtensions.cs` (Users), `ManufactureOrderExtensions.cs` (Manufacture) — all static `{Entity}Extensions` classes co-located with the entity they extend, in the entity's own feature namespace. `AnalyticsProductExtensions` in `Domain/Features/Analytics/` is consistent with this established convention, not a new one.
- Both handlers already `using Anela.Heblo.Domain.Features.Analytics;` and already reference `AnalyticsProduct` by its Domain-layer type, so no new project reference or using statement beyond what's already present is required (Application → Domain dependency already exists per `docs/architecture/filesystem.md`'s layering).
- The Analytics module's existing "extract shared logic into a named service" pattern (`IMarginCalculator`, `IProductFilterService`, `IReportBuilderService`) is Application-layer and DI-injected — appropriate for logic with dependencies or that needs mocking in isolation. `HasSalesInPeriod` has neither: it's a pure, dependency-free one-liner over data already in memory. Promoting it to an injectable service would be over-engineering for a boolean predicate; a Domain extension method is the right-sized abstraction, matching the spec's explicit choice (see Out of Scope) and the codebase's own precedent for this exact shape of logic.

No conflicts with `development_guidelines.md`: this introduces no new DTO (the DTO-classes-not-records rule doesn't apply — `AnalyticsProductExtensions` is a static method container, not a data-transfer type), no new module boundary crossing, no new controller/API surface, and no persistence change.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features/Analytics/
    ├── AnalyticsProduct.cs                 (existing, unchanged)
    └── AnalyticsProductExtensions.cs        (NEW — static extension class)

Anela.Heblo.Application
└── Features/Analytics/UseCases/
    ├── GetMarginReport/GetMarginReportHandler.cs
    │     — private HasSalesInPeriod REMOVED, call site now `product.HasSalesInPeriod(startDate, endDate)`
    └── GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs
          — private HasSalesInPeriod REMOVED, call site now `productData.HasSalesInPeriod(request.StartDate, request.EndDate)`
```

No new dependency edges are introduced: Application already depends on Domain (that's where `AnalyticsProduct` itself lives). This is purely a "move a method one layer down and rename its call sites" change.

### Key Design Decisions

#### Decision 1: Static Domain extension method vs. injectable Application service
**Options considered:**
1. Static extension method on `AnalyticsProduct` in Domain (as specced).
2. New injectable service (`ISalesPeriodChecker` or folded into `IMarginCalculator`/`IProductFilterService`), following the module's DI-service pattern.
3. Instance method directly on `AnalyticsProduct` (`product.HasSalesInPeriod(...)` as a member, not an extension).

**Chosen approach:** Option 1, per spec.

**Rationale:** The logic is pure, stateless, and has zero dependencies (no repository, no config, no logging) — nothing to inject or mock. Options 2 would add DI ceremony and a test double for a one-line predicate, which is disproportionate and inconsistent with how trivial extensions are already handled elsewhere (`CarrierExtensions`, `CurrentUserExtensions`, `ManufactureOrderExtensions` are all static, non-DI). Option 3 (instance method) is rejected because `AnalyticsProduct` is explicitly documented as a "lightweight … model" built by the repository for read purposes (see its XML doc comment); keeping query/filter behavior as extension methods rather than instance methods keeps the entity itself a narrow data holder, matching the existing convention where behavior is layered on via `*Extensions` rather than added directly to the class body.

#### Decision 2: Placement — Domain layer vs. Application-layer shared helper
**Options considered:**
1. `Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs` (Domain layer, as specced).
2. A shared static helper class in `Anela.Heblo.Application/Features/Analytics/` (e.g. under a `Shared/` or `Services/` folder), since both consuming handlers are Application-layer.

**Chosen approach:** Option 1, per spec.

**Rationale:** The method operates exclusively on `AnalyticsProduct`/`SalesDataPoint`, both Domain types, with no Application-layer dependency (no MediatR, no contracts, no repository). Clean Architecture places entity-scoped behavior with the entity's layer whenever it doesn't need outer-ring services — this is exactly the precedent set by `ManufactureOrderExtensions` (extends `ManufactureOrder`/`ManufactureOrderProduct`, lives in `Domain/Features/Manufacture/`) and `CarrierExtensions`. Placing it in Application would create an inconsistency the next reviewer would have to explain, for no benefit.

## Implementation Guidance

### Directory / Module Structure
- **New file:** `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs` — single `public static class AnalyticsProductExtensions` containing only `HasSalesInPeriod`. No new folder needed.
- **No `{Feature}Module.cs` change** — static extension methods require no DI registration.
- **No new test project/module** — this stays inside the existing `Anela.Heblo.Domain`/`Anela.Heblo.Application` structure.

### Interfaces and Contracts
- New public surface (exact signature, per spec):
  ```csharp
  namespace Anela.Heblo.Domain.Features.Analytics;

  public static class AnalyticsProductExtensions
  {
      public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
          => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
  }
  ```
- `GetMarginReportHandler.cs`: delete the `private static bool HasSalesInPeriod(...)` method at lines 125-128; change the call at line 95 from `HasSalesInPeriod(product, startDate, endDate)` to `product.HasSalesInPeriod(startDate, endDate)`.
- `GetProductMarginAnalysisHandler.cs`: delete the `private static bool HasSalesInPeriod(...)` method at lines 71-74; change the call at line 51 from `HasSalesInPeriod(productData, request.StartDate, request.EndDate)` to `productData.HasSalesInPeriod(request.StartDate, request.EndDate)`.
- Both files already have `using Anela.Heblo.Domain.Features.Analytics;` (confirmed at line 5 of `GetMarginReportHandler.cs` and line 4 of `GetProductMarginAnalysisHandler.cs`) — no using-statement changes needed.
- No changes to any MediatR `Request`/`Response` DTO, no changes to `IMarginCalculator`, `IProductFilterService`, `IReportBuilderService`, or any other interface.

### Data Flow
Unchanged. `IAnalyticsRepository` still builds `AnalyticsProduct` instances (with pre-filtered `SalesHistory`); handlers still call the period check before proceeding to margin calculation. The only change is *where* the check's implementation is defined and *how* it's invoked (extension-method syntax on the Domain type instead of a handler-private static method) — the runtime call graph and IL are effectively identical (C# extension methods compile to ordinary static method calls).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidental behavior drift while moving the method (e.g., swapping `>=`/`<=` for `>`/`<`) | Low | Copy the body verbatim as specified in FR-1; rely on the existing handler test suites (`GetMarginReportHandlerTests.cs`, `GetProductMarginAnalysisHandlerTests.cs`) to catch any regression — they exercise boundary dates indirectly and must pass unmodified. |
| Leaving a stray unused `using` or now-unreachable helper class member behind after deleting the private methods | Low | `dotnet build`/`dotnet format` (required by this repo's validation step) will flag unused usings/dead code; verify no other private members in either handler referenced only by the deleted method. |
| Namespace collision / ambiguity between `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct` and any Application-layer type of the same short name | Very Low | Not applicable here — both handlers already fully qualify or alias `AnalyticsProduct` from the Domain namespace today; no new ambiguity is introduced. |

## Specification Amendments
None. The spec is precise, fully verified against the current source (file paths, line numbers, and method bodies all match exactly), and requires no architectural correction. The extension-class-in-Domain pattern it specifies is not just reasonable but already the established convention in this codebase (`CarrierExtensions`, `CurrentUserExtensions`, `ManufactureOrderExtensions`), which strengthens confidence this is a safe, idiomatic move.

## Prerequisites
None. No migrations, no config, no new infrastructure, no DI wiring. Implementation can start immediately: add the new Domain file, then update the two handlers in the same change.
