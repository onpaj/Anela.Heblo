# Code Review: add-vatratecalculator-unit-tests

## Summary

The implementation adds exactly the test file specified in the task context, covering both branches of `VatRateCalculator.FromPrices` plus a verified rounding edge case. Production code is untouched, matching the spec's explicit out-of-scope constraint. All 5 new test cases pass, and the full `ProductPricing` suite (123 tests) passes with them included.

## Review Result: PASS

### task: add-vatratecalculator-unit-tests
**Status:** PASS

## Docs to Update

(none — test-only change, no public behavior, CLI, or docs-relevant surface touched)

## Overall Notes

- FR-1 through FR-4 and NFR-1/NFR-2 from `spec.r1.md` are all satisfied by the 5 `[InlineData]` cases; verified the rounding-edge expectation (`211, 200 -> 6`) independently against `decimal.ROUND_HALF_EVEN` semantics rather than trusting the task context's literal value on faith.
- The developer found that `dotnet build` currently fails on this branch (and on `origin/main` at the branch's merge-base) due to a pre-existing, unrelated bug in `RecurringJobSeeder.cs` (introduced by PR #4324 / issue #4318, commit `882659fe`): `HasSeededFieldsChanged(existing, config)` passes the wrong variable (`existing`, a list, instead of `existingConfig`). This blocks compilation of `Anela.Heblo.Application` and therefore `Anela.Heblo.Tests` for every branch cut from current `main`.
- Correct call not to fix it here: it is unrelated to this issue's scope (`VatRateCalculator`/`ProductPricing`), and bundling an unrelated fix into a test-coverage PR would violate this repo's surgical-changes convention. The developer verified this task's deliverable via a local-only, uncommitted patch (reverted before commit — confirmed `RecurringJobSeeder.cs` has no diff in this branch) and flagged the finding clearly in the impl artifact for a human to act on separately. This is a repo-wide blocker, not a defect in this task's deliverable, so it does not affect this task's PASS status.

## Correction (added by /rework-pr, 2026-09-28)

The claim above — that the `RecurringJobSeeder.cs` patch was reverted and "has no diff in this branch" — did **not** hold for the PR as ultimately pushed. A later `/rework-pr` run committed that same one-line fix directly to `RecurringJobSeeder.cs` to unblock this PR's own CI (the branch could not build, and therefore could not run this task's own tests, without it). The PR's diff does include this change; it is now disclosed in the PR title/body rather than left as an undocumented, unrelated production change. This note is left as an addendum, not a rewrite, so the review's original record stays intact and traceable.
