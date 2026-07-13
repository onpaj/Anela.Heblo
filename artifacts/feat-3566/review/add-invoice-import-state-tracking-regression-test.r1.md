# Code Review: add-invoice-import-state-tracking-regression-test

## Summary
The task adds a new EF-Core-backed regression test (`InvoiceImportServiceStateTrackingTests.cs`) that exercises `InvoiceImportService` against a real `ApplicationDbContext` (InMemory provider) via the concrete `IssuedInvoiceRepository`, instead of a mocked repository. This directly satisfies FR-2's requirement that the "UpdateAsync called on an unsaved Added-tracked entity" bug class be covered by a test using a real EF Core change tracker, which the existing fully-mocked `InvoiceImportServiceTests.cs` cannot detect. The implementation is a pure test addition (no production code changes), matches the task-context's prescribed code verbatim, and is grounded in the actual repository behavior.

## Review Result: PASS

### task: add-invoice-import-state-tracking-regression-test
**Status:** PASS

Verification performed:
- Read the new test file `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs` in full — it is byte-for-byte identical to the code specified in the task context (Step 1), using a real `ApplicationDbContext` with `UseInMemoryDatabase` and the concrete `IssuedInvoiceRepository`, with mocks only for `IIssuedInvoiceSource`, `IIssuedInvoiceClient`, `IMapper`, and `ILogger<InvoiceImportService>`.
- Read the current production code `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — confirms the `if (!isNew) { await _repository.UpdateAsync(...); }` guard is in place (the fix from the sibling task `fix-invoice-import-double-save`), and `GetOrCreateAsync` returns `(IssuedInvoice Invoice, bool IsNew)` without calling `SaveChangesAsync` internally, matching FR-1/FR-2/NFR-3 of the spec.
- Read `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` to independently confirm the test's assertions are correctly grounded in real repository behavior: `AddAsync` sets `CreationTime`/`ConcurrencyStamp` (lines 77-78), `UpdateAsync` sets `LastModificationTime`/`ConcurrencyStamp` (lines 85-86). The test's assertion `Assert.Null(saved.LastModificationTime)` is therefore a valid proof that `UpdateAsync` was never invoked for the new-invoice path, and the `CreationTime`/`ConcurrencyStamp` assertions correctly verify `AddAsync` ran.
- Read the existing `InvoiceImportServiceTests.cs` (mocked-repository suite) — confirms it independently asserts the same FR-2 call-count requirements (`AddAsync` once, `UpdateAsync` never, `SaveChangesAsync` once) for the new-invoice path, and that the existing-invoice path assertions are preserved, consistent with the task's self-review claims.
- The coordinator has independently confirmed (outside this review) that the new test passes 1/1, the full `Invoices` namespace slice is 88/90 passing (the 2 pre-existing `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` failures are Docker/Testcontainers-related, unrelated to this feature, and out of scope per the review instructions), `dotnet build` succeeds with 0 errors, and `dotnet format --verify-no-changes` reports no changes needed.

No functional requirement, architecture guidance, or correctness issue was found unmet. The test is a genuine, well-grounded regression test — not a tautological or trivially-passing check — since it exercises a real EF Core change tracker and would fail (as documented in the task's Step 2 sanity check and the implementation summary) if the `UpdateAsync` guard regressed.

## Docs to Update
None identified — this is a pure test addition with no public contract, API, or documented-behavior changes.

## Overall Notes
The new test file is scoped correctly: it is additive, does not modify `InvoiceImportServiceTests.cs` or `InvoiceImportIntegrationTests.cs`, and does not touch production code, consistent with the task's stated boundaries.
