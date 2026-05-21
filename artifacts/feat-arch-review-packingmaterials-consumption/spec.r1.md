# Specification: Decouple PackingMaterials.ConsumptionCalculationService from Invoices domain

## Summary
Invert the dependency between PackingMaterials and Invoices by introducing a PackingMaterials-owned contract for the minimal invoice data needed during daily consumption calculation. The Invoices module will provide an adapter, eliminating PackingMaterials' direct compile-time reference to `Anela.Heblo.Domain.Features.Invoices` types. The work mirrors the established `ILeafletKnowledgeSource` precedent and brings PackingMaterials under the same automated module-boundary enforcement applied to Leaflet and Logistics.

## Background
`ConsumptionCalculationService` in `Anela.Heblo.Application.Features.PackingMaterials.Services` currently injects `IIssuedInvoiceRepository` and consumes `IssuedInvoice` entities directly from the Invoices module's domain layer. This violates the project's module-boundary rules (`docs/architecture/development_guidelines.md` — *"Direct access to another module's entities"* is forbidden), is acknowledged inline as a known coupling in `PackingMaterialsModule.cs:18`, and is **not** yet protected by the architecture test in `ModuleBoundariesTests.cs`. Two prior decoupling efforts — `2026-05-15-decouple-leaflet-from-knowledgebase.md` and `2026-05-16-decouple-logistics-from-manufacture.md` — established the inversion pattern. PackingMaterials is the next module to bring in line, allowing it to be developed, deployed, and unit-tested in isolation.

The Invoices module exposes a rich repository surface (sync history, error filters, pagination, etc.). PackingMaterials needs only one operation — `GetHeadersByDateAsync(DateOnly)` — and only two fields from each result (`Id`, `ItemsCount`).

## Functional Requirements

### FR-1: Define consumer-owned contract in PackingMaterials
Introduce an interface in `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/` exposing only the operation that `ConsumptionCalculationService` consumes.

**Note on naming:** `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceSource` already exists (Shoptet-side ingestion contract) and is unrelated. To avoid collision and clarify direction, name the new contract `IInvoiceConsumptionSource` (consumer-side, read-only).

```csharp
// Application/Features/PackingMaterials/Contracts/IInvoiceConsumptionSource.cs
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public interface IInvoiceConsumptionSource
{
    Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}
```

**Acceptance criteria:**
- Interface lives in `Anela.Heblo.Application.Features.PackingMaterials.Contracts` namespace.
- Interface declares exactly one method: `GetHeadersByDateAsync(DateOnly, CancellationToken)`.
- Interface has no `using Anela.Heblo.Domain.Features.Invoices;` directive.
- Return type is `IReadOnlyList<T>` (consistent with the `ILeafletKnowledgeSource` precedent).

### FR-2: Define PackingMaterials-owned value type for invoice header
Introduce an immutable value type in PackingMaterials representing only the fields the consumption logic needs from an invoice header. `IssuedInvoice` must not leak across the module boundary.

```csharp
// Application/Features/PackingMaterials/Contracts/InvoiceConsumptionHeader.cs
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public sealed record InvoiceConsumptionHeader(string Id, int ItemsCount);
```

**Acceptance criteria:**
- Type lives in `Anela.Heblo.Application.Features.PackingMaterials.Contracts`.
- Type is a `sealed record` with `Id` (string, matches `IssuedInvoice.Id` type) and `ItemsCount` (int).
- Type has no reference to `Anela.Heblo.Domain.Features.Invoices`.
- The two fields are sufficient to evaluate every existing branch of `BuildFactRows` (`PerOrder` uses `Id`; `PerProduct` uses `Id` and `ItemsCount`; `PerDay` uses neither).

### FR-3: Refactor `ConsumptionCalculationService` to depend on the contract
Replace the `IIssuedInvoiceRepository` constructor dependency with `IInvoiceConsumptionSource`, and replace `List<IssuedInvoice>` in `BuildFactRows` with `IReadOnlyList<InvoiceConsumptionHeader>`.

