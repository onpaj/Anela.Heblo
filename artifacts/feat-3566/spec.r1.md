# Specification: Remove redundant `SaveChangesAsync` in `InvoiceImportService.ExecuteImportInvoice`

## Summary
`InvoiceImportService.ExecuteImportInvoice` currently persists every newly-imported invoice with two separate database round trips instead of one: an internal `SaveChangesAsync` inside `GetOrCreateAsync` right after the entity is added, followed by the normal `UpdateAsync` + `SaveChangesAsync` at the end of the method once ERP sync has completed. This spec removes the first (redundant) save so a new invoice is written exactly once, atomically, with its final synced state. This is a targeted correctness/performance fix to one private service — no public contracts, DTOs, or database schema change.

## Background
`ExecuteImportInvoice` is called once per invoice inside `ImportInvoicesAsync`'s batch loop (`backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`). For an invoice that does not yet exist locally, the current flow is:

1. `GetOrCreateAsync` (lines 127-138) doesn't find the invoice, creates it via the mapper factory, calls `_repository.AddAsync(...)`, and immediately calls `_repository.SaveChangesAsync(...)` (line 134) — persisting the invoice with no sync data.
2. Back in `ExecuteImportInvoice`, `_mapper.Map(invoiceDetail, invoice)` (line 90) re-applies the same source data the factory already mapped, transformations run, the ERP (FlexiBee) sync happens, and finally `_repository.UpdateAsync(invoice, ...)` + `_repository.SaveChangesAsync(...)` (lines 115-116) persist the sync outcome.

For a re-imported (already existing) invoice, only step 2's save fires — this path is already correct and must not change.

Every new invoice in an import batch therefore costs two database round trips, and if the process fails between the two saves, the invoice row is left in a half-initialized state (no `SyncHistory`, `IsSynced = false`, no `LastSyncTime`) even though a fresh source record was just processed.

### Verified technical risk in the brief's literal suggested fix
The brief proposes simply deleting the `SaveChangesAsync` call from inside `GetOrCreateAsync` and relying on the existing `UpdateAsync` + `SaveChangesAsync` at the end of `ExecuteImportInvoice` to flush both new and existing invoices. Taken literally, this breaks new-invoice imports.

This was verified empirically (EF Core 8, InMemory provider, `Microsoft.EntityFrameworkCore.InMemory` 8.0.10): the underlying repository is EF Core-backed (`IssuedInvoiceRepository : BaseRepository<IssuedInvoice, string>`, `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` and `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`). `BaseRepository.AddAsync` calls `DbSet.AddAsync(entity)`, which tracks the entity in EF Core's `Added` state. `BaseRepository.UpdateAsync` calls `DbSet.Update(entity)`. When `Update()` is invoked on an entity that is **already tracked** by the same `DbContext` (which is the case here — same request-scoped context, same tracked instance), EF Core sets that entry's state to `Modified` **regardless of its previous state**, overwriting `Added`. On `SaveChangesAsync`, EF Core then generates an `UPDATE` statement for a row that does not exist yet, affecting 0 rows, which EF Core reports as a `DbUpdateConcurrencyException` (confirmed via a minimal repro: `Add` → `Update` on the same tracked entity → `SaveChangesAsync` throws `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist in the store`).

A second repro confirmed the safe alternative: if the newly-added entity's properties are mutated directly (as `_mapper.Map(...)`, `invoice.SyncSucceeded(...)`, and `invoice.SyncFailed(...)` already do) **without** calling `UpdateAsync`/`DbSet.Update()` on it again, the entity remains in `Added` state and a single `SaveChangesAsync()` correctly generates one `INSERT` with the final, fully-populated values.

**Conclusion:** the fix must not call `_repository.UpdateAsync(...)` on an invoice that was just created in the same call to `ExecuteImportInvoice`. `GetOrCreateAsync` must therefore expose whether the entity was newly created, so the caller can skip the `UpdateAsync` call on that path while still calling `SaveChangesAsync` exactly once. This does not require any interface (`IIssuedInvoiceRepository`) changes — `GetOrCreateAsync` is a private helper local to `InvoiceImportService`.

