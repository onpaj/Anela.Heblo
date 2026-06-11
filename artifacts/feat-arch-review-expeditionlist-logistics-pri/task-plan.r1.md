Implementation plan saved to `docs/superpowers/plans/2026-06-10-remove-orderids-from-printpickinglistresult.md`.

The plan decomposes the spec into 7 tasks with checkbox-tracked steps:

1. **Baseline** — confirm the 4 adapter tests pass and solution builds clean before any edit.
2. **Repository search** — verify only the two expected `OrderIds` callsites exist (FR-3 pre-check).
3. **Delete the DTO property** — single-line removal from `PrintPickingListResult.cs`, then build to confirm the failure now points only at the test arrange line (intentional RED).
4. **Delete the test arrange line** — remove `OrderIds = new List<int> { 1, 2, 3 },` from `CreatePickingListAsync_TranslatesResultFields`, rebuild GREEN, run all 4 tests.
5. **Post-change grep** — confirm zero `OrderIds` references remain in the Logistics namespace, and `frontend/src/api/` is untouched.
6. **Project gates** — `dotnet build`, `dotnet format --verify-no-changes`, full `dotnet test`.
7. **Commit** — exact staged-diff verification (two hunks, two minus-lines) before commit.

Every step has the concrete command, expected output, and a stop-condition for unexpected results. Coverage check against the spec: FR-1 (Task 3), FR-2 (Task 4), FR-3 (Tasks 2 + 5), NFR-1/NFR-3 (Tasks 4.3 + 6.3), NFR-2 (Task 7.2 staged-diff guard).