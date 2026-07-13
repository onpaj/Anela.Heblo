# Implementation: revert-tracked-mutation-on-existing-invoice-import-failure

## What was implemented
Fixed a silent data-corruption bug in `InvoiceImportService.ExecuteImportInvoice`. When a re-import of an
existing `IssuedInvoice` mutates the tracked EF Core entity and then a later step in the same
`try` block throws, the entity was previously left in a `Modified` state in the shared, scoped
`DbContext`. A subsequent invoice's `SaveChangesAsync` within the same batch would then silently flush
that partial mutation, even though the first invoice was reported as `Failed`.

The fix adds a narrow `RevertTrackedChangesAsync` method to `IIssuedInvoiceRepository` /
`IssuedInvoiceRepository` that resets the tracked entity's `EntityState` to `Unchanged` (an in-memory,
non-DB-round-trip operation), and calls it from `ExecuteImportInvoice`'s outer `catch` — but only when
the invoice being processed was pre-existing (`isNew == false`) and was actually loaded (`invoice !=
null`). `GetOrCreateAsync`'s return type was changed from `Task<IssuedInvoice>` to
`Task<(IssuedInvoice invoice, bool isNew)>` to surface that flag; it has exactly one call site, so no
other code needed updating.

The inner `try`/`catch` around `_issuedInvoiceClient.SaveAsync` (the `SyncFailed(...)` path) was
deliberately left untouched, per spec — that path's persisted status update is an intentional,
immediately-flushed write and out of scope for this fix. The new-invoice (`isNew == true`) path also
receives no revert/delete logic, per spec (confirmed out of scope).

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` — added
  `Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)` to
  the interface.
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — implemented
  `RevertTrackedChangesAsync` as a synchronous `Context.Entry(entity).State = EntityState.Unchanged` reset,
  with an inline comment documenting that this accepts current values as the new baseline rather than
  rolling back CLR property values (safe here since nothing re-reads a failed invoice's in-memory object
  later in the batch).
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — changed
  `GetOrCreateAsync`'s signature to return `(IssuedInvoice invoice, bool isNew)`; declared `invoice`/`isNew`
  above the `try` in `ExecuteImportInvoice` so both are visible in the `catch`; call
  `_repository.RevertTrackedChangesAsync(invoice, cancellationToken)` from the outer catch when
  `!isNew && invoice != null`, before re-throwing.

## Tests
No new tests in this task (regression coverage is task 2,
`add-regression-test-for-tracked-mutation-revert`). Ran the existing
`backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` suite (21 tests) — all pass
unchanged, confirming no behavior change to the happy-path re-import, new-invoice, or existing
failure-reporting flows.

## How to verify
1. `dotnet build Anela.Heblo.sln` from the repo root — succeeds with 0 errors.
2. `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore` — clean, no output.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests" --no-build` — 21/21 pass.
4. `grep -rn "GetOrCreateAsync" backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — confirms the only call site was updated; no other production caller of this private method exists.

## Notes
No deviations from the task-plan/task-context. The `dotnet build` run surfaced one pre-existing,
unrelated warning (`MSB3073`, access-matrix generator tool exiting with code 134) that is not connected to
this change and was present before this fix.

## PR Summary
`InvoiceImportService.ExecuteImportInvoice` reused the same scoped EF Core `DbContext`/change tracker
across every invoice in an import batch. For a re-imported (existing) invoice, `_mapper.Map(invoiceDetail,
invoice)` mutated the tracked entity directly; if a later step in the same `try` threw, the per-invoice
catch reported the invoice as `Failed` but never reverted the tracked entity's state — so the next
invoice's `SaveChangesAsync` in the same batch could silently flush that partial mutation into the
database.

This adds a narrow `RevertTrackedChangesAsync(entity)` method on `IIssuedInvoiceRepository` /
`IssuedInvoiceRepository` (an in-memory `EntityState.Unchanged` reset, no DB round-trip) and calls it from
`ExecuteImportInvoice`'s outer catch only for pre-existing invoices that were actually loaded. The
new-invoice path and the inner `SaveAsync`-failure path (which is an intentional, immediately-persisted
status update) are unchanged.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` — new interface member
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — `RevertTrackedChangesAsync` implementation
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — `GetOrCreateAsync` now returns `(invoice, isNew)`; outer catch reverts tracked mutations for existing invoices

## Status
DONE
