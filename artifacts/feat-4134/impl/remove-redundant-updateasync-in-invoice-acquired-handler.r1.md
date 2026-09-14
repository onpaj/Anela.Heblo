# Implementation: remove-redundant-updateasync-in-invoice-acquired-handler (r1)

## What was done

Removed the redundant `_repository.UpdateAsync(purchaseOrder, cancellationToken)` call from
`UpdatePurchaseOrderInvoiceAcquiredHandler.Handle`, updated the two existing unit tests whose
assertions/setups depended on `UpdateAsync` being called, and left everything else in the
handler and its tests unchanged.

Same EF Core mechanism as the status handler (already merged in this feature): `GetByIdAsync`
loads the `PurchaseOrder` entity through `DbSet.FindAsync`, which attaches and tracks it. The
mutation via `purchaseOrder.SetInvoiceAcquired(...)` is picked up automatically by EF Core's
change tracker, so the explicit `UpdateAsync` call immediately before `SaveChangesAsync` was
redundant and forced an all-columns `UPDATE`. `SaveChangesAsync` remains the sole persistence
call.

## Files changed

1. `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs`
   - Deleted the single line `await _repository.UpdateAsync(purchaseOrder, cancellationToken);`
     from the `try` block. No other line changed.

2. `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs`
   - `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist`: deleted the
     `_repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, ...), Times.Once)` line. The
     harmless `UpdateAsync` setup was left in place (now a no-op), per the task context.
   - Renamed `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` to
     `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError` and swapped which mocked
     method throws: the `UpdateAsync` throw-setup was removed (it would never fire once the
     handler no longer calls it) and `SaveChangesAsync` is now mocked to throw
     `InvalidOperationException("db unavailable")` instead, exercising the same
     `catch (Exception ex)` / `ErrorCodes.PurchaseOrderUpdateFailed` path with identical
     assertions (error code, `OrderNumber`, `Message`).
   - `Handle_WithNonExistentOrder_ShouldReturnError` (already asserts `Times.Never` for
     `UpdateAsync`) was left completely unchanged.

## Decisions

- Strictly surgical: exactly the changes specified by the task context, nothing else.
- Step 2 of the task context (expected-pass intermediate check before the production code
  change) was not run as a separate interim step; the task context itself states this
  intermediate state is expected to already pass and does not require a red bar, so verification
  was done once, after both the test and handler edits, per Step 4/5.

## Verification

- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderInvoiceAcquiredHandlerTests"`
  -> `Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3`
- `dotnet build Anela.Heblo.sln` -> `0 Error(s)` (93 pre-existing warnings, unchanged from task 1)
- `dotnet format Anela.Heblo.sln --verify-no-changes` -> exit 0, no formatting differences
- `grep UpdateAsync UpdatePurchaseOrderInvoiceAcquiredHandler.cs` -> 0 occurrences
- Full-repo `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (no filter) was
  attempted but is not usable as a signal in this environment: 105 pre-existing failures, all
  from `Testcontainers`/Postgres fixtures failing because Docker is not available in this
  sandbox (`System.ArgumentException: Docker is either not running or misconfigured...`),
  unrelated to this change. The filtered run above plus the solution build/format check are the
  meaningful verification for this task, consistent with how task 1 was verified.

## Status

DONE
