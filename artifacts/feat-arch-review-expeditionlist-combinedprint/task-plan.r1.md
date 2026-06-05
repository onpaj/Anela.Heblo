Plan saved to `artifacts/feat-arch-review-expeditionlist-combinedprint/plan.r1.md`.

The plan covers all six FRs (FR-1 through FR-5 from the spec plus FR-6 from the arch-review amendment for `InternalsVisibleTo`) and all four NFRs across six tasks:

1. **Task 1** — Establish green baseline (build + run existing tests + snapshot the source for byte-equivalence + grep verifications).
2. **Task 2** — Grant `InternalsVisibleTo("Anela.Heblo.API")` on the Application assembly (separate commit, runtime no-op, unblocks the move).
3. **Task 3** — Create the relocated file in Application (don't delete the old one yet — both copies live in different namespaces, no symbol clash).
4. **Task 4** — Delete the old file + folder, remove dead `using` at `ServiceCollectionExtensions.cs:24`.
5. **Task 5** — Swap the test file's `using` line, run the full test gate, commit Tasks 3+4+5 atomically.
6. **Task 6** — NFR-3 architecture-compliance grep gates + final build/format verification.

Key plan decisions: two commits (the `InternalsVisibleTo` grant alone, then the atomic move); byte-diff check between old and new class bodies in Task 3 Step 2 to guarantee zero behavioral drift; explicit out-of-scope guardrails listed at the end (no parallelization, no visibility broadening, no `.ToList()` removal, no `await` reordering, no test body edits).

Skipping the execution-handoff prompt per the pipeline note.