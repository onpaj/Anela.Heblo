# Code Review: revert-tracked-mutation-on-existing-invoice-import-failure

## Summary
The implementation matches the task spec and arch review exactly: `RevertTrackedChangesAsync` was added to `IIssuedInvoiceRepository`/`IssuedInvoiceRepository` as a synchronous `EntityState.Unchanged` reset with the documented caveat comment, `GetOrCreateAsync` now surfaces `isNew`, and `ExecuteImportInvoice`'s outer catch reverts tracked changes only for pre-existing, actually-loaded invoices before re-throwing. Build, format, and all 21 existing `InvoiceImportServiceTests` pass unchanged, confirming no regression to happy-path, new-invoice, or failure-reporting behavior.

## Review Result: PASS

### task: revert-tracked-mutation-on-existing-invoice-import-failure
**Status:** PASS

## Docs to Update
None.

## Overall Notes
- Verified against the actual commit (`bea1c60d`) diff, not just the developer summary: interface addition placed after `GetHeadersByDateAsync` per arch guidance, implementation uses the inherited `Context` field with no new DB round-trip, and the outer-catch guard `if (!isNew && invoice != null)` correctly matches FR-2/FR-3 acceptance criteria, including the `GetByIdAsync`-throws-before-load edge case where `invoice` stays `null`.
- Inner try/catch around `_issuedInvoiceClient.SaveAsync` (SyncFailed path) and the `isNew == true` path were both left untouched, matching the spec's explicit scope boundaries and Out of Scope list.
- Confirmed only one call site of the now-tuple-returning `GetOrCreateAsync` exists in `backend/src` (inside `InvoiceImportService.cs` itself), so the private-method signature change is safe.
- `dotnet build Anela.Heblo.sln` succeeds (0 errors, only pre-existing unrelated warnings); `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore` is clean; `InvoiceImportServiceTests` (21/21) pass, including the three tests the spec called out by name (`ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting`, `ImportInvoicesAsync_WithExistingInvoice_RefreshesCoreDataFromSource`, `ImportInvoicesAsync_WithPartialFailure_TracksFailedInvoices`).
- No new tests were added in this task, which is correct per this task's own scope — FR-4's regression test (real EF Core change tracker) is explicitly deferred to the separate `add-regression-test-for-tracked-mutation-revert` task, not this one.
