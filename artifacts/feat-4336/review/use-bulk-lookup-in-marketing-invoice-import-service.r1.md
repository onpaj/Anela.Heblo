## Review Result: PASS

### task: use-bulk-lookup-in-marketing-invoice-import-service
**Status:** PASS

## Spec compliance

- `spec.r1.md` step 34/35 requires: (1) collect all transaction IDs from the fetched batch and call `GetExistingTransactionIdsAsync` exactly once before the loop, and (2) replace the per-row `ExistsAsync` call/`if (exists)` branch with a synchronous `alreadyImported.Contains(...)` check, preserving the existing "already imported — skipping" log message and `result.Skipped++` behavior. The implementation does exactly this in `MarketingInvoiceImportService.ImportAsync`.
- Acceptance criterion "`GetExistingTransactionIdsAsync` is called at most once per `ImportAsync` invocation — never inside the `foreach` loop" is verified by the new test `ImportAsync_BatchOfTransactions_CallsBulkLookupExactlyOnce_NeverCallsPerRowExistsAsync`, which also asserts `ExistsAsync` is never called.
- Open Question 1 in the spec ("should `stagedIds` be removed?") was explicitly resolved to **keep** `stagedIds` as a safety net, with the reasoning documented inline in a code comment. The implementation matches this resolution exactly — `stagedIds` is retained unconditionally and the pre-existing `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce` test (updated to mock the bulk method) still passes, confirming the within-run guard still works with the new snapshot-based lookup.
- `ExistsAsync` is left unchanged on both the interface and implementation, per spec constraint "Removing or refactoring `ExistsAsync` itself" is out of scope.
- All 11 tests in `MarketingInvoiceImportServiceTests.cs` pass (`Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`).

## Architecture adherence

Consistent with `arch-review.r1.md`'s finding and the established pattern from the prior task in this feature (`add-bulk-existence-lookup-repository-method`): swap an N-query per-row existence check for a single bulk query, without changing persistence/transaction semantics. No new abstractions, no schema change, no cross-module boundary touched — this is a same-file, same-class query-pattern fix consistent with Vertical Slice organization.

## Completeness

- TDD followed correctly: the task-context's Step 1 test file was applied verbatim, Step 2's RED state was confirmed (3 failing tests, matching the task-context's documented expected failures exactly), Step 3's production change was applied verbatim, and Step 4's GREEN state was confirmed.
- Build succeeds for both changed projects (`Anela.Heblo.Application`, `Anela.Heblo.Tests`) with 0 errors (pre-existing warnings only, unrelated to this change).
- `dotnet format --verify-no-changes` (scoped to the two changed files) exits 0 — no formatting issues.
- The developer's impl notes flag that a full unfiltered `dotnet build`/`dotnet test` run was not completed due to heavy shared-machine CPU contention from concurrent worker VMs (confirmed independently — background test runs on this host were observed queued behind several other `dotnet`/MSBuild processes). Given the change is isolated to one service class and its own test file, with the scoped build/test/format checks all green, this is a reasonable trade-off for this task-level review; the pipeline's own code-review phase and CI will still run the full suite before merge.

## Correctness

No logic errors found. The comment added above `stagedIds` accurately explains why the guard is still necessary (the bulk lookup is a pre-loop snapshot that cannot see in-batch duplicates). Exception handling, logging, and the post-loop `SaveChangesAsync`/`throw` behavior are all unchanged from before this task, as intended — only the existence-check mechanism was swapped.

## Docs to Update

(Omit — no public behavior, API surface, or operational change; this is an internal query-pattern optimization with no interface/contract changes.)

## Overall Notes

Clean, minimal, spec-compliant change. No concerns.
