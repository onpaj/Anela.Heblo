# Development: Remove duplicate margin-total calculation in GetProductMarginSummaryHandler

## Summary

Implemented the fix exactly as specified in plan-01.md / design-01.md / architecture-01.md: replaced the
recomputed `CalculateTotalMarginForLevel(products, marginLevel)` call with the pre-computed group total
`kvp.Value` (already produced by `MarginCalculator.CalculateAsync` into `GroupTotals[groupKey]`), and deleted
the now-dead private method. Added one regression assertion to the existing test suite that specifically locks
in the fix.

## Files changed

1. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
   - Line 84: `var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);` → `var totalMarginForLevel = kvp.Value;`
   - Deleted the private `CalculateTotalMarginForLevel(List<AnalyticsProduct>, MarginLevel)` method and its XML
     doc comment (previously lines 122-130).
   - `marginLevel` parameter of `GenerateTopProducts` is left in place unused, per architecture-01.md's explicit
     guidance not to touch the method signature beyond what the finding specifies (private method, no public
     contract, C# doesn't warn on unused parameters by default).
   - No other lines touched.

2. `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`
   - Added 3 assertion lines to the existing `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`
     test. This test already mocks `IMarginCalculator.CalculateAsync` to return `GroupTotals = { ["PROD001"] = 500m }`,
     but previously never asserted that this value flows through to `TopProductDto.TotalMargin` — it only checked
     the handler-level `result.TotalMargin` (which comes from `calculationResult.TotalMargin`, a separate field,
     not from `GroupTotals`/`GenerateTopProducts` at all). I verified none of the pre-existing tests actually
     asserted on `TopProducts[].TotalMargin`, so this was a real gap, not a redundant addition.
   - New assertions: `result.TopProducts` contains exactly one entry, with `GroupKey == "PROD001"` and
     `TotalMargin == 500m`.
   - This is a meaningful regression check: in this test, `IMarginCalculator.GetMarginAmountForLevel` is *not*
     mocked/set up. Under the old (pre-fix) code, `CalculateTotalMarginForLevel` would call
     `_marginCalculator.GetMarginAmountForLevel(p, marginLevel)` on the mock, which Moq would resolve to
     `default(decimal)` (`0`), making `TotalMargin` come out as `0`, not `500m`. So this assertion would have
     failed against the pre-fix code and now passes against the post-fix code — it directly proves the
     substitution is wired correctly and guards against reintroducing the duplicate calculation.

## Verification

- `grep -rn CalculateTotalMarginForLevel backend/` → no matches anywhere in the repo (confirmed no other
  callers existed before deletion, and none remain).
- Manual diff review: change is exactly the two-hunk, logic-preserving substitution described in the plan/design/
  architecture docs — no other lines modified, no formatting churn, no trailing-newline change (the file already
  lacked a trailing newline after the final `}` before this change; confirmed via `git show HEAD:<path>`).
- **Could not run `dotnet build` / `dotnet format` / `dotnet test` in this sandbox** — the `.NET SDK` is not
  installed in this worktree's environment (`dotnet` not found on `PATH`, no SDK directory found on the
  filesystem, and `docker`/`podman` are also unavailable here to build in a container). This is an environment
  limitation, not a code issue. **The pipeline's CI step (or a follow-up local run) must execute:**
  ```
  cd backend
  dotnet build
  dotnet format --verify-no-changes
  dotnet test --filter FullyQualifiedName~GetProductMarginSummaryHandlerTests
  dotnet test
  ```
  before merge, per the repo's stated validation requirements in CLAUDE.md.

## How to verify

1. `grep -rn CalculateTotalMarginForLevel backend/` — should return nothing.
2. `cd backend && dotnet build` — should succeed with no new warnings.
3. `dotnet test --filter FullyQualifiedName~GetProductMarginSummaryHandlerTests` — all tests in this file
   (including the new assertions) should pass.
4. `dotnet test` — full backend suite should pass unmodified elsewhere (no other files touched).
5. `dotnet format --verify-no-changes` — should report no formatting differences.

## Scope confirmation

No changes to `MarginCalculator.cs`, DTOs, controllers, or any other file. No API/contract changes. No frontend
changes. Matches the plan/design/architecture exactly; no deviations.
