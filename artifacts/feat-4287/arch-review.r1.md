# Architecture Review: Deduplicate sort-direction ternary in GetProductMarginsHandler.ApplySorting

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend, single-file, no-behavior-change refactor inside a MediatR handler (`GetProductMarginsHandler`) in the Catalog module's vertical slice (`Features/Catalog/UseCases/GetProductMargins/`). It touches only a private method (`ApplySorting`) and adds one new private static helper method to the same class. It does not cross module boundaries, does not change `contracts/` DTOs, does not touch persistence, and does not add dependencies. This aligns cleanly with the project's Vertical Slice Architecture and the "surgical changes" principle in `CLAUDE.md` — the change should be scoped to exactly this file.

I verified the current code directly (`sed`/`grep` on the handler): the switch has **13 named arms + 1 `default` arm + 1 pre-switch null-guard = 15 occurrences** of the `sortDescending ? OrderByDescending : OrderBy` pattern, not the "16 arms / 17 occurrences" the issue text states. This does not change the fix's shape — it only means the analyst's spec note (already reflecting the actual count) is the one implementers should trust over the issue body's arm count.

I also checked for an existing shared "sort by direction" convention elsewhere in the codebase to see whether this helper should be centralized:
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs` has the **exact same repeated-ternary pattern** (a different DTO, `TopProductDto`, in the Analytics module) — confirming this is a systemic style issue, not unique to Catalog.
- There is no existing shared `SortBy`/`OrderByDirection` utility in `Xcc` (the shared kernel) or elsewhere that this handler could reuse.
- `docs/architecture/development_guidelines.md` explicitly warns against adding a shared helper (e.g. `UserIdResolver`) "unless a real consumer exists." `TopProductSorter`'s duplicate is a separate class in a separate module with a separate DTO (`TopProductDto` vs. `CatalogAggregate`) and is **not** in scope for this issue — extracting a cross-module shared generic sort helper now would be speculative generalization for a single real consumer.

**Decision: keep the helper private and local to `GetProductMarginsHandler`,** exactly as the issue's suggested fix proposes. Do not attempt to generalize it into a shared utility as part of this change; that is a separate, larger-scoped refactor (and would itself warrant its own arch-review finding against `TopProductSorter` if desired later).

## Proposed Architecture

### Component Overview
```
GetProductMarginsHandler (unchanged public surface)
│
├─ Handle(...)                     — unchanged
├─ GetProducts(...)                — unchanged
├─ ApplyFilters(...)                — unchanged
├─ ApplySorting(products, sortBy, sortDescending)   — REWRITTEN internals only
│     └─ calls → SortBy<TKey>(items, key, desc)     — NEW private static helper
└─ MapToMarginDto(...)              — unchanged
```
No new files, no new classes, no new interfaces. `SortBy<TKey>` lives as a private static method on `GetProductMarginsHandler`, directly below or above `ApplySorting` in the same file.

### Key Design Decisions

#### Decision 1: Helper scope — private static instance-class method vs. extracted service
**Options considered:**
- (a) Private static method on `GetProductMarginsHandler` (as the issue suggests).
- (b) Extract to a shared static utility class (e.g. `Xcc/SortHelpers.cs`) reusable by `TopProductSorter` and future handlers.
- (c) Extract to an injectable service (`ISortHelper`) following the `TopProductSorter`/`ITopProductSorter` pattern already used in Analytics.

**Chosen approach:** (a).

**Rationale:** The issue is explicitly scoped as "no behaviour change" and single-file. Options (b)/(c) increase blast radius (new file, new namespace decision, cross-module reuse question, DI registration) for no requirement in this issue and no second confirmed consumer inside this change's scope. `CLAUDE.md`'s "surgical changes" rule and the codebase's own stated aversion to speculative shared helpers both point to (a). If `TopProductSorter`'s duplicate is worth fixing, that is better tracked as its own arch-review finding/issue, not folded into this one.

#### Decision 2: Helper return type
**Options considered:**
- (a) `IEnumerable<CatalogAggregate>` (as literally written in the issue's suggested-fix snippet).
- (b) `IOrderedEnumerable<CatalogAggregate>` (the actual common return type of `OrderBy`/`OrderByDescending`).

**Chosen approach:** (b), `IOrderedEnumerable<CatalogAggregate>`.

**Rationale:** Both compile and both satisfy `ApplySorting`'s existing `IEnumerable<CatalogAggregate>` return type (covariant), but (b) is the precise, non-widened type actually produced, costs nothing extra, and keeps the option open for a future `.ThenBy` if ever needed (not required now, but the narrower type is simply more correct). This is a one-line signature choice with zero behavioral difference either way — the analyst's spec already flags this as the recommended variant.

#### Decision 3: Generic key type constraint
**Options considered:**
- (a) Unconstrained `TKey` (relies on LINQ's default `Comparer<TKey>.Default` inside `OrderBy`/`OrderByDescending`, same as today's inline calls).
- (b) `where TKey : IComparable<TKey>` constraint.

**Chosen approach:** (a), unconstrained.

**Rationale:** `OrderBy`/`OrderByDescending` themselves are unconstrained (`IComparer<TKey>` defaults at runtime), and the existing 13 arms already sort by `string`, `decimal`, `decimal?`-coalesced-to-`decimal`, and `int?`-coalesced-to-`int` values without any explicit comparer — adding a constraint changes nothing observable and risks a mismatch with an edge-case key type. Match the existing implicit contract exactly.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes are inside:
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`

