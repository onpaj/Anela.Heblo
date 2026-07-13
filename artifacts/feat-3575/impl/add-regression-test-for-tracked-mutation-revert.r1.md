# Implementation: add-regression-test-for-tracked-mutation-revert

## What was implemented
Added `InvoiceImportRealChangeTrackerTests.cs`, a new test class using a real
`ApplicationDbContext` (EF Core InMemory provider) and a real `IssuedInvoiceRepository`, so the EF Core
change tracker is genuinely exercised (the existing `InvoiceImportServiceTests.cs` mocks
`IIssuedInvoiceRepository` entirely and has no change tracker for the bug to leak into).

The single test seeds invoice "INV-A" as a pre-existing row, clears the change tracker to simulate a fresh
batch read, then imports a batch of two invoices: A (whose transformation step throws, exercising the
*outer* catch — the actual bug location) and B (which succeeds). It asserts:
- A is reported `Failed` and B is reported `Succeeded` (unchanged reporting behavior).
- A's row, read fresh with `AsNoTracking()`, still has its original `CustomerName`/`Price` — proving the
  tracked mutation from A's failed re-import was reverted and never flushed by B's later
  `SaveChangesAsync`.
- B's row was persisted.
- `_issuedInvoiceClient.SaveAsync` was never called for A (confirms the failure point is the
  transformation step, not the client call, so the *inner* catch — out of scope — is not the one being
  exercised).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportRealChangeTrackerTests.cs` — new test file.

## Tests
- New: `InvoiceImportRealChangeTrackerTests.ImportInvoicesAsync_WhenReImportOfExistingInvoiceFailsMidPipeline_DoesNotPersistPartialMutationAndContinuesBatch`.
- **Fail-before/pass-after explicitly verified**: temporarily disabled the revert call in
  `InvoiceImportService.ExecuteImportInvoice`'s outer catch (`if (false && !isNew && invoice != null)`),
  reran only this test — it FAILED with `Assert.Equal() Failure: Expected: "Original Customer" Actual:
  "MUTATED-SHOULD-NOT-PERSIST"`, confirming the test genuinely detects the bug. Restored the file via
  `git checkout --` (no diff left behind) and reran — the test PASSES again.
- Ran the full existing suite: `InvoiceImportServiceTests` (21) + new test (1) +
  `InvoiceImportIntegrationTests` all pass together (28/28, 0 failures).
- Two unrelated pre-existing failures were observed running the broader `~Invoices` filter:
  `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.*` — these require a real Postgres container via
  Testcontainers, which isn't available in this environment (no Docker). Confirmed unrelated: they test
  `GetSyncStatsAsync` SQL shape, nothing touched by this change.

## How to verify
1. `dotnet build Anela.Heblo.sln` — succeeds, 0 errors.
2. `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore` — clean.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportRealChangeTrackerTests"` — 1/1 pass.
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests|FullyQualifiedName~InvoiceImportRealChangeTrackerTests|FullyQualifiedName~InvoiceImportIntegrationTests"` — 28/28 pass.

## Notes
No deviations from the task-plan's guidance. Followed the plan's implementer note precisely: used a mocked
`IIssuedInvoiceImportTransformation.TransformAsync` throw for invoice A (not `_issuedInvoiceClient.SaveAsync`
throwing) to land in the outer catch where the actual bug/fix lives, and added an explicit
`_mockClient.Verify(x => x.SaveAsync(detailA, ...), Times.Never)` assertion to make that guarantee visible
in the test itself.

## PR Summary
Adds regression coverage proving the tracked-mutation-revert fix (from the prior task) actually works: a
real EF Core InMemory `ApplicationDbContext` + real `IssuedInvoiceRepository` are used (instead of the
usual fully-mocked repository) so the change tracker leak this bug relied on can be genuinely observed.
The new test seeds an existing invoice, fails its re-import mid-pipeline, and asserts the row is left
untouched in the database after a second invoice in the same batch commits — verified to fail on the
pre-fix code and pass on the fixed code.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportRealChangeTrackerTests.cs` — new regression test (real change-tracker harness)

## Status
DONE
