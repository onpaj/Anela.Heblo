### task: revert-tracked-mutation-on-existing-invoice-import-failure

**Goal:** Stop a failed re-import of an existing `IssuedInvoice` from silently corrupting that row via a
later invoice's `SaveChangesAsync` in the same batch. Add a narrow `RevertTrackedChangesAsync` method to
`IIssuedInvoiceRepository`/`IssuedInvoiceRepository` that resets the tracked entity's `EntityState` to
`Unchanged`, and call it from `InvoiceImportService.ExecuteImportInvoice`'s outer `catch` block only when
the invoice was a pre-existing (not newly created) entity.

**Files to touch:**
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`

**Specific changes:**

1. `IIssuedInvoiceRepository.cs` — add a new member to the interface (grouped with the other
   Invoices-specific members, after `GetHeadersByDateAsync` per the arch review's placement guidance):
   ```csharp
   Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
   ```

2. `IssuedInvoiceRepository.cs` — implement it as a synchronous, in-memory `EntityState` reset (no DB
   round-trip), using the inherited `Context` field (protected on `BaseRepository<TEntity, TKey>` — do not
   introduce a new field):
   ```csharp
   public Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
   {
       // Discards the in-memory mutation applied by _mapper.Map(...) in ExecuteImportInvoice before
       // this invoice's own SaveChangesAsync ran, so it cannot be flushed by a later invoice's
       // SaveChangesAsync within the same batch/DbContext scope.
       // NOTE: this makes Original == Current (accepts current values as the new baseline) — it does
       // NOT roll the CLR object's property values back to what was loaded from the DB. Nothing in this
       // batch re-reads a failed invoice's in-memory object afterward today, so that's safe, but don't
       // rely on the in-memory `entity` reflecting original DB values after this call.
       Context.Entry(entity).State = EntityState.Unchanged;
       return Task.CompletedTask;
   }
   ```
   Add `using Microsoft.EntityFrameworkCore;` if not already present (it is — see existing imports).

3. `InvoiceImportService.cs`:
   - Change `GetOrCreateAsync` to surface whether the entity was newly created, e.g. change its return
     type from `Task<IssuedInvoice>` to `Task<(IssuedInvoice invoice, bool isNew)>`:
     ```csharp
     private async Task<(IssuedInvoice invoice, bool isNew)> GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
     {
         var found = await _repository.GetByIdAsync(key, cancellationToken);
         if (found == null)
         {
             found = factory();
             await _repository.AddAsync(found, cancellationToken);
             await _repository.SaveChangesAsync(cancellationToken);
             return (found, true);
         }

         return (found, false);
     }
     ```
     (`GetOrCreateAsync` is `private` with exactly one call site — `ExecuteImportInvoice` — so this is a
     safe, local signature change; no other caller to update.)
   - In `ExecuteImportInvoice`, declare `invoice`/`isNew` above the `try` so both are visible in the
     `catch` block (a `var` declared inside `try` does not compile if referenced from `catch`), consume
     the tuple from `GetOrCreateAsync`, and call `RevertTrackedChangesAsync` from the outer `catch` when
     `isNew == false`, before re-throwing:
     ```csharp
     private async Task<IssuedInvoice> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
     {
         IssuedInvoice? invoice = null;
         var isNew = false;

         try
         {
             _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

             (invoice, isNew) = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

             // Always refresh core data fields from source (handles re-imports where data may have changed or was missing)
             _mapper.Map(invoiceDetail, invoice);

             // Apply transformations to domain model
             var transformedInvoice = invoiceDetail;
             foreach (var transformation in _importTransformations)
             {
                 transformedInvoice = await transformation.TransformAsync(transformedInvoice, cancellationToken);
             }

             try
             {
                 // Send to external system via abstraction
                 var adapterResponse = await _issuedInvoiceClient.SaveAsync(transformedInvoice, cancellationToken);
                 invoice.SyncSucceeded(transformedInvoice, adapterResponse);
                 _logger.LogInformation(
                     "Successfully imported invoice: {InvoiceNumber}: {InvoiceValue} ({Currency})",
                     invoiceDetail.Code, invoiceDetail.Price.WithVat, invoiceDetail.Price.CurrencyCode);
             }
             catch (Exception ex)
             {
                 var adapterResponse = (ex as IssuedInvoiceClientException)?.RawAdapterResponse;
                 _logger.LogError(ex, "FlexiBee rejected invoice {InvoiceCode}: {Error}", transformedInvoice.Code, ex.Message);
                 invoice.SyncFailed(transformedInvoice, ex.Message, adapterResponse);
             }

             await _repository.UpdateAsync(invoice, cancellationToken);
             await _repository.SaveChangesAsync(cancellationToken);

             return invoice;
         }
         catch (Exception ex)
         {
             if (!isNew && invoice != null)
             {
                 await _repository.RevertTrackedChangesAsync(invoice, cancellationToken);
             }

             _logger.LogError(ex, "Error occurred while importing invoice: {InvoiceNumber}", invoiceDetail.Code);
             throw;
         }
     }
     ```
   - Do **not** touch the inner `try`/`catch` around `_issuedInvoiceClient.SaveAsync` (lines 99-113 in the
     current file) — that path's `SyncFailed(...)` + `UpdateAsync`/`SaveChangesAsync` is an intentional,
     immediately-persisted status update and is explicitly out of scope (spec FR-2 scope boundary, Out of
     Scope list).
   - Do **not** add any revert/delete logic for the `isNew == true` path — confirmed out of scope by the
     spec.

**Verification:**
- `dotnet build` succeeds (interface/implementation/consumer all updated consistently; no other caller of
  `GetOrCreateAsync` exists to break).
- `dotnet format` clean.
- Existing `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` passes unchanged
  — in particular `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting` and
  `ImportInvoicesAsync_WithExistingInvoice_RefreshesCoreDataFromSource` (happy-path re-import behavior must
  be identical to today) and `ImportInvoicesAsync_WithPartialFailure_TracksFailedInvoices` (failure
  reporting/logging behavior must be identical — that test's failing invoice is `isNew` via `GetByIdAsync`
  throwing before any entity is even returned, so `invoice` stays `null` and the new `if (!isNew &&
  invoice != null)` guard correctly skips the revert call for it — confirm this still passes as-is).
- No other call sites of `GetOrCreateAsync` broken by the signature change (`grep -rn "GetOrCreateAsync"
  backend/src` should show only the one call site inside `InvoiceImportService.cs`).
