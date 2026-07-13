# PR Context

- **PR**: #3576 — #3566: Fix InvoiceImportService double-save for new invoices
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3576
- **Branch**: `feature/3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Closes**: issue #3566 (`[arch-review] Invoices: ExecuteImportInvoice saves new invoices twice`)
- **Changes**: +1764 / -9 across 20 files (before backmerge)
- **Absorbed**: backmerged with `main`, conflict resolved, all InvoiceImportService tests passing, pushed. PR is now `MERGEABLE`.

## Backmerge notes

One conflict in `InvoiceImportService.cs`, resolved by combining two **complementary** fixes rather than choosing a side:

- **New-invoice failure path (#3566, this PR)** — detach the `Added` entity via `_repository.DeleteAsync` in the catch block so a half-built row can't be flushed by a later invoice's `SaveChangesAsync` on the shared per-batch DbContext.
- **Existing-invoice failure path (#3575, merged to main via #3583)** — `_repository.RevertTrackedChangesAsync` to undo in-place mutations on a re-imported invoice.
- Kept this PR's core fix: removed the inner `SaveChangesAsync` from `GetOrCreateAsync` (the double-save being fixed) and the tuple return `(IssuedInvoice Invoice, bool IsNew)`.

Resolved catch block now branches on `isNew`: `DeleteAsync` when new, `RevertTrackedChangesAsync` when existing.

## Validation after backmerge

- `dotnet build` → 0 errors.
- `dotnet test --filter "FullyQualifiedName~InvoiceImportService"` → 23/23 passing.
- Invoices slice → 70/72; the 2 failures are pre-existing Testcontainers tests requiring a Docker daemon (unavailable here), not from this merge.
- `dotnet format --verify-no-changes` → clean.

## Description

Closes #3566

`InvoiceImportService.ExecuteImportInvoice` called `SaveChangesAsync` twice per newly-imported invoice (once inside `GetOrCreateAsync` after `AddAsync`, once at the end after ERP sync), costing `2N` DB round trips for N new invoices and leaving a crash window where a half-initialized invoice row could be persisted.

Fix: `GetOrCreateAsync` no longer saves internally and returns `(IssuedInvoice, bool IsNew)`; `ExecuteImportInvoice` skips the trailing `UpdateAsync` for new (already-tracked) invoices and calls `SaveChangesAsync` exactly once at the end. Catch path detaches a failed new invoice to avoid poisoning the shared DbContext. A related pre-existing bug in the existing-invoice path was filed separately as #3575 (now merged to main and integrated here during the backmerge).
