All 4 tasks complete. Here is the output summary.

---

# Implementation: Consolidate Margin-Level Resolution in GetProductMarginSummaryHandler

## What was implemented

Removed the duplicated `M0/M1/M2` switch in `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` and delegated to the already-injected `_marginCalculator.GetMarginAmountForLevel`. Two characterization tests were written first (against the original inline switch) to lock the existing semantics — case-insensitive resolution and silent M2 fallback — then the refactor was applied, proving behavior is preserved.

One correction vs. the plan: `[InlineData("mI")]` was replaced with `[InlineData("m1")]` in the case-insensitive test — `"mI".ToUpperInvariant()` yields `"MI"` (capital-I, not digit-1), which would fall through to M2 rather than match M1, contradicting the test assertion of 300m.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — `CalculateTotalMarginForLevel` (lines 217–237) replaced with a 3-line LINQ delegation to `_marginCalculator.GetMarginAmountForLevel`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — two new `[Theory]` test methods appended to the existing class

## Tests

- `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` — `[InlineData("M1")]`, `[InlineData("m1")]`; both must yield `300m` (15 units × M1Amount 20m)
- `Handle_UnknownMarginLevel_FallsBackToM2` — `[InlineData("M9")]`, `[InlineData("")]`, `[InlineData("xyz")]`; all must yield `450m` (15 units × M2Amount 30m)
- Full Analytics suite: **69 passed, 0 failed**

## How to verify

```bash
# From worktree root
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build --filter "FullyQualifiedName~Features.Analytics"
grep -rn '"M0" =>' backend/src/Anela.Heblo.Application/Features/Analytics/
# Expected: exactly 1 hit in MarginCalculator.cs
```

## Notes

- The 38 failures in the full test run are pre-existing Testcontainers/PostgreSQL infrastructure tests failing because Docker is not running — unrelated to this change.
- `MarginCalculator.cs` and `IMarginCalculator` are unchanged (FR-3 preserved).
- No DI changes, no DTO changes, no frontend impact.

## PR Summary

Removes the duplicated `M0/M1/M2` switch in `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` by delegating to the already-injected `IMarginCalculator.GetMarginAmountForLevel`. Margin-level resolution now has a single source of truth in `MarginCalculator`. Two characterization tests lock the existing semantics (case-insensitive resolution, silent M2 fallback) both before and after the refactor.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — `CalculateTotalMarginForLevel` body replaced with a 3-line LINQ + `_marginCalculator.GetMarginAmountForLevel` delegation
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — added `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` and `Handle_UnknownMarginLevel_FallsBackToM2` theory tests

## Status

DONE