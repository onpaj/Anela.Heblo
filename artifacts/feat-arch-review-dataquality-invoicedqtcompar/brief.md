## Module
DataQuality

## Finding
`InvoiceDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`, lines 2–3) directly depends on two interfaces owned by the **Invoices** module:

```csharp
using Anela.Heblo.Domain.Features.Invoices;  // IIssuedInvoiceSource, IIssuedInvoiceClient
```

Both are injected in the constructor (lines 12–13). However DataQuality only calls one method on each:
- `IIssuedInvoiceSource.GetAllAsync(...)` — the interface also exposes `CommitAsync` and `FailAsync` which DataQuality never calls.
- `IIssuedInvoiceClient.GetAllAsync(...)` — the interface also exposes `SaveAsync` and `GetAsync` which DataQuality never calls.

Per the cross-module communication pattern documented in `docs/architecture/development_guidelines.md` (§ Cross-Module Communication):
> **Consumer (A) defines the contract.** Module A declares an interface in its own `Contracts/` folder, exposing only the operations it actually consumes (no speculative methods).
> **Provider (B) implements the contract via an adapter.**

DataQuality (consumer) should own narrow read-only contracts like `IInvoiceDataSource` and `IInvoiceErpClient` in `DataQuality/Contracts/`, and the Invoices module should provide adapters that delegate to its existing services.

## Why it matters
- **Module boundary**: DataQuality is tightly coupled to Invoices domain interfaces. Any change to `IIssuedInvoiceSource` or `IIssuedInvoiceClient` signatures forces a DataQuality change.
- **Interface Segregation (ISP)**: DataQuality depends on operations (`CommitAsync`, `FailAsync`, `SaveAsync`) it never calls. Adding a mock/test double for `InvoiceDqtComparer` requires implementing unused methods.
- **Ownership inversion**: the current pattern means the Invoices module implicitly controls what DataQuality can access, rather than DataQuality declaring its own needs.

## Suggested fix
1. Add two narrow interfaces to `DataQuality/Contracts/`:
   ```csharp
   // DataQuality/Contracts/IInvoiceShoptetSource.cs
   public interface IInvoiceShoptetSource
   {
       Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query, CancellationToken ct = default);
   }

   // DataQuality/Contracts/IInvoiceErpClient.cs
   public interface IInvoiceErpClient
   {
       Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct);
   }
   ```
2. Add thin adapters in the **Invoices** module's `Infrastructure/` folder that delegate to the existing implementations.
3. Update `InvoicesModule.cs` DI registration to bind the new contracts to the adapters.
4. Update `InvoiceDqtComparer` to inject the new DataQuality-owned interfaces.

---
_Filed by daily arch-review routine on 2026-06-01._