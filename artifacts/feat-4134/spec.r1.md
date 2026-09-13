# Specification: Remove Redundant UpdateAsync Calls in Purchase Order Handlers

## Summary
Two MediatR command handlers in the Purchase module (`UpdatePurchaseOrderStatusHandler` and `UpdatePurchaseOrderInvoiceAcquiredHandler`) call `_repository.UpdateAsync(purchaseOrder, cancellationToken)` immediately before `SaveChangesAsync(cancellationToken)`, even though the `PurchaseOrder` entity was already loaded via `GetByIdAsync` and is therefore already tracked by the EF Core change tracker. This spec covers removing the two redundant `UpdateAsync` calls so persistence in these handlers relies solely on EF Core's automatic change detection, matching the pattern already used correctly by `UpdatePurchaseOrderHandler`.

## Background
`BaseRepository<TEntity, TKey>.UpdateAsync` calls `DbSet.Update(entity)`, which internally calls `context.Entry(entity).State = EntityState.Modified` (via `DbSet.Update`), marking **every scalar property** of the entity as modified — not just the properties that actually changed. Because both `UpdatePurchaseOrderStatusHandler` and `UpdatePurchaseOrderInvoiceAcquiredHandler` load the entity through `GetByIdAsync`, which calls `DbSet.FindAsync(id, ...)`, the entity is already attached and tracked with `EntityState.Unchanged` before any mutation happens. When the handler then mutates the entity in memory (`purchaseOrder.ChangeStatus(...)` / `purchaseOrder.SetInvoiceAcquired(...)`) and calls the redundant `UpdateAsync`, EF's fine-grained change tracking is short-circuited and replaced with a blanket "all columns modified" state. The subsequent `SaveChangesAsync` then emits an `UPDATE` statement that writes every scalar column (`OrderNumber`, `OrderDate`, `ExpectedDeliveryDate`, `ContactVia`, `Notes`, `SupplierId`, `SupplierName`, `TotalAmount`, etc.) regardless of whether those columns changed.

A third handler in the same module, `UpdatePurchaseOrderHandler`, already demonstrates the correct pattern: it loads the entity via `GetByIdWithDetailsAsync` (also EF-tracked), mutates it through domain methods, and calls only `SaveChangesAsync`, relying on EF's automatic diffing to generate a minimal `UPDATE`. The two handlers in scope diverge from this established, correct pattern for no functional benefit, creating inconsistency across otherwise-similar in-place-mutation handlers and unnecessary risk if optimistic-concurrency tokens (e.g. `row_version` shadow properties) or `SaveChanges` interceptors that inspect per-property modification state are introduced later.

This is a pure internal persistence-efficiency and consistency fix. It does not change any request/response contract, business rule, validation, or observable behavior of either use case.

## Functional Requirements

### FR-1: Remove redundant `UpdateAsync` call in `UpdatePurchaseOrderStatusHandler`
In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs`, remove the line:

```csharp
await _repository.UpdateAsync(purchaseOrder, cancellationToken);
```

which currently precedes `await _repository.SaveChangesAsync(cancellationToken);` inside the `try` block (handler line 51, immediately after `purchaseOrder.ChangeStatus(newStatus, updatedBy);`). Leave `await _repository.SaveChangesAsync(cancellationToken);` in place unchanged — EF Core's change tracker will detect and persist the status change (and the `UpdatedAt`/`UpdatedBy` fields set inside `ChangeStatus`) automatically because `purchaseOrder` was loaded via `GetByIdAsync` and remains attached/tracked.

No other line in this handler changes: error handling, logging, response mapping, and the status-transition validation (`Enum.TryParse`, the `InvalidOperationException` catch for disallowed transitions) all remain exactly as they are today.

**Acceptance criteria:**
- The `UpdateAsync` call on line 51 of `UpdatePurchaseOrderStatusHandler.cs` is deleted; no other line in the file is modified.
- `_repository.SaveChangesAsync(cancellationToken)` is still called exactly once on the success path, as before.
- Calling the handler with a valid status transition still persists the new `Status`, `UpdatedAt`, and `UpdatedBy` values (verified via a test that asserts on the entity state after `SaveChangesAsync`, e.g. against an EF Core in-memory/SQLite provider or by asserting the mutated entity's properties, rather than by verifying a mocked `UpdateAsync` call — see FR-3).
- Existing response contract (`UpdatePurchaseOrderStatusResponse` shape and field values), error codes (`PurchaseOrderNotFound`, `InvalidPurchaseOrderStatus`, `StatusTransitionNotAllowed`), and log messages are unchanged.
- `IPurchaseOrderRepository.UpdateAsync` and `BaseRepository.UpdateAsync` are **not** modified or removed — only this call site is removed, since `UpdateAsync` may still be legitimately used elsewhere (e.g. for newly-added, not-yet-tracked entities).

### FR-2: Remove redundant `UpdateAsync` call in `UpdatePurchaseOrderInvoiceAcquiredHandler`
In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs`, remove the line:

