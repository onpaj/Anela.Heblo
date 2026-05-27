# Architecture Review: Decouple Purchase.RecalculatePurchasePrice from Catalog Domain

## Skip Design: true

This is a backend-only architectural refactor. No UI components, screens, layouts, or visual design decisions are involved. The HTTP API surface, MediatR contracts, and OpenAPI client output are explicitly unchanged.

## Architectural Fit Assessment

The proposed design is an **exact replication of the existing, documented pattern** in this codebase. The verification is concrete:

- **`IMaterialCatalogService`** (Purchase-owned contract at `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IMaterialCatalogService.cs`) is implemented by **`PurchaseMaterialCatalogAdapter`** (`internal sealed`, in `Application/Features/Catalog/Infrastructure/`), registered in `CatalogModule.cs:46` as `Scoped`. The Catalog module already imports `Anela.Heblo.Application.Features.Purchase.Contracts` (`CatalogModule.cs:10`), so the new adapter introduces zero new project-level dependencies.
- **`PurchaseAllowlist`** in `ModuleBoundariesTests.cs:84-93` contains exactly one entry — the `RecalculatePurchasePriceHandler → IProductPriceErpClient` coupling — and that entry's own comment explicitly tracks this work as the follow-up. Eliminating it makes the set empty, matching `PackingMaterials`, `ExpeditionListArchive`, and both `Analytics` allowlists.
- The handler at `RecalculatePurchasePriceHandler.cs:83` performs a single, side-effect-only invocation of `_productPriceClient.RecalculatePurchasePrice(bom.BoMId, cancellationToken)` — there is no return value, no state transfer, no transformation. The boundary is trivially narrow and adapts cleanly through a pure delegation method.

Integration points are limited to: (1) Purchase Contracts folder (add interface), (2) Catalog Infrastructure folder (add adapter), (3) `CatalogModule` DI registration, (4) `RecalculatePurchasePriceHandler` constructor + one call site, (5) two test files. No persistence, no API surface, no cross-assembly references change.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────── Purchase module ────────────────────────────────┐
│                                                                                  │
│   RecalculatePurchasePriceHandler                                                │
│         │                                                                        │
│         │ depends on (constructor injection)                                     │
│         ▼                                                                        │
│   IPurchasePriceRecalculationService     ◄── new contract (Purchase-owned)       │
│         (Contracts/)                                                             │
└─────────┬────────────────────────────────────────────────────────────────────────┘
          │ DI binding registered in CatalogModule
          ▼
┌─────────┴──────────────────────── Catalog module ───────────────────────────────┐
│                                                                                  │
│   CatalogPurchasePriceRecalculationAdapter  ◄── new adapter (internal sealed)    │
│         (Infrastructure/)                                                        │
│         │                                                                        │
│         │ delegates to                                                           │
│         ▼                                                                        │
│   IProductPriceErpClient                                                         │
│         (Domain/Features/Catalog/Price/) — unchanged                             │
└──────────────────────────────────────────────────────────────────────────────────┘
          │
          ▼
   FlexiProductPriceErpClient (Adapters layer) — unchanged
