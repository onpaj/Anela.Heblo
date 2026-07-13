# Code Review: fix-invoice-import-double-save

## Summary
The implementation matches the task spec's exact prescribed diff: `GetOrCreateAsync` no longer calls `SaveChangesAsync` and now returns `(IssuedInvoice Invoice, bool IsNew)`; `ExecuteImportInvoice` skips `UpdateAsync` for newly created invoices and calls `SaveChangesAsync` exactly once, unconditionally, at the end. The three prescribed test updates (1a, 1b, 1c) were applied exactly as specified. Ran the full `InvoiceImportServiceTests` suite — all 21 tests pass.

## Review Result: PASS

### task: fix-invoice-import-double-save
**Status:** PASS

Verification detail:
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`: `GetOrCreateAsync` (lines 133-144) matches the spec's prescribed shape exactly — `AddAsync` called once for a new key, no `SaveChangesAsync` inside it, returns `(found, true)`/`(found, false)`. `ExecuteImportInvoice` (lines 81-131) deconstructs the tuple, conditionally skips `UpdateAsync` when `isNew` (lines 115-120), and calls `SaveChangesAsync` exactly once unconditionally (line 122) — satisfies FR-1, FR-2, FR-3, FR-4, NFR-1, NFR-3 as specified.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`: the three test edits (1a `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult`, 1b `ImportInvoicesAsync_WithExternalServiceFailure_TracksSyncStatus`, 1c `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting`) match the task context's prescribed new assertions verbatim, correctly locking in the new-invoice (`AddAsync` once / `UpdateAsync` never / `SaveChangesAsync` once) and existing-invoice (`UpdateAsync` once / `SaveChangesAsync` once) call-count contracts from FR-2.
- Ran `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests"` myself: **Total tests: 21, Passed: 21, Failed: 0** (Test Run Successful). All three modified tests pass, and the seven other tests in the file are unaffected.
- Confirmed the FR-2/NFR-2 requirement for a real-EF-Core-change-tracker regression test (to catch the `DbUpdateConcurrencyException` class of bug that mocks cannot detect) is explicitly and correctly deferred to the separate, already-planned task `add-invoice-import-state-tracking-regression-test` (`backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs`), which is out of scope for this task per the pipeline's task decomposition — not a gap in this task.
- Commit message described in the implementation summary (Step 6) accurately reflects the change; scope is surgical (only the two intended files touched), no unrelated changes.

## Docs to Update
None — this is an internal, private-method-only implementation fix with no public contract, DTO, or interface changes, consistent with NFR-3.

## Overall Notes
No issues found. Implementation is a faithful, verified execution of the spec's prescribed fix and test updates.
