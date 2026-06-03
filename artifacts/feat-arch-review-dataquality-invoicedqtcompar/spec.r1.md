# Specification: Decouple `InvoiceDqtComparer` from Invoices Module Interfaces

## Summary
Replace `InvoiceDqtComparer`'s direct dependencies on the Invoices module's `IIssuedInvoiceSource` and `IIssuedInvoiceClient` with narrow, read-only contracts owned by the DataQuality module. The Invoices module will provide thin adapters that delegate to its existing implementations, restoring proper consumer-owned-contract boundaries per the architecture guidelines.

## Background
`InvoiceDqtComparer` is a DataQuality service that compares issued invoices between Shoptet (source) and the ERP (Abra Flexi) to detect inconsistencies. It currently consumes two Invoices-module interfaces directly:

- `IIssuedInvoiceSource` — used only for `GetAllAsync(...)`, but also exposes `CommitAsync` and `FailAsync` (Outbox-pattern hooks).
- `IIssuedInvoiceClient` — used only for `GetAllAsync(...)`, but also exposes `SaveAsync` and `GetAsync` (write operations).

This violates two cross-module conventions documented in `docs/architecture/development_guidelines.md` § Cross-Module Communication:

1. **Consumer defines the contract.** A consuming module declares only the operations it actually uses.
2. **Provider implements via an adapter.** The provider module supplies an adapter binding its internal services to the consumer's contract.

The current arrangement:
- Couples DataQuality to write/transaction semantics it never invokes.
- Forces test doubles to implement four unused methods.
- Inverts ownership — Invoices' interface evolution drives DataQuality changes.

The arch-review routine flagged this on 2026-06-01 as a clean, low-risk refactor with high architectural value.

## Functional Requirements

### FR-1: New DataQuality-owned read contracts
DataQuality declares two interfaces in `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/` (or equivalent DataQuality contracts location — see Open Questions), each exposing only the single `GetAllAsync` operation `InvoiceDqtComparer` consumes.

**Acceptance criteria:**
- `IInvoiceShoptetSource` is added with the signature:
  ```csharp
  Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query, CancellationToken ct = default);
  ```
- `IInvoiceErpClient` is added with the signature:
  ```csharp
  Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct);
  ```
- Both interfaces live under the DataQuality module's namespace and folder structure.
- Neither interface exposes `CommitAsync`, `FailAsync`, `SaveAsync`, or `GetAsync`.
- Domain types reused on the interface signatures (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`) remain in their current Invoices-module location; DataQuality references them as shared domain models. (Reuse is acceptable because they are domain DTOs, not behavior contracts — see Open Questions if a deeper split is desired.)

### FR-2: Adapter implementations in Invoices module
The Invoices module provides two adapter classes in its `Infrastructure/` folder that implement the new DataQuality contracts by delegating to the existing `IIssuedInvoiceSource` / `IIssuedInvoiceClient` services.

**Acceptance criteria:**
- An adapter class (e.g. `InvoiceShoptetSourceAdapter`) implements `IInvoiceShoptetSource` and forwards `GetAllAsync` to the existing `IIssuedInvoiceSource` instance.
- An adapter class (e.g. `InvoiceErpClientAdapter`) implements `IInvoiceErpClient` and forwards `GetAllAsync` to the existing `IIssuedInvoiceClient` instance.
- Adapters contain no business logic — they only delegate.
- Adapters live in the Invoices module so the Invoices module owns its provider-side mapping.

### FR-3: DI registration update in `InvoicesModule.cs`
The Invoices module's DI registration binds the two new DataQuality contracts to the new adapters.

**Acceptance criteria:**
- `InvoicesModule.cs` (or the equivalent module registration class) registers `IInvoiceShoptetSource → InvoiceShoptetSourceAdapter` and `IInvoiceErpClient → InvoiceErpClientAdapter`.
- Existing registrations for `IIssuedInvoiceSource` and `IIssuedInvoiceClient` remain unchanged — internal Invoices consumers still resolve them.
- The application starts and resolves `InvoiceDqtComparer` successfully.

### FR-4: Refactor `InvoiceDqtComparer` to consume new contracts
`InvoiceDqtComparer` is updated to depend on `IInvoiceShoptetSource` and `IInvoiceErpClient` instead of the Invoices interfaces.

**Acceptance criteria:**
- `InvoiceDqtComparer`'s constructor takes `IInvoiceShoptetSource` and `IInvoiceErpClient`.
- The `using Anela.Heblo.Domain.Features.Invoices;` import is replaced where possible; if domain DTOs still require it, the using stays but no behavior interface is referenced.
- Behavior of `InvoiceDqtComparer` (inputs, outputs, side effects) is unchanged.
- No other consumer of `IIssuedInvoiceSource` / `IIssuedInvoiceClient` is affected.

### FR-5: Tests updated to use new contracts
Existing unit tests for `InvoiceDqtComparer` are updated to mock the new narrow contracts.

**Acceptance criteria:**
- Tests mocking `IIssuedInvoiceSource` / `IIssuedInvoiceClient` for `InvoiceDqtComparer` are rewritten to mock `IInvoiceShoptetSource` / `IInvoiceErpClient`.
- Mocks no longer need to set up unused `CommitAsync` / `FailAsync` / `SaveAsync` / `GetAsync` methods.
- All previously-passing tests for `InvoiceDqtComparer` still pass after the refactor.
- If no unit tests currently exist for `InvoiceDqtComparer`, none must be added by this work item (kept out of scope) — but smoke coverage via integration tests must still pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable runtime impact. Adapters add a single virtual method dispatch per call; `GetAllAsync` is invoked at most a few times per DataQuality comparison run, so the overhead is negligible.

### NFR-2: Security
No change to authentication, authorization, or data exposure. The new interfaces expose a strict subset of operations already accessible to DataQuality.

### NFR-3: Maintainability / Architecture
- DataQuality module must compile without referencing any Invoices-module **behavior** interfaces (it may still reference Invoices domain DTOs — see FR-1).
- `dotnet build` succeeds with no new warnings.
- `dotnet format` produces no diffs.
- Architecture tests (if any exist in the solution) continue to pass.

### NFR-4: Backwards compatibility
- Existing Invoices-module consumers of `IIssuedInvoiceSource` / `IIssuedInvoiceClient` keep working unchanged.
- No database, API, or external contract changes.

## Data Model
No data model changes. Existing domain types (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`) are reused on the new interface signatures. No new entities, tables, or migrations.

