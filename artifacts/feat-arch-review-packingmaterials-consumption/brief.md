## Module
PackingMaterials

## Finding
`ConsumptionCalculationService` directly injects and uses `IIssuedInvoiceRepository` from the Invoices module's domain:

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs:2` — `using Anela.Heblo.Domain.Features.Invoices;`
- `ConsumptionCalculationService.cs:11,14-16` — constructor injects `IIssuedInvoiceRepository invoiceRepository`

`IIssuedInvoiceRepository` is defined in `Anela.Heblo.Domain.Features.Invoices`, making it a domain type owned by the Invoices module. The PackingMaterials `ConsumptionCalculationService` reaches directly into the Invoices module's domain layer to fetch invoice headers.

The project's architecture guidelines explicitly forbid this: "Direct access to another module's entities" and require that cross-module communication go through a contract interface defined in the consumer's own `Contracts/` folder (see the `ILeafletKnowledgeSource` pattern in `development_guidelines.md`).

The `PackingMaterialsModule.cs` comment at line 18 acknowledges the coupling but treats it as acceptable: *"Note: ConsumptionCalculationService depends on IIssuedInvoiceRepository, which is registered by InvoicesModule"*.

## Why it matters
- PackingMaterials cannot be developed, deployed, or tested in isolation — it is tightly coupled to the Invoices module's domain type.
- Any change to `IIssuedInvoiceRepository`'s signature can break PackingMaterials compilation.
- The coupling is invisible at the module boundary — there is no declared contract between the two modules.
- It violates the inversion-of-dependency pattern established by the `ILeafletKnowledgeSource` precedent in this codebase.

## Suggested fix
Apply the same pattern as `ILeafletKnowledgeSource`:

1. Define a narrow contract in PackingMaterials' own `Contracts/` folder — e.g. `IIssuedInvoiceSource` — exposing only what this module needs:
   ```csharp
   // Application/Features/PackingMaterials/Contracts/IIssuedInvoiceSource.cs
   public interface IIssuedInvoiceSource
   {
       Task<IEnumerable<IssuedInvoiceHeader>> GetHeadersByDateAsync(DateOnly date, CancellationToken ct = default);
   }
   // IssuedInvoiceHeader is a value type owned by PackingMaterials, not imported from Invoices
   ```
2. `ConsumptionCalculationService` injects `IIssuedInvoiceSource` instead of `IIssuedInvoiceRepository`.
3. The Invoices module provides an adapter implementing `IIssuedInvoiceSource` and registers it in `InvoicesModule.cs`.

---
_Filed by daily arch-review routine on 2026-05-20._