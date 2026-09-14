# Code Review: remove-redundant-updateasync-in-invoice-acquired-handler (r1)

## Summary

The implementation matches the task context exactly: one line deleted from the handler, and
the two test edits (dropping the `UpdateAsync` verification, and rewriting the exception-path
test to throw from `SaveChangesAsync` instead of `UpdateAsync`) applied verbatim. Verification
commands were re-run and pass.

## Review Result: PASS

### task: remove-redundant-updateasync-in-invoice-acquired-handler
**Status:** PASS

## Findings

1. **Spec compliance** — `git diff` against the prior commit shows exactly the changes the task
   context specified: the `await _repository.UpdateAsync(purchaseOrder, cancellationToken);`
   line is deleted from `UpdatePurchaseOrderInvoiceAcquiredHandler.Handle`, with
   `SaveChangesAsync` remaining as the sole persistence call and no other line touched. In the
   test file, the `UpdateAsync` verification in
   `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist` is removed (its harmless
   `UpdateAsync` setup correctly left in place), and
   `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` is renamed to
   `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError` with the throw moved from the
   `UpdateAsync` setup to the `SaveChangesAsync` setup, keeping the same exception type/message
   and all downstream assertions (error code, `OrderNumber`, `Message`) identical.
   `Handle_WithNonExistentOrder_ShouldReturnError` is unchanged, as required.

2. **Correctness** — Same EF Core mechanism already validated for the sibling status handler in
   this feature: `GetByIdAsync` tracks the entity via `DbSet.FindAsync`, `SetInvoiceAcquired`'s
   mutation is picked up by the change tracker, and `SaveChangesAsync` alone persists it.
   Removing the redundant `UpdateAsync` call is behaviour-preserving.

3. **Scope discipline** — No other line in the handler or test file changed; the not-found
   branch, the generic `catch` block, constructor/DI, and the unrelated
   `Handle_WithNonExistentOrder_ShouldReturnError` test are untouched, matching the task's
   explicit scope.

4. **Completeness** — All three tests in the file pass. No new tests were required by the
   spec; the exception path stays covered via the rewritten `SaveChangesAsync`-throws test.

## Verification (re-run by the reviewer)

- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderInvoiceAcquiredHandlerTests"`
  -> `Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3`
- `dotnet build Anela.Heblo.sln` -> `0 Error(s)` (93 pre-existing warnings, unchanged)
- `dotnet format Anela.Heblo.sln --verify-no-changes` -> exit 0
- `UpdatePurchaseOrderInvoiceAcquiredHandler.cs` contains zero occurrences of `UpdateAsync`
- Noted (not held against this task): an unfiltered full-suite `dotnet test` run shows 105
  pre-existing failures, all `Testcontainers`/Postgres fixture failures because Docker is
  unavailable in this sandbox — unrelated to this change, same as observed for task 1.

## Docs to Update

None. This is an internal persistence-call removal with no change to public behaviour, API
surface, configuration, or operations.

## Overall Notes

Both tasks for this feature (status handler and invoice-acquired handler) are now implemented
identically in shape and verified the same way. No cross-cutting concerns.

**Status:** PASS
