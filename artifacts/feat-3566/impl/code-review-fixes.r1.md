# Implementation: code-review-fixes (round 1)

## What was implemented
Fixed the Blocking finding from `code-review.r1.md`: a new invoice's entity, tracked via `AddAsync` inside `GetOrCreateAsync`, could be left dangling in the shared per-batch `DbContext`'s change tracker if anything threw between that point and the invoice's own `SaveChangesAsync` call (e.g. an AutoMapper exception in the refresh-map step, or a transformation throwing). Because `IInvoiceImportService`/`IIssuedInvoiceRepository` are `AddScoped` and `ImportInvoicesAsync` loops over every invoice in a batch using the same instances, a dangling `Added` entity from a failed invoice would be silently flushed into the database by whichever *next* invoice's `SaveChangesAsync` call happened to run — persisting an incomplete row for an invoice that was reported as `Failed`.

`ExecuteImportInvoice`'s `invoice`/`isNew` locals were hoisted above the `try` block. In the outer `catch`, if `isNew` is true and `invoice` is non-null, `_repository.DeleteAsync(invoice, cancellationToken)` is now called before rethrowing. `DeleteAsync` maps to `DbSet.Remove(entity)` (see `BaseRepository.DeleteAsync`), and per EF Core semantics, calling `Remove()` on an entity still in `Added` state (never flushed) transitions it straight to `Detached` with no DB round trip — it safely evicts the entity from tracking without attempting a delete against a row that was never inserted.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — hoisted `invoice`/`isNew` above the `try`; added the detach-on-failure call in the `catch` block.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs` — added `ImportInvoicesAsync_WithFailureBeforeSaveThenSuccess_DoesNotPersistFailedInvoice`, exercising exactly the reviewer's suggested scenario: two invoices in one batch, the first mocked to throw during the refresh-map step (`_mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(failingDetail, It.IsAny<IssuedInvoice>())` throws), the second succeeding normally. Asserts the first invoice's row does not exist in the database after the batch completes, and the second invoice's row does.

## Tests
`InvoiceImportServiceStateTrackingTests.ImportInvoicesAsync_WithFailureBeforeSaveThenSuccess_DoesNotPersistFailedInvoice` (new). Sanity-checked by temporarily removing the detach call and confirming this test fails against the pre-fix code (`Assert.False(failedRowExists, ...)` failed — the failed invoice's row *was* persisted), then restoring the fix and confirming it passes again.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportService" --logger "console;verbosity=normal"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" --logger "console;verbosity=normal"
cd ..
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs
```
All `InvoiceImportService*` tests pass (23/23). Full `Invoices`-namespace slice: 89/91 passing — the 2 failures are the same pre-existing, unrelated `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` Docker/Testcontainers-unavailable failures noted in the prior round. Build succeeds with 0 errors, format check reports no changes needed.

## Notes
No deviation from the reviewer's suggested fix approach (detach in the catch block via the existing `DeleteAsync` method — no new repository interface members were needed, keeping NFR-3 intact).

## Status
DONE