```csharp
await _repository.UpdateAsync(purchaseOrder, cancellationToken);
```

which currently precedes `await _repository.SaveChangesAsync(cancellationToken);` inside the `try` block (handler line 44, immediately after `purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy);`). Leave `await _repository.SaveChangesAsync(cancellationToken);` in place unchanged.

No other line in this handler changes: the `null`-check for a missing order, the generic `catch (Exception ex)` block returning `ErrorCodes.PurchaseOrderUpdateFailed`, logging, and response mapping all remain exactly as they are today.

**Acceptance criteria:**
- The `UpdateAsync` call on line 44 of `UpdatePurchaseOrderInvoiceAcquiredHandler.cs` is deleted; no other line in the file is modified.
- `_repository.SaveChangesAsync(cancellationToken)` is still called exactly once on the success path, as before.
- Calling the handler still persists the new `InvoiceAcquired` value (and any `UpdatedBy`/`UpdatedAt` set inside `SetInvoiceAcquired`), verified without relying on a mocked `UpdateAsync` call (see FR-3).
- Existing response contract (`UpdatePurchaseOrderInvoiceAcquiredResponse` shape and field values) and error codes (`PurchaseOrderNotFound`, `PurchaseOrderUpdateFailed`) are unchanged.
- Note: the existing test `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` exercises the generic `catch (Exception ex)` block by making the mocked `UpdateAsync` throw; since `UpdateAsync` will no longer be called, this test must be adapted (see FR-3) so the exception path is still exercised, e.g. by making the mocked `SaveChangesAsync` throw instead. The `catch (Exception ex)` block itself is not modified.

### FR-3: Update existing unit tests to match the new call pattern
Both `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs` currently mock `IPurchaseOrderRepository.UpdateAsync` and, in several cases, explicitly `Verify(... UpdateAsync ..., Times.Once)`. After FR-1/FR-2, the handlers no longer call `UpdateAsync`, so these tests must be updated or they will fail (the `Times.Once` verifications) or become misleading (the now-unused `UpdateAsync` setups).

Specifically:
- In `UpdatePurchaseOrderStatusHandlerTests.cs`, the `Handle_ShouldCallRepositoryMethods` test's assertion `_repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);` must be removed or changed to `Times.Never`. The now-unnecessary `_repositoryMock.Setup(x => x.UpdateAsync(...))` calls in this file may be left in place (they become no-ops since the mocked method is never invoked) or removed for clarity — removing them is preferred to keep tests honest about what the handler actually does, per the "surgical changes" principle only the assertions that would otherwise fail must be touched; broader test cleanup is at the implementer's discretion but should not expand scope unnecessarily.
- In `UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs`:
  - `Handle_WithNonExistentOrder_ShouldReturnError` already asserts `Times.Never` for `UpdateAsync` — no change needed there.
  - `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist` asserts `_repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);` — this must be removed or changed to `Times.Never`.
  - `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` mocks `UpdateAsync` to throw to exercise the `catch (Exception ex)` / `ErrorCodes.PurchaseOrderUpdateFailed` path. Since `UpdateAsync` is no longer called, this test must be rewritten to make `SaveChangesAsync` throw instead (same exception, same expected error code and message), so the exception-handling path in the handler remains covered. Rename the test if desired to reflect the new trigger (e.g. `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError`), or keep the name if minimizing diff is preferred.