**Acceptance criteria:**
- `ConsumptionCalculationService.cs` has no `using Anela.Heblo.Domain.Features.Invoices;` directive.
- Constructor injects `IInvoiceConsumptionSource` instead of `IIssuedInvoiceRepository`.
- `BuildFactRows` accepts `IReadOnlyList<InvoiceConsumptionHeader>` and operates only on `Id` and `ItemsCount`.
- The semantics of `ProcessDailyConsumptionAsync` are unchanged for all three `ConsumptionType` branches (`PerDay`, `PerOrder`, `PerProduct`), as evidenced by the existing test suite continuing to pass without behavioral assertion changes.

### FR-4: Provide adapter in Invoices module
Create an internal adapter in the Invoices module that implements `IInvoiceConsumptionSource` by delegating to `IIssuedInvoiceRepository.GetHeadersByDateAsync` and projecting each `IssuedInvoice` into an `InvoiceConsumptionHeader`.

**Acceptance criteria:**
- Adapter lives at `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/<AdapterName>.cs` (mirror the location pattern of `KnowledgeBaseLeafletSourceAdapter`).
- Adapter class is `internal sealed`.
- Adapter projects each `IssuedInvoice` to `new InvoiceConsumptionHeader(invoice.Id, invoice.ItemsCount)`.
- Adapter passes the `CancellationToken` through.

### FR-5: Register the adapter in InvoicesModule
The provider module (Invoices) owns the DI registration, per the precedent set in `KnowledgeBaseModule.cs:38`.

**Acceptance criteria:**
- Adapter registered as `services.AddScoped<IInvoiceConsumptionSource, ...>()` inside `InvoicesModule.cs`.
- Registration is accompanied by a short comment matching the style of the `KnowledgeBaseModule` precedent (cross-module contract, owned by consumer, implemented by provider).
- The cross-module note at `PackingMaterialsModule.cs:18` is removed; PackingMaterials no longer has any DI-time knowledge of Invoices.

### FR-6: Extend `ModuleBoundariesTests` with a PackingMaterials → Invoices rule
Add a third rule to the existing theory in `ModuleBoundariesTests.cs` so the new boundary is regression-protected.

**Acceptance criteria:**
- A new `ModuleBoundaryRule` entry is added with:
  - `Name = "PackingMaterials -> Invoices"`
  - `InspectedNamespacePrefix = "Anela.Heblo.Application.Features.PackingMaterials"`
  - `ForbiddenNamespacePrefixes = ["Anela.Heblo.Domain.Features.Invoices", "Anela.Heblo.Application.Features.Invoices", "Anela.Heblo.Persistence.Invoices"]`
  - `Allowlist = empty set` (the goal of this work is to leave no residual violations).
- The new theory case passes after the refactor.

### FR-7: Update unit tests to use the contract
`ConsumptionCalculationServiceTests.cs` and the existing `MockIssuedInvoiceRepository` test double must be replaced or updated so PackingMaterials' test project no longer references `Anela.Heblo.Domain.Features.Invoices`.

**Acceptance criteria:**
- PackingMaterials test file has no `using Anela.Heblo.Domain.Features.Invoices;` directive.
- A new `MockInvoiceConsumptionSource` (or equivalent in-memory fake) implements `IInvoiceConsumptionSource` and is used by all existing test cases.
- Test helper `MakeInvoice` is replaced by `MakeHeader(string id, int itemsCount)` returning `InvoiceConsumptionHeader`.
- All existing assertions and scenarios (`PerDay`, `PerOrder`, `PerProduct`, day-already-processed, no-invoices) continue to pass without behavior change.

### FR-8: Verify no other PackingMaterials code references Invoices
Search the PackingMaterials feature folder and persistence project for any remaining references to `Anela.Heblo.Domain.Features.Invoices` or `Anela.Heblo.Application.Features.Invoices` and remove them.

**Acceptance criteria:**
- `grep -r "Anela.Heblo.*Features.Invoices" backend/src/Anela.Heblo.Application/Features/PackingMaterials backend/src/Anela.Heblo.Persistence/PackingMaterials` returns no results.
- If any unexpected references are discovered during the audit, they are either fixed in scope or explicitly listed in the architecture-test allowlist with a justification comment (matching the precedent in `ModuleBoundariesTests.cs`).

