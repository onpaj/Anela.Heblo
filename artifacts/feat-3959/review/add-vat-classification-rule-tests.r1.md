# Code Review: add-vat-classification-rule-tests

## Summary
The implementation adds `VatClassificationRuleTests.cs` with 14 tests (10 theory rows + 4 facts) covering all functional requirements FR-3 through FR-6 of `spec.r1.md`, plus a property-metadata fact needed to close a coverage gap discovered during verification. Structure, namespace, and fixture usage match the sibling test classes exactly as required by FR-1.

## Review Result: PASS

### task: add-vat-classification-rule-tests
**Status:** PASS

## Docs to Update
(None — this is a pure test-authoring change with no public behavior, CLI, or documented-concept changes.)

## Overall Notes
- Verified independently: `dotnet test --filter "FullyQualifiedName~VatClassificationRuleTests"` → 14/14 passed.
- Verified coverage: `coverage.cobertura.xml` shows `line-rate="1"` (100%) for `VatClassificationRule.cs` — all 6 executable lines (3 property getters + 3 lines of `Evaluate`) hit, satisfying NFR-4. The added `Properties_ReturnExpectedMetadata` fact was necessary — the initial coverage run (before adding it) showed only 50% line-rate, confirming the property getters are not auto-covered by constructing `_sut` alone in this codebase's coverage instrumentation.
- Verified full backend suite: Total 6690, Passed 6581, Failed 105, Skipped 4 — the 105 failures are the same pre-existing `System.ArgumentException: Docker is either not running or misconfigured` Postgres-testcontainer failures established as the sandbox baseline by the prior `extend-invoice-classification-fixture` task (baseline: Total 6677, Failed 105). The full-suite run was executed with 13 `VatClassificationRuleTests` in place (before the 14th `Properties_ReturnExpectedMetadata` fact was added to close the coverage gap), so 6677 + 13 = 6690 reconciles exactly, and the class-level filtered run afterward confirms all 14 tests (including the property fact) pass. Failed count unchanged at 105 in both runs — no regression.
- Verified `dotnet build` (0 errors) and `dotnet format --verify-no-changes` (no diffs) on the new file.
- FR-3/FR-4/FR-5/FR-6 acceptance criteria each traced to a specific named fact or commented inline-data row, consistent with the task-context's explicit allowance.
- No placeholder/TODO language; no scope creep beyond the task-context's Step 6 fallback (explicitly anticipated by the task-context itself).