```

### Key Design Decisions

#### Decision 1: Contract Naming
**Options considered:**
- `IPurchasePriceRecalculationService` (spec choice)
- `IPurchasePriceRecalculator` (suggested in brief and in the allowlist's tracking comment)

**Chosen approach:** `IPurchasePriceRecalculationService`, as in the spec.

**Rationale:** Matches the existing sibling contract `IMaterialCatalogService` in the same Contracts/ folder. The `*Service` suffix is established convention here; introducing a `*Recalculator` suffix would create an inconsistency at the same boundary. Naming is a one-way door once consumers reference the interface.

#### Decision 2: Async Suffix on the New Method
**Options considered:**
- Match the existing `IProductPriceErpClient.RecalculatePurchasePrice` (no suffix)
- Use `RecalculatePurchasePriceAsync` (spec choice)

**Chosen approach:** `RecalculatePurchasePriceAsync`.

**Rationale:** New code follows the project's documented Async naming convention. The legacy `IProductPriceErpClient` symbol is explicitly out of scope (see spec "Out of Scope" §3). The adapter absorbs the rename mismatch — that is precisely what adapters exist to do.

#### Decision 3: Provider-Side DI Registration
**Options considered:**
- Register binding in `CatalogModule.AddCatalogModule` (spec choice)
- Register in `PurchaseModule.AddPurchaseModule`
- Register in a composition root

**Chosen approach:** `CatalogModule`.

**Rationale:** The provider owns the implementation; the consumer owns the contract. This is the rule established for `IMaterialCatalogService` and is what makes Purchase compile without referencing any Catalog implementation symbol. Putting the binding in Purchase would force Purchase to know about `CatalogPurchasePriceRecalculationAdapter`, defeating the entire point of the refactor.

#### Decision 4: Adapter Visibility
**Options considered:**
- `internal sealed` (spec choice)
- `public sealed`

**Chosen approach:** `internal sealed`.

**Rationale:** Matches `PurchaseMaterialCatalogAdapter` exactly. The DI container can resolve internal types because `CatalogModule` lives in the same assembly. No external code needs to reference the adapter directly. Sealing prevents accidental subclassing of a pure-delegation type.

#### Decision 5: Service Lifetime
**Options considered:**
- `Scoped` (spec choice, matches `IMaterialCatalogService`)
- `Transient` (matches the underlying `ICatalogRepository`)
- `Singleton` (the adapter is stateless)

**Chosen approach:** `Scoped`.

**Rationale:** Consistency with the sibling adapter. The adapter is stateless so any lifetime is functionally correct, but uniformity reduces cognitive load when reading `CatalogModule`. Scoped also matches the lifetime ceiling implied by handler scopes in the MediatR pipeline.

## Implementation Guidance

### Directory / Module Structure

Files to create:
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs`

