# Architecture Review: Extract margin aggregation and sorting logic from GetProductMarginSummaryHandler

## Skip Design: true

## Architectural Fit Assessment

This is a pure internal refactor with no UI, no contract change, and no new endpoint. It fits the existing Analytics module conventions exactly — it does not introduce a new pattern, it completes one that's already half-applied.

The module already extracted two comparable concerns out of handlers into named `Services/` classes with `I{Name}` interfaces, scoped DI registration in `AnalyticsModule.cs`, and constructor injection:

- `IMarginCalculator` / `MarginCalculator.cs` — margin math (`CalculateAsync`, `GetMarginAmountForLevel`, `CalculateForProduct`, `GetGroupKey`, `GetGroupDisplayName`)
- `IMonthlyBreakdownGenerator` / `MonthlyBreakdownGenerator.cs` — monthly rollup, itself depending on `IMarginCalculator`

`GetProductMarginSummaryHandler.cs` (242 lines, verified by direct read) still has two private methods that don't fit this pattern:
- `CalculateGroupMarginData` (lines 122–158) — weighted-average margin math, the same *kind* of concern `IMarginCalculator` already owns, just not yet moved there.
- `ApplySorting` (lines 163–215) — a 13-branch switch, self-contained, no dependencies, currently untestable in isolation.

The spec's Option B (new `IMarginCalculator.GetGroupAggregatedMarginData`, new `ITopProductSorter` service) is the correct call — it finishes the existing pattern rather than inventing a third convention (a static helper, as Option A proposes, would be the first static utility in a module that otherwise uses DI services everywhere, and is explicitly rejected by `development_guidelines.md`'s "Don't create shared services" framing only in the sense that ad hoc statics fragment the pattern, not services themselves).

**Verified against `docs/architecture/development_guidelines.md` and `docs/architecture/filesystem.md`:** `Services/` is the documented location for "Domain services and business logic" within a feature (`filesystem.md` §Complex Features). DI registration in the feature's own `{Feature}Module.cs` is the mandatory pattern (`development_guidelines.md` §Dependency Injection Patterns, ADR-004). Nothing here touches `Contracts/`, module boundaries, persistence, or user identity — none of the module-boundary or identity ADRs apply.

## Proposed Architecture

### Component Overview

```
GetProductMarginSummaryHandler (orchestration only)
        │
        ├── IAnalyticsRepository            (unchanged)
        ├── IMarginCalculator                (existing — GAINS GetGroupAggregatedMarginData)
        │       └── MarginCalculator          (existing impl, extended)
        ├── IMonthlyBreakdownGenerator        (unchanged)
        ├── ITopProductSorter                (NEW)
        │       └── TopProductSorter          (NEW impl)
        └── TimeWindowParser                 (unchanged)
```

No new cross-module edges, no new contracts, no new endpoints. Both new/extended members are additive to interfaces already consumed only within the Analytics module.

### Key Design Decisions

#### Decision 1: Where `GetGroupAggregatedMarginData` lives
**Options considered:** (a) new standalone `IGroupMarginAggregator` service; (b) method on existing `IMarginCalculator`.
**Chosen approach:** (b), per spec FR-1.
**Rationale:** `IMarginCalculator` is already the single owner of margin math with four methods of similar shape (`CalculateAsync`, `CalculateForProduct`, `GetMarginAmountForLevel`, `GetGroupKey`/`GetGroupDisplayName`). Splitting weighted-average-by-group into a fourth interface for one more method would fragment a concern that already has one home, and would create exactly the kind of two-parallel-implementations risk the brief is trying to eliminate (just relocated one interface further). Confirmed no other Analytics handler needs this specific aggregation, so no interface-bloat concern for other consumers.

#### Decision 2: `GroupMarginData` DTO placement and visibility
**Options considered:** (a) leave as `internal` in a new file in `Services/`; (b) make `public` in `MarginCalculator.cs` or an adjacent file.
**Chosen approach:** (b) — public, in `Services/`, per spec.
**Rationale:** It must cross the `IMarginCalculator` interface boundary back into `GetProductMarginSummaryHandler.cs` in the `UseCases/` folder — `internal` would not be visible there since `internal` is assembly-scoped, which happens to work today only because handler and service are in the same assembly (`Anela.Heblo.Application`) but relies on nobody tightening visibility later. Making it `public` is correct and matches the visibility of `MarginCalculationResult` (already public, same folder, same pattern) — verified in `Services/MarginCalculationResult.cs`.
**Note for implementer:** put `GroupMarginData` in its own file (`Services/GroupMarginData.cs`) rather than appended to the bottom of `MarginCalculator.cs`, consistent with `MarginCalculationResult` already being split into its own file rather than living inside `MarginCalculator.cs`. This is a minor deviation from the spec's "in `MarginCalculator.cs` (or an adjacent file)" wording — the adjacent-file option is the one to take, since it matches what's already there for the sibling result-DTO.

