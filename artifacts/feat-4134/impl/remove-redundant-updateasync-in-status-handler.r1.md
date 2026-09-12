# Implementation: remove-redundant-updateasync-in-status-handler (r1)

## What was done

Removed the redundant `_repository.UpdateAsync(purchaseOrder, cancellationToken)` call from
`UpdatePurchaseOrderStatusHandler.Handle`, and removed the now-obsolete `UpdateAsync`
verification from the one unit test that asserted on it.

The `PurchaseOrder` entity is already tracked by EF Core after `GetByIdAsync`
(`DbSet.FindAsync`), so the in-memory mutation performed by `purchaseOrder.ChangeStatus(...)`
is picked up by the change tracker and persisted by `SaveChangesAsync` as a minimal
per-column `UPDATE`. The explicit `UpdateAsync` call (`DbSet.Update` internally) marked every
scalar property modified, forcing an all-columns `UPDATE`. `SaveChangesAsync` remains as the
sole persistence call.

## Files changed

1. `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs`
   - Deleted the single line `await _repository.UpdateAsync(purchaseOrder, cancellationToken);`
     from the `try` block. No other line changed.

2. `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs`
   - In `Handle_ShouldCallRepositoryMethods`, deleted the line
     `_repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);`.
   - The `_repositoryMock.Setup(x => x.UpdateAsync(...))` calls elsewhere in the file were left
     untouched as instructed (harmless no-op setups; removing them is out of scope).

## Decisions

- Strictly surgical: exactly the two line deletions specified by the task context, nothing else.
- Step 2 of the task context (expected-pass intermediate test run) was folded into the final
  verification run, since the task context itself states no red bar is expected there.

## Verification

- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderStatusHandlerTests"`
  -> `Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14`
- `dotnet build Anela.Heblo.sln` -> `0 Error(s)` (93 pre-existing warnings, unchanged)
- `dotnet format Anela.Heblo.sln --verify-no-changes` -> exit 0, no formatting differences
- `grep UpdateAsync UpdatePurchaseOrderStatusHandler.cs` -> 0 occurrences

## Status

Complete. The task's verifiable check is satisfied.