Files to modify:
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (add one `AddScoped` line near line 46)
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` (swap dependency)
- `backend/test/Anela.Heblo.Tests/Application/Purchase/RecalculatePurchasePriceHandlerTests.cs` (swap mock)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (empty the `PurchaseAllowlist`)

No files to delete. No project-level reference changes.

### Interfaces and Contracts

```csharp
// New — Purchase-owned
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceRecalculationService
{
    Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);
}
```

```csharp
// New — Catalog-implemented adapter
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPurchasePriceRecalculationAdapter : IPurchasePriceRecalculationService
{
    private readonly IProductPriceErpClient _productPriceErpClient;

    public CatalogPurchasePriceRecalculationAdapter(IProductPriceErpClient productPriceErpClient)
    {
        _productPriceErpClient = productPriceErpClient;
    }

    public Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken) =>
        _productPriceErpClient.RecalculatePurchasePrice(bomId, cancellationToken);
}
```

Developer rules:
- The adapter MUST be pure delegation. No logging, retry, transformation, or null-checks. Existing semantics (including any exceptions thrown by `IProductPriceErpClient`) propagate verbatim — `RecalculatePurchasePriceHandler`'s try/catch at line 78–113 already handles them.
- The handler's exception/error-code surface (`ErrorCodes.InvalidValue`, `ErrorCodes.CatalogItemNotFound`, `ErrorCodes.Exception`) MUST remain bit-identical. This is asserted by the existing nine `[Fact]` tests in `RecalculatePurchasePriceHandlerTests` — keep them all passing.

### Data Flow

For the recalculate-single-product use case:

1. HTTP → `RecalculatePurchasePriceController` → MediatR → `RecalculatePurchasePriceHandler.Handle`
2. Handler validates → calls `IMaterialCatalogService.GetByIdAsync` (existing contract) → builds `bomReferences` list
3. Per BoM reference, handler calls **`IPurchasePriceRecalculationService.RecalculatePurchasePriceAsync(bomId, ct)`** (new line replacing line 83)
4. DI resolves to `CatalogPurchasePriceRecalculationAdapter` → forwards to `IProductPriceErpClient.RecalculatePurchasePrice(bomId, ct)`
5. DI resolves `IProductPriceErpClient` to `FlexiProductPriceErpClient` → calls the ERP

Compared to the current flow, only step 4 is new. All other behavior — including iteration semantics, exception handling, response shape, and logging — is preserved.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Adapter lifetime mismatch with `IProductPriceErpClient` registration (e.g., the ERP client is Singleton while the adapter is Scoped) | Low | Verify in `FlexiAdapterServiceCollectionExtensions` before committing. Captive dependency is harmless for a stateless adapter, but worth a one-line confirmation that lifetimes compose cleanly. |
| Other Purchase types (handlers, services, controllers) still reference Catalog symbols, causing `Consumer_types_should_not_reference_provider_owned_namespaces` to fail after the allowlist is emptied | Low | Run `dotnet test --filter ModuleBoundariesTests` locally after the change. If new violations surface, they were latent under the original allowlist scope and need to be addressed under their own tickets — **do not expand the allowlist**. |
| Test file path drift: spec suggests `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs` but mentions following "the existing test folder convention" | Low | Confirmed convention by inspection: `PurchaseMaterialCatalogAdapterTests.cs` and `CatalogAnalyticsSourceAdapterTests.cs` both live in `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`. Use that exact directory. |
| Adapter changes inadvertently break the `FinancialOverview` consumers (`StockValueService`, `FinancialOverviewModule`) that also reference `IProductPriceErpClient` | Low | The refactor adds a new binding; it does not modify, replace, or unregister `IProductPriceErpClient`. Other consumers continue to inject `IProductPriceErpClient` directly. Verify by running their existing tests (`StockValueServiceTests`, `FinancialOverviewModuleTests`). |
| Internal adapter not resolvable by DI | Negligible | Identical to `PurchaseMaterialCatalogAdapter` which is already `internal sealed` and DI-registered in the same module — pattern is proven. |

## Specification Amendments

The spec is well-grounded and aligns with existing patterns. Three minor amendments recommended:

1. **FR-2 acceptance criterion (test directory):** Replace the parenthetical "(or follow the existing test folder convention for Catalog infrastructure adapters)" with the explicit, verified path `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs`. The convention is unambiguous; the hedge is unnecessary.

2. **FR-3 add an implicit check:** After implementing, run a grep verifying no type under `Anela.Heblo.Application.Features.Purchase.*` references `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*`. Concretely: `grep -r "Anela.Heblo.\(Application\|Domain\)\.Features\.Catalog" backend/src/Anela.Heblo.Application/Features/Purchase/` should return no matches. This is the operational equivalent of NFR-4 and provides a fast pre-test signal.

3. **FR-7 cleanup:** Spec says "comment block describing the violation MUST be removed; if the set is empty, the descriptive comment about the allowlist's purpose can be reduced to one line." To match the empty-allowlist style used by `PackingMaterials`, `ExpeditionListArchive`, and `Analytics` rules (which inline `new HashSet<string>(StringComparer.Ordinal)` directly into the `Rules()` `TheoryData`), consider deleting the `PurchaseAllowlist` field entirely and inlining an empty `HashSet` in the rule definition. This removes a now-pointless named field. (Optional cleanup — does not affect correctness.)

No functional or non-functional requirements need to be added or removed. No data model, API, or persistence amendments.

## Prerequisites

None. Specifically:

- No NuGet package additions.
- No project reference changes (Catalog already references Purchase's Contracts namespace at `CatalogModule.cs:10`).
- No database migration.
- No configuration change (`appsettings.*.json`, environment variables).
- No infrastructure or deployment change.
- No frontend rebuild (OpenAPI client surface is bit-identical, per spec NFR-3).
- No coordinated work in adjacent modules. The `FinancialOverview` violation referenced in spec "Out of Scope" §1 is independent and untouched.

Implementation can begin immediately.