## Non-Functional Requirements

### NFR-1: Performance
- Adapter overhead must be a simple in-memory projection (`Select` over the existing result set). No additional database round-trips, no buffering beyond what `GetHeadersByDateAsync` already returns.
- Daily consumption job runtime characteristics must not regress measurably (it processes one day's invoices, currently on the order of hundreds to low thousands of records).

### NFR-2: Security
- No new attack surface. The new contract exposes strictly less data than the existing repository (two fields instead of the full `IssuedInvoice` entity), which is a small confidentiality improvement.
- No authentication, authorization, or input-validation boundaries change.

### NFR-3: Maintainability
- After the change, PackingMaterials has zero compile-time references to the Invoices module.
- Architecture test enforces the boundary on every build; future violations fail CI rather than slipping in unnoticed.
- The narrow contract surface (one method, two-field record) is intentionally small — adding a new field requires deliberate updates to both modules.

### NFR-4: Test coverage
- 80%+ unit-test coverage is preserved on `ConsumptionCalculationService` (current coverage must not drop).
- The adapter is covered by at least one unit test that verifies the projection from `IssuedInvoice` to `InvoiceConsumptionHeader` and that the `DateOnly` and `CancellationToken` are forwarded.

## Data Model

No database schema changes. Only an in-process value type is introduced:

| Type | Owner | Kind | Fields |
|---|---|---|---|
| `InvoiceConsumptionHeader` | PackingMaterials (Application/Contracts) | `sealed record` | `Id : string`, `ItemsCount : int` |
| `IInvoiceConsumptionSource` | PackingMaterials (Application/Contracts) | `interface` | `Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(DateOnly, CancellationToken)` |

The existing `IssuedInvoice` entity in `Anela.Heblo.Domain.Features.Invoices` is untouched.

## API / Interface Design

No external (HTTP/MediatR) API changes. The MediatR contract `ProcessDailyConsumptionRequest` / `ProcessDailyConsumptionResponse` and its handler are unaffected.

Internal interface change (PackingMaterials → Invoices), illustrated:

```text
Before:
  ConsumptionCalculationService
        |
        v (direct)
  IIssuedInvoiceRepository  ── owned by Invoices.Domain

After:
  ConsumptionCalculationService
        |
        v
  IInvoiceConsumptionSource  ── owned by PackingMaterials.Application.Contracts
        ^
        | (implements)
  InvoiceConsumptionSourceAdapter  ── lives in Invoices.Application.Infrastructure
        |
        v
  IIssuedInvoiceRepository  ── stays internal to Invoices
```

## Dependencies

- **Existing precedents to mirror:**
  - `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` (contract shape).
  - `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` (adapter shape and `internal sealed` modifier).
  - `KnowledgeBaseModule.cs:38` (provider-side DI registration).
  - `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (new rule entry).
- **No new external libraries** required (no NuGet packages, no infrastructure).
- **Build dependency direction**: `Anela.Heblo.Application` already references itself; the adapter (in the Invoices feature folder of the same assembly) is allowed to consume the PackingMaterials contract namespace because the boundary test inspects the PackingMaterials namespace only — provider → consumer-contract references are by design.

## Out of Scope

- Renaming or restructuring the existing `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceSource` (Shoptet ingestion contract). It is unrelated.
- Decoupling other Invoices consumers (if any exist elsewhere) — this spec covers only PackingMaterials.
- Splitting `IIssuedInvoiceRepository` into smaller interfaces, or otherwise refactoring the Invoices domain layer.
- Performance optimization of `GetHeadersByDateAsync`.
- Database schema or migration changes.
- Pre-existing `LeafletAllowlist` and `LogisticsAllowlist` entries — keep as-is.
- The known EF change-tracking subtlety at `ConsumptionCalculationService.cs:69-74` (the marker write when `processedCount == 0`) — preserve the current behavior; do not redesign it.

## Open Questions

None.

## Status: COMPLETE