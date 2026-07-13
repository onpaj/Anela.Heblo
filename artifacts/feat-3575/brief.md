## Module
Invoices

## Context
Discovered during code review of #3566 (fix for the `ExecuteImportInvoice` double-save bug, PR pending). This is a **separate, pre-existing** bug, not introduced or worsened by that fix — confirmed by diff: the code paths described below are byte-identical before and after #3566's change. Filing separately since fixing it is out of scope for that narrow bugfix (its spec explicitly required "No behavior change to existing (re-import) invoices").

## Finding
`InvoiceImportService`, `IIssuedInvoiceRepository`, and `ApplicationDbContext` are registered `AddScoped` (`backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`), and `ImportInvoicesAsync`'s `foreach` loop over a batch's invoices reuses the *same* instances — and therefore the same EF Core change tracker — for every invoice in the batch.

`IssuedInvoiceRepository.GetByIdAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:22-26`) returns a **tracked** entity (no `.AsNoTracking()`, unlike other repositories in this codebase). In `ExecuteImportInvoice`, for a re-imported (existing) invoice, `_mapper.Map(invoiceDetail, invoice)` mutates that tracked entity directly, and the transformation pipeline (`ProductMappingIssuedInvoiceImportTransformation`, `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation`, `GiftWithoutVATIssuedInvoiceImportTransformation`, etc.) can throw.

If an exception is thrown anywhere between the tracked load and this invoice's own `UpdateAsync`/`SaveChangesAsync` call, `ImportInvoicesAsync`'s per-invoice `catch` records the invoice as `Failed` and moves on — but the mutated, tracked entity is never reverted or detached. EF Core's automatic change detection will still pick up those mutations as `Modified` the next time **any other invoice** in the same batch calls `SaveChangesAsync`, silently persisting the partially-mutated row for an invoice that was reported as failed.

## Why it matters
- Silent data corruption: a "failed" invoice's row can still end up updated in the database with partial/incorrect field values, with no error surfaced anywhere.
- Existing mocked unit tests (`InvoiceImportServiceTests.cs`) can't catch this — it requires a real EF Core change tracker (InMemory or Testcontainers-backed test), similar to the regression coverage added for the new-invoice case in #3566.

## Suggested fix
In `ExecuteImportInvoice`'s outer `catch`, when the invoice was *not* newly created (`isNew == false`) but was loaded and possibly mutated, revert its tracked state before the next iteration's `SaveChangesAsync` can flush it — e.g. `_context.Entry(invoice).State = EntityState.Unchanged` (discarding in-memory mutations) or `_context.Entry(invoice).Reload()`. Do **not** use `DeleteAsync`/`Remove()` for this case — unlike a new, never-saved invoice, an existing invoice has a real persisted row, and `Remove()` would mark it for deletion.

This likely requires exposing a small amount of tracking-state control from `IIssuedInvoiceRepository` (or handling it directly against `ApplicationDbContext` from `InvoiceImportService`, if that's already accessible) — needs an architecture pass to decide the right abstraction boundary, hence filing as its own issue rather than folding into #3566.

## Suggested test
A batch with two invoices: invoice A already exists in the DB; its refresh-map or a transformation step throws. Invoice B (new or existing) succeeds right after in the same batch. Assert invoice A's row in the DB is unchanged from its pre-import state.

---
_Filed during code review of #3566 by the automated oneshot pipeline._