**Acceptance criteria:**
- All tests in both test files compile and pass after FR-1 and FR-2 are applied.
- No test asserts `UpdateAsync` is called by either handler after the fix.
- The exception-handling path in `UpdatePurchaseOrderInvoiceAcquiredHandler` (the generic `catch (Exception ex)` returning `PurchaseOrderUpdateFailed`) remains covered by at least one test, now triggered via a `SaveChangesAsync` failure instead of an `UpdateAsync` failure.
- `dotnet build` and `dotnet test` (or the equivalent solution-wide test run) succeed with no new failures introduced by this change.

## Non-Functional Requirements

### NFR-1: Performance
The change reduces the size of the generated `UPDATE` SQL statement for both use cases from an all-columns update to an update containing only the columns whose values actually changed (e.g. just `Status`, `UpdatedAt`, `UpdatedBy` for a status change; just `InvoiceAcquired`, `UpdatedAt`, `UpdatedBy` for an invoice-acquired toggle — exact column set depends on what `ChangeStatus`/`SetInvoiceAcquired` mutate). No specific latency or throughput target is defined; this is a correctness/efficiency cleanup, not a performance-critical hot path, so no load testing is required. The functional behavior and final persisted row values must be identical to today's behavior — only the SQL shape (columns written) changes.

### NFR-2: Security
No security-relevant behavior changes. No new inputs, outputs, permissions, or data exposure are introduced. Authorization/authentication for these two MediatR requests is unaffected and out of scope for this change.

## Data Model
No schema, entity, or DTO changes. The `PurchaseOrder` aggregate (and its `ChangeStatus` / `SetInvoiceAcquired` domain methods) is unchanged. `IPurchaseOrderRepository` and `BaseRepository<TEntity, TKey>` interfaces/implementations are unchanged — `UpdateAsync` remains available on the repository for other call sites; only these two specific call sites are removed.

## API / Interface Design
No public API surface changes. `UpdatePurchaseOrderStatusRequest`/`UpdatePurchaseOrderStatusResponse` and `UpdatePurchaseOrderInvoiceAcquiredRequest`/`UpdatePurchaseOrderInvoiceAcquiredResponse` (MediatR request/response contracts, and by extension any generated OpenAPI/TypeScript client and controller endpoints that invoke these MediatR requests) are unchanged in shape and semantics. This is purely an internal handler implementation change.

## Dependencies
- Entity Framework Core change tracking (`DbSet.FindAsync` attaches and tracks the entity; `SaveChangesAsync` performs automatic diffing) — existing dependency, no version or configuration change required.
- No new libraries, services, or feature flags are introduced.
- Depends on `GetByIdAsync` continuing to return an EF-tracked entity (i.e., no `AsNoTracking()` is introduced upstream) — this is already the case today (`DbSet.FindAsync`) and is not changed by this spec.

## Out of Scope
- Any change to `BaseRepository.UpdateAsync` or `IRepository<TEntity, TKey>.UpdateAsync` itself (e.g. changing it to only mark modified properties) — it is used elsewhere in the codebase for entities that are not already tracked (e.g. entities attached via `Attach` rather than `Find`), and altering its semantics is a separate, broader change not requested by the brief.
- `UpdatePurchaseOrderHandler` — already uses the correct pattern and requires no change.
- Introducing optimistic-concurrency tokens (e.g. `row_version`) or `SaveChanges` interceptors — mentioned in the brief only as future risk this fix avoids, not as work to perform now.
- Any other handler in the Purchase module or elsewhere in the codebase that may have a similar redundant-`UpdateAsync` pattern — the brief scopes this to exactly the two named handlers/files; a broader sweep is out of scope for this fix (it could be raised as a separate arch-review finding if desired).
- Database migrations — none are needed; no schema changes.
- E2E test changes — this is a pure backend persistence-efficiency fix with no observable behavior change, so no `frontend/test/e2e` changes are anticipated.

## Open Questions
None.

## Status: COMPLETE
