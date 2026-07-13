# Implementation: add-invoice-import-state-tracking-regression-test

## What was implemented
Added a new EF-Core-backed regression test that exercises `InvoiceImportService` against a real `IssuedInvoiceRepository` + `ApplicationDbContext` (EF Core InMemory provider), instead of a fully mocked repository. This is the test class FR-2/NFR-2 require: a mocked repository cannot detect the class of bug where `UpdateAsync` is called on an entity that is still tracked as `Added` (not yet saved) — only a real EF Core change tracker throws `DbUpdateConcurrencyException` in that situation.

A sanity check was performed (not committed): the `if (!isNew)` guard in `ExecuteImportInvoice` was temporarily removed so `UpdateAsync` is called unconditionally again, reproducing the original bug. The new test failed as expected (`Assert.Single(result.Succeeded)` failed — the invoice landed in `Failed` because the EF InMemory provider's failed "update" swallowed into the per-invoice catch block). The guard was then restored and `git diff` confirmed the file was byte-identical to the already-committed fix before re-running tests and committing.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs` — new file. Constructs `InvoiceImportService` with a real `IssuedInvoiceRepository` backed by an `ApplicationDbContext` using `UseInMemoryDatabase`, and mocks for `IIssuedInvoiceSource`, `IIssuedInvoiceClient`, `IMapper`, `ILogger<InvoiceImportService>` (no transformations). One `[Fact]`, `ImportInvoicesAsync_WithNewInvoice_PersistsWithSingleSaveChangesCall`, imports a brand-new invoice and asserts: the import succeeds (no `DbUpdateConcurrencyException` swallowed into `Failed`), the persisted row (read back via a fresh `AsNoTracking()` query) has `IsSynced`/`LastSyncTime` populated from the ERP sync, `CreationTime`/`ConcurrencyStamp` set by `AddAsync`, and `LastModificationTime == null` (proof `UpdateAsync` — which is the only place that stamps it — was never called for the new-invoice path).

## Tests
`InvoiceImportServiceStateTrackingTests.ImportInvoicesAsync_WithNewInvoice_PersistsWithSingleSaveChangesCall` — the new EF-Core-backed regression test described above.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceStateTrackingTests" --logger "console;verbosity=normal"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" --logger "console;verbosity=normal"
cd ..
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs
```
The new test passes (1/1). The full `Invoices`-namespace slice run is 88/90 passing — the 2 failures (`IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.*`) are pre-existing, unrelated to this change: they require a running Docker daemon for Testcontainers Postgres, which is unavailable in this sandbox (`System.ArgumentException: Docker is either not running or misconfigured`), and belong to an earlier, already-merged feature (feat-3564). Build succeeds with 0 errors; format check reports no changes needed for the new file.

## Notes
As with the sibling task, the initially-dispatched developer subagent repeatedly launched backgrounded `dotnet test` calls and stopped without waiting for results, never completing the task. The orchestrator implemented this task directly per the plan's exact specified test code, ran the full TDD sanity-check/verify/build/format sequence itself, and committed. No deviation from the plan's exact code.

## Status
DONE