## Functional Requirements

### FR-1: Single database round trip for newly-imported invoices
Remove the `SaveChangesAsync` call currently inside `GetOrCreateAsync` (line 134). The method should only call `_repository.AddAsync(...)` to register a new entity with the change tracker, without flushing it.

**Acceptance criteria:**
- `GetOrCreateAsync` no longer calls `_repository.SaveChangesAsync(...)`.
- `_repository.AddAsync(...)` is still called exactly once when no existing invoice is found for the given key.
- Behavior for an existing invoice (found via `GetByIdAsync`) is unchanged: no `AddAsync` call, the found entity is returned as-is for further processing.

### FR-2: Caller must not call `UpdateAsync` on a newly-created (unsaved) invoice
`ExecuteImportInvoice` must know whether the invoice returned by `GetOrCreateAsync` is newly created or pre-existing, and must skip the `_repository.UpdateAsync(...)` call for newly created invoices (relying on EF Core's automatic tracking of property changes on the already-`Added` entity). For pre-existing invoices, `_repository.UpdateAsync(...)` continues to be called exactly as today.

`_repository.SaveChangesAsync(...)` continues to be called exactly once at the end of `ExecuteImportInvoice`, unconditionally, for both new and existing invoices — this is the single flush point.

**Acceptance criteria:**
- For a new invoice: `AddAsync` is called once, `UpdateAsync` is **not** called, `SaveChangesAsync` is called exactly once (previously: `SaveChangesAsync` was called twice).
- For an existing (re-imported) invoice: `GetByIdAsync` finds it, `UpdateAsync` is called once, `SaveChangesAsync` is called exactly once — identical to current behavior.
- All fields set by `_mapper.Map(invoiceDetail, invoice)` (line 90), `invoice.SyncSucceeded(...)`, and `invoice.SyncFailed(...)` are present in the persisted row after the single `SaveChangesAsync` call, for both new and existing invoices.
- No `DbUpdateConcurrencyException` (or any other EF Core state-tracking error) occurs when importing a new invoice. This must be covered by a test that exercises a real EF Core change tracker (e.g., the SQLite/InMemory-backed integration test fixture already used in `InvoiceImportIntegrationTests.cs`, or an equivalent test using a real `ApplicationDbContext`) — the existing `InvoiceImportServiceTests.cs` suite uses fully mocked `IIssuedInvoiceRepository` and therefore cannot detect this class of bug (Moq has no concept of EF Core entity state transitions).

### FR-3: Preserve crash-safety improvement
If the process fails after `AddAsync` but before the single `SaveChangesAsync` call completes, no partial invoice row must exist in the database (this is a strict improvement over current behavior, where a partial, un-synced row can be committed by the first save and left behind if the process crashes before the second save).

**Acceptance criteria:**
- No new invoice row exists in the database unless the entire `ExecuteImportInvoice` flow (including ERP sync outcome recording) has completed and the single `SaveChangesAsync` has succeeded.

### FR-4: No behavior change to existing (re-import) invoices
The re-import path (invoice already exists) must remain byte-for-byte behaviorally identical: `GetByIdAsync` finds the invoice, `_mapper.Map(invoiceDetail, invoice)` refreshes core fields (this mapping call is unchanged and out of scope — see Out of Scope), transformations and ERP sync run as before, `UpdateAsync` + `SaveChangesAsync` persist the result in one round trip, same as today.

**Acceptance criteria:**
- Existing tests `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting` and `ImportInvoicesAsync_WithExistingInvoice_RefreshesCoreDataFromSource` (in `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`) continue to pass unmodified (or with only mechanical updates, e.g., mock verification counts) after the change.

## Non-Functional Requirements

### NFR-1: Performance
For a batch containing N newly-imported invoices, the number of `SaveChangesAsync` calls attributable to this method drops from `2N` to `N` (existing/re-imported invoices are unaffected, already at 1 save each). No change to batching strategy — this fix does not introduce cross-invoice batching of `SaveChangesAsync`; each invoice still flushes independently, exactly once, matching the existing per-invoice transactional boundary and error-isolation behavior in `ImportInvoicesAsync`'s try/catch-per-invoice loop.

### NFR-2: Correctness / data integrity
No regression in persisted data: after the fix, a freshly-imported invoice's final row must contain the same field values it would have contained under the old two-save behavior (core fields from `invoiceDetail`, plus `IsSynced`/`ErrorType`/`SyncHistory`/`LastSyncTime` reflecting the ERP sync outcome). This must be validated by a test that reads the invoice back from a real (non-mocked) persistence layer after import, not solely by verifying mock call counts.

### NFR-3: No interface/contract changes
`IIssuedInvoiceRepository`, `IIssuedInvoiceImportTransformation`, `IIssuedInvoiceClient`, and all public DTOs are unchanged. `GetOrCreateAsync` is a `private` method on `InvoiceImportService` — its signature may change freely (e.g., returning a tuple or an out-parameter indicating whether the entity was newly created) without affecting any other code.

## Data Model
No schema or entity changes. Relevant existing entity: `IssuedInvoice` (`backend/src/Anela.Heblo.Domain/Features/Invoices/`), including its `SyncHistory` collection, `IsSynced`, `ErrorType`, `LastSyncTime`, and `ConcurrencyStamp` fields, all populated as they are today — only the number/ordering of database writes changes.

## API / Interface Design
No public API changes. This is an internal implementation change to `InvoiceImportService.ExecuteImportInvoice` and `InvoiceImportService.GetOrCreateAsync` (both private members) in `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`. `IInvoiceImportService.ImportInvoicesAsync` — the only public entry point — keeps its existing signature and return type (`ImportResultDto`).

Suggested internal shape (illustrative, not prescriptive — implementation detail left to the architect/developer):
```csharp
private async Task<(IssuedInvoice Invoice, bool IsNew)> GetOrCreateAsync(
    string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
{
    var found = await _repository.GetByIdAsync(key, cancellationToken);
    if (found == null)
    {
        found = factory();
        await _repository.AddAsync(found, cancellationToken);
        return (found, true);
    }
    return (found, false);
}
```
with `ExecuteImportInvoice` skipping `_repository.UpdateAsync(invoice, cancellationToken)` when `IsNew` is `true`, and always calling `_repository.SaveChangesAsync(cancellationToken)` once.

## Dependencies
- EF Core 8 change-tracking semantics (`Microsoft.EntityFrameworkCore`), specifically that a tracked `Added` entity's properties can be mutated freely and will be included in the next `SaveChangesAsync` as part of the original `INSERT`, without needing (and without tolerating) an explicit `Update()` call.
- Existing `IIssuedInvoiceRepository` / `BaseRepository<TEntity, TKey>` implementations (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`, `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`) — no changes required to these files.
- Existing test infrastructure: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` (mock-based unit tests, need mechanical updates to save/update call-count assertions) and `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportIntegrationTests.cs` (real-context integration test fixture, needed to validate FR-2's concurrency-safety criterion).

## Out of Scope
- The redundant `_mapper.Map(invoiceDetail, invoice)` call on line 90. It is unavoidable as currently structured: it's required to refresh core fields on the *existing-invoice* (re-import) path, and the code does not currently branch on new-vs-existing before this line. Removing or conditionalizing it is not part of this fix and was not requested by the brief.
- Any change to the per-invoice transactional/error-isolation boundary in `ImportInvoicesAsync` (each invoice is still saved and can fail independently within a batch).
- Batching multiple invoices into a single `SaveChangesAsync` across the loop in `ImportInvoicesAsync`.
- Any change to `IIssuedInvoiceRepository`, `BaseRepository<TEntity, TKey>`, or other repositories that also pair `AddAsync` with an unconditional `UpdateAsync` call — this spec covers only `InvoiceImportService`. (If the same `Add`-then-`Update`-in-one-unit-of-work pattern exists elsewhere in the codebase, it would carry the same latent risk described in Background, but auditing other call sites is not part of this task.)
- Marketing invoice import (`MarketingInvoiceImportServiceTests.cs`) — separate service, not touched by this fix.

## Open Questions
None.

## Status: COMPLETE
