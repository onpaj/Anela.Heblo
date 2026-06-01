# Architecture Review: Move `MarginCalculationResult` from Domain to Application layer

## Skip Design: true

Backend-only structural refactor. No UI components, screens, layouts, or visual decisions involved. Pure namespace/file relocation with byte-identical type shape.

## Architectural Fit Assessment

The proposed change is correct in direction and well-scoped. Verified against `docs/architecture/filesystem.md:76-80` (Clean Architecture project layers) and the existing Analytics module structure:

- `Anela.Heblo.Domain` is documented as "entities, domain services, repository interfaces." `MarginCalculationResult` is none of these — it is the shape produced by `IMarginCalculator.CalculateAsync()` (an Application-layer service at `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs:5-22`) and consumed only by `GetProductMarginSummaryHandler` and `MonthlyBreakdownGenerator` (both Application layer).
- The proposed destination — `Application/Features/Analytics/Services/` — is exactly where sibling service-result types in the same module live. `MarginCalculator.cs` and `MonthlyBreakdownGenerator.cs` are already in that folder under the namespace `Anela.Heblo.Application.Features.Analytics.Services`.
- The spec correctly preserves the legitimate Domain types in `AnalyticsProduct.cs` (`AnalyticsProduct`, `SalesDataPoint`, `DateRange`) and does not conflate the unrelated `MarginCalculationResult` at `Application/Features/Catalog/Services/SafeMarginCalculator.cs:53` (verified — different shape with `IsSuccess`/`Margin`/`ErrorMessage` and static factory methods, different namespace).

**Integration points** (all verified):
- `Application/Features/Analytics/Services/MarginCalculator.cs` — interface return type + implementation return type (same namespace post-move → no `using` change).
- `Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — interface parameter type + implementation parameter type (same namespace post-move → no `using` change).
- `Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — local parameter type at line 67. Already imports `Anela.Heblo.Application.Features.Analytics.Services` at line 3, so no `using` change needed.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — instantiates the type at line 183. Already imports `Anela.Heblo.Application.Features.Analytics.Services` at line 9, so no `using` change needed.

**Net result:** all four consumer files require zero `using`-directive changes. The move is mechanically simpler than the spec anticipates.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application                                                │
│ └── Features/Analytics/                                                │
│     ├── Services/                                                      │
│     │   ├── IMarginCalculator + MarginCalculator                       │
│     │   │       returns ───────────────────────────────┐               │
│     │   ├── IMonthlyBreakdownGenerator + ...           │               │
│     │   │       consumes ──────────────────────────┐   │               │
│     │   └── MarginCalculationResult  ◄─────────────┴───┘   (NEW HOME)  │
│     │           │                                                      │
│     │           └─ depends on ── AnalyticsProduct (Domain)  ✓ correct  │
│     └── UseCases/GetProductMarginSummary/                              │
│         └── GetProductMarginSummaryHandler  ── consumes ──┘            │
└────────────────────────────────────────────────────────────────────────┘
                                  │  depends on (one direction)
                                  ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Domain                                                     │
