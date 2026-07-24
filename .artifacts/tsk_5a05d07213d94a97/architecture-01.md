# Architecture Assessment: Remove duplicate margin-total calculation in GetProductMarginSummaryHandler

## Verdict

Approved as scoped. This is a single-file, two-hunk change with no architectural surface — no new component,
no interface change, no data-flow change. The plan and design produced in prior steps are accurate (verified
against current source below) and require no correction. This document exists to record that verification and to
give the implementer explicit go/no-go checks, per the arch-review process — it does not redirect the approach.

## Verification against current source

Read directly (not from memory of the finding):

- `GetProductMarginSummaryHandler.cs:84` — `CalculateTotalMarginForLevel(products, marginLevel)` is called exactly
  once, inside the `GroupTotals.Select(kvp => ...)` projection, with `kvp` in scope.
- `GetProductMarginSummaryHandler.cs:125-130` — the private method, confirmed to duplicate
  `MarginCalculator.CalculateAsync`'s per-product formula (`totalSold * GetMarginAmountForLevel(product,
  marginLevel)`, summed) verbatim.
- `MarginCalculator.cs:60-81` — `CalculateAsync` computes this same sum while streaming products, keyed by
  `groupKey`, into `groupTotals[groupKey]`, using the **same** `marginLevel` passed through from
  `request.MarginLevel` (handler line 45) — not a different level.
- `MarginCalculator.cs:78-79` — `groupProducts[groupKey]` is populated with exactly the same products that
  contributed to `groupTotals[groupKey]` (same loop, same `MarginAmount > 0` filter applied before either
  dictionary is touched). This is the fact that makes the two computations provably equivalent, not just
  incidentally equal: `calculationResult.GroupProducts[kvp.Key]` (read at handler line 78, passed into
  `CalculateTotalMarginForLevel`) is the identical product set that was summed into `kvp.Value`.
- `grep -rn CalculateTotalMarginForLevel backend/` — exactly 2 hits, both in this file (the call site and the
  declaration). No test references the method by name; no other production caller exists. Safe to delete outright.
- `GetProductMarginSummaryHandlerTests.cs` — existing coverage already exercises the code path this change
  touches: line 185 seeds a mocked `IMarginCalculator.CalculateAsync` result with
  `GroupTotals = { ["PROD001"] = 500m }`, and line 278 asserts `result.TotalMargin.Should().Be(500m)`. Under the
  current (buggy-in-spirit, not-buggy-in-output) code, that assertion only passes because
  `CalculateTotalMarginForLevel` happens to be computed from a *separately mocked* `GroupProducts` list that
  independently sums to 500m — i.e., the test setup already keeps both values consistent. After the fix, this
  same test now more directly proves the substitution is wired correctly, since `TotalMargin` will come straight
  from the seeded `GroupTotals` value with no independent recomputation. This is a meaningful regression check,
  not an incidental one — worth calling out to the implementer so they don't dismiss it as unrelated.

No discrepancy found between the finding, the plan, the design, and the current state of the code.

## Alignment with existing patterns

- The handler already follows the "streaming architecture, calculation extracted to `IMarginCalculator`" pattern
  documented in its own header comment (`GetProductMarginSummaryHandler.cs:8-11`). `MarginCalculator` is the
  established single source of truth for margin math; `GenerateTopProducts` is meant to be a thin
  presentation/shaping layer over `MarginCalculationResult`. The fix restores that intended boundary rather than
  crossing it — it removes an accidental second implementation of calculator logic that leaked into the handler.
- `GetGroupAggregatedMarginData` (M0-M2 weighted averages) is a *different* calculation with no precomputed
  equivalent in `MarginCalculationResult` — correctly left untouched by both the finding and this assessment. Do
  not conflate it with `CalculateTotalMarginForLevel`; only the latter is redundant.
- No DTO, contract, or persistence rule from `docs/architecture/development_guidelines.md` is implicated — confirms
  the design doc's own scope note.

## Implementation guidance

Single file: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`.

1. Line 84: replace
   `var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);`
   with
   `var totalMarginForLevel = kvp.Value;`
2. Delete lines 122-130 (the XML doc comment + `CalculateTotalMarginForLevel` method body) entirely.
3. The `marginLevel` parameter of `GenerateTopProducts` becomes unused by this line, but it's still required by
   the method signature — check whether it's used elsewhere in the method body before considering removing it
   from the signature. (It is not otherwise used post-fix per the current file; however, changing the method
   signature is **out of scope** — leave the parameter in place unless a compiler warning forces the question,
   since `GenerateTopProducts` is a private method whose signature isn't part of any public contract, and trimming
   it isn't part of this finding. Do not do speculative cleanup beyond what's specified.)
4. No changes anywhere else — not in `MarginCalculator.cs`, not in DTOs, not in the controller, not in tests.

## Risks and mitigations

- **Risk**: `GroupProducts[key]` and the products actually summed into `GroupTotals[key]` silently diverge in the
  future (e.g., someone adds post-filtering to `GroupProducts` without touching `GroupTotals`, or vice versa).
  - **Mitigation**: none needed for this change — it doesn't introduce the risk, it just stops paying to
    "re-verify" a value that was already trustworthy by construction (both dictionaries are populated in the same
    loop iteration in `MarginCalculator.CalculateAsync`). If this divergence risk matters going forward, it's an
    argument for keeping `CalculateAsync`'s two dictionaries populated in lockstep (as today), not for retaining
    the deleted method as a defensive recomputation — a recomputation that doesn't independently re-derive its
    inputs (it reads the same `GroupProducts` list) provides no actual safety net anyway.
- **Risk**: unused-parameter or unused-method compiler/analyzer warnings after deletion.
  - **Mitigation**: `dotnet build` (per repo validation rules) will surface this immediately; low likelihood given
    `marginLevel` remains a legitimate parameter of a private method that may be extended later, and most C#
    analyzers don't flag unused parameters by default.

## Prerequisites before implementation begins

None. No schema migration, no feature flag, no coordination with other in-flight work. Implementation can proceed
directly per the plan's rough-plan section (steps 1-7), using this document's line references as the authoritative
current-state confirmation.
