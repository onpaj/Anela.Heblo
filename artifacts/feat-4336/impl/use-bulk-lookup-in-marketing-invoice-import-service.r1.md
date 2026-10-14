# Implementation: use-bulk-lookup-in-marketing-invoice-import-service

## What was implemented

Replaced the per-row `ExistsAsync` existence check inside `MarketingInvoiceImportService.ImportAsync`'s
import loop with a single pre-loop call to `IImportedMarketingTransactionRepository.GetExistingTransactionIdsAsync`
(added in the prior task of this feature). This cuts the per-run DB round-trip count for existence
checking from O(N) — one `ExistsAsync` call per transaction — to O(1): one bulk lookup per import run.

Followed strict TDD: updated the test file first to mock the bulk method and add a new test proving
the bulk lookup is called exactly once and `ExistsAsync` is never called, ran the suite to confirm RED
(3 failures, matching the task context's expected RED output exactly), then updated the production code
and reran to confirm GREEN.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — replaced whole file per task spec: all existing tests now mock `GetExistingTransactionIdsAsync` instead of `ExistsAsync`, plus the new `ImportAsync_BatchOfTransactions_CallsBulkLookupExactlyOnce_NeverCallsPerRowExistsAsync` test
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — `ImportAsync` now fetches all transaction ids up front and calls `GetExistingTransactionIdsAsync` once before the loop; the per-row `ExistsAsync` call and its `await` are gone. The in-memory `stagedIds` within-run duplicate guard is kept unconditionally (with an explanatory comment) since the bulk lookup is a single pre-loop snapshot that cannot see duplicates appearing later in the same batch.

## Tests

`MarketingInvoiceImportServiceTests.cs` — 11 tests, all passing:
- Existing 10 tests updated to mock the bulk method instead of per-row `ExistsAsync`
- New: `ImportAsync_BatchOfTransactions_CallsBulkLookupExactlyOnce_NeverCallsPerRowExistsAsync` — 3 transactions (1 already imported), asserts `GetExistingTransactionIdsAsync` is called exactly once and `ExistsAsync` is never called

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
# Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11

dotnet build   # 0 errors (pre-existing warnings only, unrelated to this change)
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
# exit 0, no changes needed
```

## Notes

- Ran solo in-session (no Task-tool subagent dispatch available in this environment) rather than via the subagent-driven-development implementer/reviewer chain `developer.md` normally uses; work still followed the task-context's exact steps (RED → implement → GREEN → build/format) and the orchestrator's separate Reviewer Task step still runs against this output as usual.
- Did not run the full `dotnet build` + full `dotnet test` suite (very slow / resource-contended on this shared machine — a full unfiltered `dotnet test` timed out even after 900s under concurrent load from other worker VMs). Instead verified via project-scoped `dotnet build` for both the changed projects (`Anela.Heblo.Application`, `Anela.Heblo.Tests`) and the scoped, filtered test run above, plus a `dotnet format --verify-no-changes` scoped to the two changed files. No other code was touched, so a full suite run is not expected to surface anything new, but a human/CI run of the full suite is the authoritative check.

## PR Summary

Replaced the O(N) per-row `ExistsAsync` existence check in `MarketingInvoiceImportService.ImportAsync` with a single O(1) bulk lookup (`GetExistingTransactionIdsAsync`, added by the prior task) called once before the import loop, cutting the per-run DB round-trip count for duplicate detection from one query per transaction to one query per run. The in-memory within-run duplicate guard (`stagedIds`) is unchanged and still required, since the bulk lookup is a pre-loop snapshot that can't see duplicates appearing later in the same batch.

### Changes
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — call bulk lookup once, drop per-row `ExistsAsync`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — updated mocks to the bulk method, added a test asserting the bulk call happens exactly once and per-row `ExistsAsync` is never called

## Status
DONE