│ └── Features/Analytics/AnalyticsProduct.cs                             │
│     ├── AnalyticsProduct                                               │
│     ├── SalesDataPoint                                                 │
│     └── DateRange    (MarginCalculationResult removed from this file)  │
└────────────────────────────────────────────────────────────────────────┘
```

Application → Domain dependency restored cleanly. Domain no longer references any Application-organizational shape.

### Key Design Decisions

#### Decision 1: Standalone file vs. nested in `MarginCalculator.cs`

**Options considered:**
1. New standalone file `Services/MarginCalculationResult.cs` (the spec's choice, also suggested by the brief).
2. Inline the type into `Services/MarginCalculator.cs` directly below the `MarginCalculator` class.
3. Nest as `MarginCalculator.Result` inside the implementation.

**Chosen approach:** Option 1 — standalone file.

**Rationale:** The type is consumed by *two* services (`MarginCalculator` produces it, `MonthlyBreakdownGenerator` consumes it). Co-locating it inside one of them creates an asymmetry where one service "owns" the shared type. The Services folder already follows a one-public-type-per-file convention (`ProductFilterService.cs`, `ReportBuilderService.cs`, `TimeWindowParser.cs`, `ReportBuilderService.cs`). A separate file matches this convention and keeps file-level grep/IDE navigation predictable. Nesting (Option 3) breaks the existing pattern and complicates the test arrange block at `GetProductMarginSummaryHandlerTests.cs:183`.

#### Decision 2: Preserve dictionary-based shape vs. introduce richer type now

**Options considered:**
1. Move byte-identical (spec's choice).
2. Replace `Dictionary<string, decimal>` + `Dictionary<string, List<AnalyticsProduct>>` with `IReadOnlyList<MarginGroup>` while we're touching it.

**Chosen approach:** Option 1.

**Rationale:** Mixing structural relocation with shape redesign turns a low-risk move into a behavior-affecting change. The spec correctly defers the richer-type redesign as out-of-scope. Note this is the same opinion the brief expresses ("Worth doing later but not in this change").

#### Decision 3: How to keep the consumer-file impact minimal

**Chosen approach:** Place the new type in the **same namespace** as the existing `IMarginCalculator` and `IMonthlyBreakdownGenerator` (`Anela.Heblo.Application.Features.Analytics.Services`).

**Rationale:** Since all consumers either already use that namespace (Handler, tests) or *are* in it (the two services themselves), no `using` directive needs to be added or removed in any file. The Domain `using` in those consumers must stay — they still need `AnalyticsProduct`, `DateRange`, `ProductGroupingMode`. This eliminates an entire class of mistakes around stale or duplicated using statements.

## Implementation Guidance

### Directory / Module Structure

**Create one file, edit four:**

```
NEW    backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculationResult.cs
EDIT   backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs    (delete lines 56-64)
EDIT   docs/superpowers/plans/2026-05-27-margin-calculator-abstractions.md       (line 7 + line 1076)
```

No other files require code changes (verified — all four consumer files already have the right `using` directive). Editing them solely to "refresh" usings is forbidden by the surgical-changes rule in `CLAUDE.md`.

### Interfaces and Contracts

The new file should contain exactly:

```csharp
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// Result object for margin calculations
/// </summary>
public class MarginCalculationResult
{
    public required Dictionary<string, decimal> GroupTotals { get; init; }
    public required Dictionary<string, List<AnalyticsProduct>> GroupProducts { get; init; }
    public required decimal TotalMargin { get; init; }
}
```

The `using Anela.Heblo.Domain.Features.Analytics;` is necessary because `AnalyticsProduct` is referenced in the `GroupProducts` property type. No file-scoped namespace alternative changes the semantics.

The `AnalyticsProduct.cs` deletion must remove lines 56-64 inclusive (the `/// <summary>` comment plus the class body) and leave a single trailing newline after `DateRange` (line 54). Do not delete `DateRange`.

### Data Flow

Unchanged. The runtime flow remains:

```
AnalyticsRepository.StreamProductsWithSalesAsync
        │
        ▼  IAsyncEnumerable<AnalyticsProduct>
MarginCalculator.CalculateAsync ─────────► MarginCalculationResult
                                                  │
                ┌─────────────────────────────────┴─────────────────────────┐
                ▼                                                           ▼
GetProductMarginSummaryHandler.GenerateTopProducts        MonthlyBreakdownGenerator.Generate
                │                                                           │
                ▼                                                           ▼
        TopProductDto list                                  MonthlyProductMarginDto list
                                          │
                                          ▼
                          GetProductMarginSummaryResponse
```