## API / Interface Design

### New DataQuality contracts (consumer-owned)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/` (final path subject to confirmation — see Open Questions).

```csharp
// IInvoiceShoptetSource.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceShoptetSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default);
}

// IInvoiceErpClient.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceErpClient
{
    Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

### Adapters (provider-owned)
Location: inside the Invoices module under its `Infrastructure/` folder.

```csharp
internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
{
    private readonly IIssuedInvoiceSource _inner;
    public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner) => _inner = inner;

    public Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default)
        => _inner.GetAllAsync(query, ct);
}

internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
{
    private readonly IIssuedInvoiceClient _inner;
    public InvoiceErpClientAdapter(IIssuedInvoiceClient inner) => _inner = inner;

    public Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
        => _inner.GetAllAsync(from, to, ct);
}
```

### DI registration (`InvoicesModule.cs`)
```csharp
services.AddScoped<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();
```
(Lifetime should match the existing `IIssuedInvoiceSource` / `IIssuedInvoiceClient` registrations — assumed Scoped; confirm at implementation.)

### `InvoiceDqtComparer` constructor change
```csharp
// Before
public InvoiceDqtComparer(IIssuedInvoiceSource source, IIssuedInvoiceClient client) { ... }

// After
public InvoiceDqtComparer(IInvoiceShoptetSource source, IInvoiceErpClient client) { ... }
```

## Dependencies
- **Existing Invoices module** must remain functional and continue to provide `IIssuedInvoiceSource` and `IIssuedInvoiceClient` to its own internal consumers.
- **Domain DTOs** (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`) — referenced from new contracts; no change required.
- **DI container** — standard `Microsoft.Extensions.DependencyInjection`; no new packages.
- **Architecture guideline doc** `docs/architecture/development_guidelines.md` § Cross-Module Communication — drives the chosen pattern.

## Out of Scope
- Refactoring other DataQuality services that may depend on other modules' interfaces (e.g., catalog, stock). This work item is scoped strictly to invoices.
- Splitting domain DTOs out of the Invoices module into a shared kernel. Reuse of `IssuedInvoiceDetail` and friends across modules is accepted.
- Adding new unit tests where none exist today for `InvoiceDqtComparer`.
- Changing the behavior of `InvoiceDqtComparer` (comparison rules, output, logging).
- Renaming or restructuring the existing `IIssuedInvoiceSource` / `IIssuedInvoiceClient` interfaces.
- Removing the unused methods (`CommitAsync`, `FailAsync`, `SaveAsync`, `GetAsync`) from the Invoices interfaces — they remain in use elsewhere in Invoices.

## Open Questions
1. **Contract folder location.** The brief suggests `DataQuality/Contracts/`. Confirm whether DataQuality already has a `Contracts/` folder and whether it should live under `Application/Features/DataQuality/Contracts/` or under `Domain/Features/DataQuality/Contracts/`. Assumed: `Application/Features/DataQuality/Contracts/` (matches Vertical Slice convention used elsewhere). Implementer should verify against existing module conventions.
2. **Adapter lifetime.** Assumed `Scoped` to mirror the wrapped services. If `IIssuedInvoiceSource` or `IIssuedInvoiceClient` is registered with a different lifetime (Transient/Singleton), the adapters must match.
3. **Domain DTO ownership.** `IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, and `IssuedInvoiceSourceQuery` are currently in `Anela.Heblo.Domain.Features.Invoices`. The new DataQuality contracts will continue to reference them across module boundaries. Confirm this is acceptable, or whether a follow-up work item should extract these into a shared kernel. (Recommendation: accept as-is; treat domain DTOs as shareable model types, distinct from behavior contracts.)
4. **Naming of `IInvoiceShoptetSource`.** The brief alternates between `IInvoiceDataSource` and `IInvoiceShoptetSource`. The latter is more descriptive (it explicitly names the source system) and is used in the suggested fix block. Going with `IInvoiceShoptetSource`. Confirm this matches naming conventions used by other DataQuality contracts.

## Status: HAS_QUESTIONS