## Module
Invoices

## Finding
`IssuedInvoiceRepository` lives at `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` in namespace `Anela.Heblo.Application.Features.Invoices.Infrastructure`.

It directly imports the Persistence layer:
```csharp
using Anela.Heblo.Persistence;                   // line 4
using Anela.Heblo.Persistence.Repositories;      // line 5
```
and extends `BaseRepository<IssuedInvoice, string>` which takes an `ApplicationDbContext`.

Every other module's repository implementation is in `Anela.Heblo.Persistence/` (verified across ~35 repositories: `ArticleRepository`, `LeafletDocumentRepository`, `PurchaseOrderRepository`, `PackingMaterialRepository`, etc.). Invoices is the only exception.

## Why it matters
`development_guidelines.md` states explicitly: *"A repository's **implementation** lives in `Anela.Heblo.Persistence`"*. Placing it in Application instead:
- Reverses the Clean Architecture dependency direction: Application now depends on the Infrastructure/Persistence layer, where the flow should be Persistence → Application.
- Contradicts the codebase's own established pattern (all other repositories are in Persistence).
- Makes the Application project un-testable in isolation without pulling in EF Core / `ApplicationDbContext`.
- Adds friction to the eventual Phase 2 migration to per-module DbContexts, because the implementation isn't where the migration tooling expects to find it.

## Suggested fix
Move `IssuedInvoiceRepository.cs` to `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` (same directory as the existing `IssuedInvoiceConfiguration.cs` and `IssuedInvoiceSyncDataConfiguration.cs`). The interface `IIssuedInvoiceRepository` stays in `Application/Features/Invoices/Contracts/` — that part is correct. Update the namespace to `Anela.Heblo.Persistence.Invoices` and adjust the DI binding in `InvoicesModule.cs` (no functional change needed — it already references the concrete type by name).

---
_Filed by daily arch-review routine on 2026-06-09._