Add the new private static helper method adjacent to `ApplySorting` (immediately before or after it) so a reader sees both together.

### Interfaces and Contracts
```csharp
private static IOrderedEnumerable<CatalogAggregate> SortBy<TKey>(
    IEnumerable<CatalogAggregate> items,
    Func<CatalogAggregate, TKey> key,
    bool desc)
    => desc ? items.OrderByDescending(key) : items.OrderBy(key);
```

`ApplySorting` becomes:
```csharp
private IEnumerable<CatalogAggregate> ApplySorting(IEnumerable<CatalogAggregate> products, string? sortBy, bool sortDescending)
{
    if (string.IsNullOrWhiteSpace(sortBy))
    {
        // Default sorting by ProductCode
        return SortBy(products, x => x.ProductCode, sortDescending);
    }

    return sortBy.ToLower() switch
    {
        "productcode" => SortBy(products, x => x.ProductCode, sortDescending),
        "productname" => SortBy(products, x => x.ProductName, sortDescending),
        "pricewithoutvat" => SortBy(products, x => x.PriceWithoutVat ?? 0, sortDescending),
        "purchaseprice" => SortBy(products, x => x.ErpPrice?.PurchasePrice ?? 0, sortDescending),
        "manufacturedifficulty" => SortBy(products, x => x.ManufactureDifficulty ?? 0, sortDescending),
        // M0-M3 margin levels - amounts (using pre-calculated data)
        "m0amount" => SortBy(products, x => x.Margins.Averages.M0.Amount, sortDescending),
        "m1amount" => SortBy(products, x => x.Margins.Averages.M1.Amount, sortDescending),
        "m2amount" => SortBy(products, x => x.Margins.Averages.M2.Amount, sortDescending),
        "m3amount" => SortBy(products, x => x.Margins.Averages.M3.Amount, sortDescending),
        // M0-M3 margin levels - percentages (using pre-calculated data)
        "m0percentage" => SortBy(products, x => x.Margins.Averages.M0.Percentage, sortDescending),
        "m1percentage" => SortBy(products, x => x.Margins.Averages.M1.Percentage, sortDescending),
        "m2percentage" => SortBy(products, x => x.Margins.Averages.M2.Percentage, sortDescending),
        "m3percentage" => SortBy(products, x => x.Margins.Averages.M3.Percentage, sortDescending),
        _ => SortBy(products, x => x.ProductCode, sortDescending)
    };
}
```
Every selector lambda is copied verbatim from the current arm it replaces — including the exact null-coalescing (`?? 0`) and null-conditional (`?.`) operators already present. No selector logic changes.

### Data Flow
Unchanged. `Handle` still calls `ApplySorting(filteredProducts, request.SortBy, request.SortDescending)`, still gets back an `IEnumerable<CatalogAggregate>` (now via the covariant `IOrderedEnumerable<CatalogAggregate>`), and still applies `.Skip().Take()` afterward. LINQ's deferred execution is preserved — sorting still only executes when the caller enumerates.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A selector lambda is transcribed incorrectly during the mechanical rewrite (e.g. dropping a `?? 0`), silently changing null-ordering behavior | Medium | Diff each rewritten arm against the original line-by-line; run the existing `GetProductMarginsHandlerTests` suite, which already covers default sort and unknown-field fallback, and add/keep coverage that exercises at least one amount field and one percentage field sort if not already present |
| Generic type inference fails for a mixed-nullable selector (e.g. `x.PriceWithoutVat ?? 0` — `decimal?` coalesced to `decimal`) | Low | `TKey` is inferred from the lambda's return expression exactly as C#'s target-typing already resolves it in the existing ternary arms; compiling the file is sufficient verification — if it doesn't compile, the type argument is visible in the build error and trivially fixed |
| Scope creep into `TopProductSorter`'s identical duplicate (Analytics module) | Low | Explicitly out of scope per this review's Decision 1 — leave `TopProductSorter` untouched; do not touch files outside `GetProductMarginsHandler.cs` |
| `dotnet format` reformats the collapsed single-line arms in an unexpected way | Low | Run `dotnet build` + `dotnet format` per `CLAUDE.md`'s validation checklist before completion, as normal |

## Specification Amendments
None. `spec.r1.md` already reflects the actual 15-occurrence count and already specifies the `IOrderedEnumerable<CatalogAggregate>` return type as preferred; this review confirms both and adds the explicit decision to keep the helper private/local rather than extracting a shared utility (Decision 1 above), and gives the planner the ready-to-follow full replacement body for `ApplySorting` under Implementation Guidance.

## Prerequisites
None. No migrations, no config, no infrastructure changes. Standard `dotnet build` + `dotnet format` + existing test suite (`backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`) is sufficient to validate.
