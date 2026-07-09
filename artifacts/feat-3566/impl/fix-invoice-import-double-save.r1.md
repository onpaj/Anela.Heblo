# Implementation: fix-invoice-import-double-save

## What was implemented
`InvoiceImportService.GetOrCreateAsync` no longer calls `SaveChangesAsync` internally; it now returns `(IssuedInvoice Invoice, bool IsNew)`. `ExecuteImportInvoice` uses `IsNew` to skip `_repository.UpdateAsync(...)` for newly created invoices (already tracked via `AddAsync` in the same unit of work) while still calling `_repository.SaveChangesAsync(...)` exactly once at the end for both new and existing invoices. This eliminates the redundant first `SaveChangesAsync` round trip for new invoices and avoids the `DbUpdateConcurrencyException` that a naive removal of just the inner save would have caused (calling `UpdateAsync`/`DbSet.Update` on an already-`Added`-tracked entity flips it to `Modified`, producing an `UPDATE` against a nonexistent row).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — `GetOrCreateAsync` signature changed to `Task<(IssuedInvoice Invoice, bool IsNew)>` and no longer calls `SaveChangesAsync`; `ExecuteImportInvoice` deconstructs the tuple and conditionally skips `UpdateAsync` when `isNew`.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` — added FR-2 call-count assertions (`AddAsync` once / `UpdateAsync` never / `SaveChangesAsync` once) to `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult`; rewrote the sync-failure assertion in `ImportInvoicesAsync_WithExternalServiceFailure_TracksSyncStatus` to check the mutated `invoice` object directly (since `UpdateAsync` is no longer called for new invoices) plus `Times.Never`/`Times.Once` call-count checks; added a `SaveChangesAsync` `Times.Once` assertion to `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting`.

## Tests
`backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` — full 21-test suite in this class/namespace slice passes, including the three updated tests above (both the new-invoice and existing-invoice call-count contracts from FR-2 are now explicitly asserted).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests" --logger "console;verbosity=normal"
cd ..
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs
```
All 21 tests pass, build succeeds with 0 errors (pre-existing unrelated warnings only), format check reports no changes needed.

## Notes
The initially-dispatched developer subagent completed the test-file edits (Step 1) but stalled twice on backgrounded `dotnet test` calls without waiting for their results, never reaching the production code change. The orchestrator applied Step 3 (production fix) directly per the plan's exact specified diff, then ran verification (Steps 2/4/5) and the commit (Step 6) itself. No deviation from the plan's exact code.

## Status
DONE
