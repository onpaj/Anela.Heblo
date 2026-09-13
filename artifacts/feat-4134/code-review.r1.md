# Code Review: feat-4134 (round 1)

## Review Result: CLEAN

## Scope

Full feature diff for feat-4134 against `main` (merge-base `b7718dd7c1d12038b8dcd9d773657491936d051b`). The real code changes are confined to four files:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs`

Everything else in the branch diff is pipeline artifacts under `artifacts/feat-4134/` (spec, design, arch-review, task-context, impl, per-task review notes, state.json) — not reviewed as code.

## Verification performed

- Read both handler files in full and confirmed the diff is exactly a one-line deletion each (`await _repository.UpdateAsync(purchaseOrder, cancellationToken);`), with `SaveChangesAsync` remaining as the sole persistence call and no other line touched.
- Read `BaseRepository<TEntity, TKey>` and `PurchaseOrderRepository`: `GetByIdAsync` is not overridden by `PurchaseOrderRepository`, so it resolves to the base implementation (`DbSet.FindAsync`), which attaches/tracks the returned entity. No `AsNoTracking()` is applied on this path (the repository's `AsNoTracking()` usages are confined to `ExistsAsync`, `GetHistoryAsync`, `GetLineByIdAsync` — unrelated methods). This confirms the premise both the spec and the two per-task reviews already verified: the entity is tracked before `ChangeStatus`/`SetInvoiceAcquired` mutate it, so EF's change tracker alone (via `SaveChangesAsync`) is sufficient, and the deleted `UpdateAsync` call was genuinely redundant, not silently relied upon.
- Read both test files' full diffs: assertions verifying `UpdateAsync` was called are removed (not weakened to `Times.Never` in a way that hides missing coverage — they are simply deleted since the setups become no-ops), and `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` is correctly renamed to `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError` with the throw moved from the `UpdateAsync` mock setup to the `SaveChangesAsync` mock setup — the exception-handling path (`catch (Exception ex)` → `ErrorCodes.PurchaseOrderUpdateFailed`) stays exercised with identical assertions.
- Confirmed `IPurchaseOrderRepository.UpdateAsync` and `BaseRepository.UpdateAsync` themselves are untouched, matching the spec's explicit Out-of-Scope boundary (the same primitive is used elsewhere for not-yet-tracked entities).
- No response contract, error code, log message, or unrelated branch (not-found checks, `Enum.TryParse` validation, `InvalidOperationException` catch) was touched in either handler.

## Blocking (correctness)

- None.

## Advisory (cleanup)

- None. The change is a minimal, surgical two-line deletion plus the mechanically-required test updates; there is no duplicated logic, dead code, or avoidable inefficiency introduced.

## Notes

Both per-task reviews (`review/remove-redundant-updateasync-in-status-handler.r1.md`, `review/remove-redundant-updateasync-in-invoice-acquired-handler.r1.md`) already independently verified the same EF Core tracking premise and confirmed passing test runs, build, and `dotnet format`. This round re-verified the premise directly against `BaseRepository`/`PurchaseOrderRepository` source rather than relying on the prior reviews' claims, and found no discrepancy.