Only the namespace of the in-flight `MarginCalculationResult` instance changes; behavior, lifecycle, and identity are byte-identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer deletes more than `MarginCalculationResult` from `AnalyticsProduct.cs` (e.g. also removes `DateRange` or `SalesDataPoint`) | High | Mandate the post-state check from the spec — `AnalyticsProduct.cs` must end at line 54 with `public record DateRange(DateTime FromDate, DateTime ToDate);` and contain `AnalyticsProduct`, `SalesDataPoint`, `DateRange` and nothing else. Add a grep gate: `grep -c "class MarginCalculationResult" Domain/Features/Analytics/AnalyticsProduct.cs` must return `0`. |
| Confusion with the Catalog `MarginCalculationResult` causes the wrong file to be edited | Medium | The Catalog type is in `Application/Features/Catalog/Services/SafeMarginCalculator.cs` and uses `IsSuccess`/`Margin`/`ErrorMessage` fields plus static factory methods. Reject any change touching that file. Add a grep gate: `grep -n "class MarginCalculationResult" backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs` must still return a match at the original line. |
| Implementer adds an unneeded `using Anela.Heblo.Application.Features.Analytics.Services;` to files that already have it | Low | Spec explicitly directs to check each file first; CLAUDE.md "surgical changes" rule already covers this. |
| Implementer changes the Handler or test files to remove the Domain `using` because "Domain isn't needed anymore" | Medium | The Domain `using` is still needed in all four consumer files for `AnalyticsProduct`, `DateRange`, and `ProductGroupingMode`. Spec's FR-2 acceptance criterion already addresses this but should be enforced by build (the build will fail loudly if removed). |
| Plan-file edit (FR-4) accidentally rewords surrounding plan content | Low | Use a targeted `Edit` tool call on the exact strings at line 7 and line 1076, not a regex or sed. |
| Hidden fully-qualified reference somewhere in the codebase (e.g. `Anela.Heblo.Domain.Features.Analytics.MarginCalculationResult`) | Low | Run a solution-wide grep for the fully-qualified string before declaring done. If hits exist, update each. |
| Build cache / IDE retains stale resolution | Low | Run `dotnet build` from a clean state (no `--no-incremental` needed; the `.cs` file move is detected). |

## Specification Amendments

The spec is solid. Two small refinements would tighten it:

1. **FR-2 — clarify that no `using` changes are needed.** The spec says "ensure `using Anela.Heblo.Application.Features.Analytics.Services;` is present (already is for `IMarginCalculator`)" for the Handler and "add … if not already present" for the tests. Both are *already present* (verified: Handler line 3, tests line 9). Tighten the language to "Verify the using is already present; no edit required" so the implementer does not perform a no-op edit just to "be safe" and accidentally touch surrounding lines.

2. **FR-1 — append explicit post-state of `AnalyticsProduct.cs`.** Add: "After the edit, `AnalyticsProduct.cs` ends at the line `public record DateRange(DateTime FromDate, DateTime ToDate);` with no trailing class declaration." This gives the implementer an exact, mechanically verifiable end-state.

3. **Add a validation step:** After implementation, run
   ```
   grep -rn "MarginCalculationResult" backend/src backend/test
   ```
   and confirm:
   - Exactly one declaration in `Application/Features/Analytics/Services/MarginCalculationResult.cs`.
   - Exactly one declaration in `Application/Features/Catalog/Services/SafeMarginCalculator.cs` (the unrelated Catalog type).
   - Zero declarations in `Anela.Heblo.Domain`.
   - Zero occurrences of the fully-qualified `Anela.Heblo.Domain.Features.Analytics.MarginCalculationResult` anywhere.

These are clarifications, not new requirements — the spec's behavior expectations are correct.

## Prerequisites

None. This is a self-contained refactor:

- No database migration.
- No DI registration change (the type is a return shape, never registered in the container; only `MarginCalculator` and `MonthlyBreakdownGenerator` are registered, and their registrations don't reference `MarginCalculationResult` by type).
- No NuGet package, infrastructure, or external-service dependency.
- No feature flag, OpenAPI regeneration, or frontend impact.
- No coordination with another in-flight branch is needed — but the implementer should still backmerge `main` before starting to pick up the recent `1d95ea75` and `98123e72` artifact commits and the `1d9aa863` AnalyticsProduct mapping helper extraction.

Validation gates before declaring done (per `CLAUDE.md`):
- `dotnet build` — passes for the whole solution.
- `dotnet format` — produces no changes.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Analytics"` — all Analytics tests green.
- The grep gates listed under "Specification Amendments" return the expected counts.