#### Decision 3: `ITopProductSorter` as DI-registered service vs. static helper
**Options considered:** Option A (static `TopProductSorter.Sort(...)`) vs. Option B (DI-registered `ITopProductSorter`).
**Chosen approach:** Option B, per spec and brief's stated preference.
**Rationale:** The module has zero static-helper business-logic utilities today — every extracted concern (`IMarginCalculator`, `IMonthlyBreakdownGenerator`, `IProductFilterService`, `IReportBuilderService`) is an interface + scoped registration. A static helper here would be the outlier, not the norm, and would make this one piece of logic harder to substitute in tests relative to everything around it (even though, being pure/stateless, it wouldn't strictly need DI). Consistency with the established pattern wins over the marginal simplicity of a static method.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Analytics/
├── Services/
│   ├── MarginCalculator.cs          # MODIFIED: + GetGroupAggregatedMarginData
│   ├── GroupMarginData.cs           # NEW: public class, moved verbatim from handler file
│   ├── TopProductSorter.cs          # NEW: ITopProductSorter + TopProductSorter
│   ├── MonthlyBreakdownGenerator.cs # unchanged (reference pattern)
│   └── ...
├── UseCases/GetProductMarginSummary/
│   └── GetProductMarginSummaryHandler.cs  # MODIFIED: methods removed, ctor gains ITopProductSorter
└── AnalyticsModule.cs                # MODIFIED: + services.AddScoped<ITopProductSorter, TopProductSorter>();
```

Register the new service in `AnalyticsModule.cs` immediately after the existing `IMonthlyBreakdownGenerator` line (line 49), keeping the three Analytics-owned services adjacent:

```csharp
services.AddScoped<TimeWindowParser>();
services.AddScoped<IMarginCalculator, MarginCalculator>();
services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
services.AddScoped<ITopProductSorter, TopProductSorter>();
```

### Interfaces and Contracts

```csharp
// IMarginCalculator — new member
GroupMarginData GetGroupAggregatedMarginData(List<AnalyticsProduct> products);

// New interface, Services/TopProductSorter.cs
public interface ITopProductSorter
{
    List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending);
}
```

`GroupMarginData` keeps its exact current shape (8 decimal properties), only its accessibility changes (`internal` → `public`) and its file location (`UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` → `Services/GroupMarginData.cs`).

`GetProductMarginSummaryHandler`'s constructor gains one parameter, appended last to match the existing four-parameter ordering convention (repository, calculators/generators, parser last) — put `ITopProductSorter` after `TimeWindowParser` or grouped with the other calculator-style services; either position is acceptable since C# constructor parameter order isn't semantically load-bearing here, but grouping it with `IMarginCalculator`/`IMonthlyBreakdownGenerator` (before `TimeWindowParser`) reads better since all three are "calculation collaborators" and `TimeWindowParser` is the odd one out (parses input, doesn't touch products).

### Data Flow

Unchanged end-to-end shape; only internal delegation changes:

1. `Handle` → `_marginCalculator.CalculateAsync(...)` (unchanged) → `GenerateTopProducts` (handler, retained).
2. Inside `GenerateTopProducts`, per group: `_marginCalculator.GetGroupAggregatedMarginData(products)` replaces the private `CalculateGroupMarginData(products)` call — same input, same output type, same call site (line 78 in current file).
3. After building `topProductsWithData`, `_topProductSorter.Sort(topProductsWithData, sortBy, sortDescending)` replaces the private `ApplySorting(...)` call (line 108).
4. Rank assignment loop (lines 111–114) stays in the handler — it's a one-line mapping step, not a calculation, consistent with FR-3's scope boundary.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Second test call site (`GetProductMarginSummaryHandlerTests.cs` line 249) constructs the handler with `marginCalculatorMock.Object` (a `Mock<IMarginCalculator>`) — if that test's flow reaches `GenerateTopProducts`, the mock must now also stub `GetGroupAggregatedMarginData`, or the call returns `null`/default and the test breaks with a NullReferenceException rather than a clear assertion failure | Medium | Before merging, run the full `GetProductMarginSummaryHandlerTests.cs` suite locally; if the mocked-calculator test path touches `GenerateTopProducts`, add a `.Setup(x => x.GetGroupAggregatedMarginData(...))` stub matching the spec's mandate that all call sites are updated (already captured in spec FR-2 acceptance criteria, but the specific mock-setup mechanics weren't spelled out — flag here so it isn't missed) |
| `GroupMarginData` visibility change (`internal` → `public`) technically exports a class that was previously invisible outside the assembly | Low | Assembly is `Anela.Heblo.Application`, already fully internal to the backend solution; no OpenAPI/contract surface is touched (confirmed: `GroupMarginData` is never referenced by `Contracts/` or by the API project) |
| Byte-for-byte behavior parity for the weighted-average and 13-branch sort logic must hold — any transcription slip (e.g. mis-copying a `switch` arm) silently changes report output with no compiler error | Medium | This is why the spec mandates "verbatim move" — do a mechanical cut-paste rather than a rewrite; the acceptance criteria already require new direct unit tests against both `MarginCalculator.GetGroupAggregatedMarginData` and `TopProductSorter.Sort` covering all branches, which is the correct verification net |

## Specification Amendments

None required. The spec (FR-1/FR-2/FR-3) is architecturally sound and already matches the module's established service-extraction pattern in every material respect (interface location, DI registration style, constructor injection, real-instance-in-tests convention). Two clarifications worth folding in if the spec is revised, otherwise leave as implementer judgment calls:

1. **File placement for `GroupMarginData`**: spec says "into `MarginCalculator.cs` (or an adjacent file in `Services/`)" — recommend the adjacent-file option (`Services/GroupMarginData.cs`) to mirror `MarginCalculationResult.cs`'s existing separate-file placement, keeping `MarginCalculator.cs` itself free of unrelated DTO declarations.
2. **Constructor parameter ordering** for `ITopProductSorter` in `GetProductMarginSummaryHandler` — recommend grouping it with `IMarginCalculator`/`IMonthlyBreakdownGenerator` rather than appending after `TimeWindowParser`, for readability only; not a correctness requirement.

## Prerequisites

None. No migrations, no config, no new infrastructure. This can start immediately: it only touches `backend/src/Anela.Heblo.Application/Features/Analytics/Services/`, `.../UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`, `AnalyticsModule.cs`, and the two test files already identified (`GetProductMarginSummaryHandlerTests.cs`, `MarginCalculatorTests.cs`), plus a new `TopProductSorterTests.cs` alongside them.
