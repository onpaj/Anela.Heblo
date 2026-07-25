# Review: Remove duplicate margin-total calculation in GetProductMarginSummaryHandler

## Verdict: done

## What was checked

1. **Spec conformance** — Finding required replacing `CalculateTotalMarginForLevel(products, marginLevel)` with
   `kvp.Value` and deleting the now-dead private method. Diff (`git show HEAD`) shows exactly this: one line
   changed (`GetProductMarginSummaryHandler.cs:84`), and the 9-line private method + its XML doc comment deleted.
   No other lines in the handler touched.

2. **Architecture adherence** — architecture-01.md approved the plan as-is with no changes required; design-01.md
   scoped this as internal-only with no DTO/contract impact. Implementation matches: no API/contract/DTO changes,
   `GetProductMarginSummaryResponse`/`TopProductDto`/controller untouched.

3. **Correctness** — Confirmed by re-reading both source files:
   - `MarginCalculator.CalculateAsync` accumulates `totalSold * GetMarginAmountForLevel(product, marginLevel)`
     into `groupTotals[groupKey]` for the same product set (`GroupProducts[key]`) and `marginLevel` that
     `GenerateTopProducts` uses (`request.MarginLevel`, passed through unchanged). `kvp.Value` is therefore
     mathematically identical to what `CalculateTotalMarginForLevel` used to recompute — a true logic-preserving
     substitution, not just a plausible one.
   - `grep -rn CalculateTotalMarginForLevel backend/` → no matches; confirms the deleted method had no other
     callers and nothing was left dangling.

4. **Test coverage** — A regression assertion was added to the existing
   `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` test asserting
   `result.TopProducts[0].TotalMargin == 500m` (the pre-seeded `GroupTotals["PROD001"]` value). I verified this is
   a genuine regression check and not padding: in this test, `IMarginCalculator.GetMarginAmountForLevel` is never
   mocked, so under the pre-fix code Moq would resolve it to `default(decimal)` (0), making the old
   `CalculateTotalMarginForLevel` recomputation yield `0m` — the assertion would have failed against the old code
   and passes against the new code. This directly exercises the fixed code path and guards against regression.

5. **Scope discipline** — Only the handler and its test file changed (12 lines in handler: -1/+1 substitution and
   -10 dead method; +6 lines in test). No unrelated formatting, no touched `MarginCalculator.cs`, no frontend
   changes. Matches "surgical changes" project guidance.

## Note on validation gap

Neither the development step nor this review step had `dotnet` available in the sandbox (`dotnet: command not
found`, no SDK on `PATH`, no container runtime available). `dotnet build` / `dotnet format` / `dotnet test` could
not be executed locally in either step. Given the change is a mechanically verifiable, logic-preserving
substitution (confirmed above by direct code inspection and the existing/added test coverage), this is flagged as
an environment limitation for CI to confirm, not treated as a blocking correctness concern for this review.

## Cleanup suggestions (non-binding)

None beyond what's already noted — the change is minimal and precisely scoped to the finding.
