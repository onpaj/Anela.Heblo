# Design: Invoices ExecuteImportInvoice Saves New Invoice Twice

## Component Design

**`InvoiceImportService`** (`backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`) — sole component touched. Public surface (`IInvoiceImportService.ImportInvoicesAsync`) is unchanged; the fix is confined to two private methods.

- **`GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken)`**
  Responsibility: look up an `IssuedInvoice` by key, or create+track a new one via `_repository.AddAsync` if absent. Currently it also flushes (`SaveChangesAsync`) immediately after `AddAsync`, which prematurely commits the new entity and leaves it tracked as `Unchanged`/persisted before the rest of `ExecuteImportInvoice` mutates it — the root cause of the later `UpdateAsync` call flipping it to `Modified` against a state EF doesn't expect, producing `DbUpdateConcurrencyException`.
  Fix: remove the `SaveChangesAsync` call from this method entirely (persistence is deferred to the caller), and change the return type so the caller can distinguish "just created" from "found existing" without re-querying or relying on tracking-state inspection.

- **`ExecuteImportInvoice(IssuedInvoiceDetail, CancellationToken)`**
  Responsibility: orchestrate a single invoice's import — resolve/create the entity, refresh fields from source, run transformations, sync to the external client, then persist. It owns the single `SaveChangesAsync` call for the whole operation (already true today for the re-import path; now also true for the new-invoice path).
  Fix: consume the `IsNew` flag from `GetOrCreateAsync` to conditionally call `_repository.UpdateAsync` — only for entities that were *found* (already persisted, so an explicit update-tracking call is correct and necessary), never for entities that were just `AddAsync`-tracked in this same unit of work (calling `UpdateAsync` on an `Added` entity is redundant and is what triggers the concurrency exception). `SaveChangesAsync` remains unconditional, called exactly once at the end, for both branches.

No other component, interface, DTO, or public contract changes. `IIssuedInvoiceRepository` (`AddAsync`, `UpdateAsync`, `GetByIdAsync`, `SaveChangesAsync`) keeps its existing signatures.

## Data Schemas

No database schema, migration, external API, or DTO changes. The only shape change is the internal method signature below (private, not exposed outside `InvoiceImportService`):

```csharp
// Before
private async Task<IssuedInvoice> GetOrCreateAsync(
    string key,
    Func<IssuedInvoice> factory,
    CancellationToken cancellationToken = default)

// After
private async Task<(IssuedInvoice Invoice, bool IsNew)> GetOrCreateAsync(
    string key,
    Func<IssuedInvoice> factory,
    CancellationToken cancellationToken = default)
```

Behavioral contract of the new return value:
- `IsNew == true`: `Invoice` was just constructed via `factory()` and added to the change tracker in this call (`_repository.AddAsync`) but **not yet saved**. Caller must not call `UpdateAsync` on it before the final `SaveChangesAsync`.
- `IsNew == false`: `Invoice` was loaded from the store via `_repository.GetByIdAsync` (pre-existing, already persisted). Caller should call `_repository.UpdateAsync` before `SaveChangesAsync`, as today.

`ExecuteImportInvoice` call-site shape change (persistence branch only):

```csharp
var (invoice, isNew) = await GetOrCreateAsync(
    invoiceDetail.Code,
    () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail),
    cancellationToken);

// ... field refresh, transformations, external sync (unchanged) ...

if (!isNew)
{
    await _repository.UpdateAsync(invoice, cancellationToken);
}
await _repository.SaveChangesAsync(cancellationToken);
```

Resulting entity-state/audit-field behavior (relevant for the new EF Core InMemory-backed test):
- New invoice: `CreationTime` and `ConcurrencyStamp` set (by `AddAsync`/entity defaults); `LastModificationTime` stays `null`, since the modification-stamping path lives in `UpdateAsync`, which is now skipped for new entities. Exactly one `SaveChangesAsync` occurs for the whole invoice import.
- Existing invoice (re-import): unchanged from current behavior — `UpdateAsync` then `SaveChangesAsync`, `LastModificationTime` stamped.
