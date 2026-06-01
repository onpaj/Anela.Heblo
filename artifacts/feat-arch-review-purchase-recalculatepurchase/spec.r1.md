# Specification: Decouple Purchase.RecalculatePurchasePrice from Catalog Domain

## Summary
The `RecalculatePurchasePriceHandler` in the Purchase module directly injects `IProductPriceErpClient`, an interface owned by the Catalog domain. This violates the project's module isolation rule and is currently tolerated via an explicit allowlist entry in `ModuleBoundariesTests`. This specification defines the work required to lift that dependency behind a Purchase-owned contract implemented by an adapter in the Catalog module, mirroring the established `IMaterialCatalogService` / `PurchaseMaterialCatalogAdapter` pattern.

## Background
The codebase follows a Vertical Slice / Clean Architecture where each feature module (Purchase, Catalog, Logistics, etc.) is intended to be independently deployable. The governing rule in `docs/architecture/development_guidelines.md` is:

> Communication between modules **exclusively through `contracts/`** … When module A needs data from module B: define the interface in **module A's contracts**, implement it in module B, never access module B's domain or infrastructure directly.

The `RecalculatePurchasePriceHandler` currently breaks this rule at two locations:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs:3` — `using Anela.Heblo.Domain.Features.Catalog.Price;`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs:12,17` — `IProductPriceErpClient` is a constructor dependency

The violation is already tracked: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:84-93` contains a `PurchaseAllowlist` entry permitting this single coupling pending decoupling work. The allowlist comment explicitly notes that the fix is "out of scope for the 2026-05-24 Purchase ↔ Catalog decoupling" and tracks it as a follow-up. This specification IS that follow-up.

The pattern to apply is already proven in this codebase by two precedents:

1. `IMaterialCatalogService` (Purchase contract) implemented by `PurchaseMaterialCatalogAdapter` (Catalog infrastructure), registered in `CatalogModule.AddCatalogModule()` at line 46.
2. `ILeafletKnowledgeSource` (Leaflet contract) implemented by `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase infrastructure) — the canonical example in the architecture docs.

Beyond restoring architectural conformance, this also reduces blast radius for the planned Phase-2 split into per-module DbContexts and microservices.

## Functional Requirements

### FR-1: Introduce Purchase-owned recalculation contract
A new interface, `IPurchasePriceRecalculationService`, MUST be added to the Purchase module's contracts folder. It exposes only the single operation the handler currently consumes.

**Location:** `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs`

**Signature:**
```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceRecalculationService
{
    Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);
}
```

**Acceptance criteria:**
- File exists in the path above with the namespace `Anela.Heblo.Application.Features.Purchase.Contracts`.
- The interface declares exactly one method matching the signature above (preserving the existing `bomId` parameter type and the cancellation token contract).
- The method name uses the `Async` suffix to match repository naming conventions (existing client uses `RecalculatePurchasePrice` without suffix; the new contract aligns with `IMaterialCatalogService` naming).
- No other operations are added speculatively — YAGNI applies.

### FR-2: Implement Catalog-owned adapter
A new adapter, `CatalogPurchasePriceRecalculationAdapter`, MUST be added to the Catalog module's infrastructure folder. It implements `IPurchasePriceRecalculationService` by delegating to the existing `IProductPriceErpClient`.

**Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs`

**Structure:** Sealed `internal` class with a constructor accepting `IProductPriceErpClient`. The single method delegates straight through; no business logic is added.

**Acceptance criteria:**
- Implementation lives in namespace `Anela.Heblo.Application.Features.Catalog.Infrastructure`, mirroring `PurchaseMaterialCatalogAdapter`.
- The adapter is `internal sealed` (matches the existing `PurchaseMaterialCatalogAdapter` visibility/sealing).
- `RecalculatePurchasePriceAsync(bomId, ct)` forwards the call verbatim to `_productPriceErpClient.RecalculatePurchasePrice(bomId, ct)`.
- No error handling, retry, logging, or transformation is added in the adapter — semantics must be identical to the current direct call.

### FR-3: Register DI binding in CatalogModule
The DI binding for `IPurchasePriceRecalculationService → CatalogPurchasePriceRecalculationAdapter` MUST be registered inside `CatalogModule.AddCatalogModule(...)`, alongside the existing `IMaterialCatalogService` registration.

**Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

**Acceptance criteria:**
- A new line `services.AddScoped<IPurchasePriceRecalculationService, CatalogPurchasePriceRecalculationAdapter>();` is added near line 46 (next to the `PurchaseMaterialCatalogAdapter` registration).
- The Purchase module's `PurchaseModule.cs` is NOT modified — the provider (Catalog) owns the binding.
- Lifetime is `Scoped` to match `IMaterialCatalogService` and the surrounding pattern.
- No code in any module under `Application/Features/Purchase/` references `IProductPriceErpClient` after the change.

### FR-4: Replace dependency in RecalculatePurchasePriceHandler
`RecalculatePurchasePriceHandler` MUST be modified to depend on `IPurchasePriceRecalculationService` instead of `IProductPriceErpClient`.

**Location:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs`

**Changes:**
- Remove `using Anela.Heblo.Domain.Features.Catalog.Price;` (line 3).
- Replace field `IProductPriceErpClient _productPriceClient` with `IPurchasePriceRecalculationService _priceRecalculationService` (lines 12, 21).
- Replace constructor parameter type and assignment (lines 17, 21).
- Replace call at line 83 from `_productPriceClient.RecalculatePurchasePrice(bom.BoMId, cancellationToken)` to `_priceRecalculationService.RecalculatePurchasePriceAsync(bom.BoMId, cancellationToken)`.

**Acceptance criteria:**
- The handler has zero `using` directives or symbol references pointing at any `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*` namespace.
- Public behavior of the handler is unchanged: identical request/response shapes, identical error codes (`ErrorCodes.InvalidValue`, `ErrorCodes.CatalogItemNotFound`, `ErrorCodes.Exception`), identical iteration semantics over BoM references, identical logging messages.
- The handler still runs successfully through the existing MediatR pipeline.

### FR-5: Update existing unit tests
`RecalculatePurchasePriceHandlerTests` currently mocks `IProductPriceErpClient` directly. The tests MUST be updated to mock the new contract.

**Location:** `backend/test/Anela.Heblo.Tests/Application/Purchase/RecalculatePurchasePriceHandlerTests.cs`

**Changes:**
- Remove `using Anela.Heblo.Domain.Features.Catalog.Price;` (line 4).
- Replace `Mock<IProductPriceErpClient> _productPriceClientMock` with `Mock<IPurchasePriceRecalculationService> _priceRecalculationServiceMock`.
- Replace all `.Setup(x => x.RecalculatePurchasePrice(...))` and `.Verify(...)` calls with the new method name `RecalculatePurchasePriceAsync`.
- Existing `[Fact]` test cases (single product, recalculate-all, not-found, no-BoM, ERP failure, mixed success/failure, empty BoM, invalid request, cancellation token) MUST all pass unchanged in intent.

**Acceptance criteria:**
- The test file has no references to `IProductPriceErpClient` after the change.
- All nine existing tests pass: `Handle_WithValidSingleProduct_ShouldRecalculateSuccessfully`, `Handle_WithRecalculateAll_ShouldProcessOnlyProductsWithBoM`, `Handle_WithSingleProductNotFound_ShouldReturnError`, `Handle_WithSingleProductWithoutBoM_ShouldFail`, `Handle_WithErpClientFailure_ShouldRecordError`, `Handle_WithMixedSuccessAndFailure_ShouldRecordBoth`, `Handle_WithNoProductsWithBoM_ShouldReturnEmptyResult`, `Handle_WithInvalidRequest_ShouldReturnError`, `Handle_WithCancellationToken_ShouldPassTokenToClients`.

### FR-6: Add adapter unit test
A new test class MUST be added for `CatalogPurchasePriceRecalculationAdapter` to verify the delegation contract.

**Location:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs` (or follow the existing test folder convention for Catalog infrastructure adapters).

**Required coverage:**
- Test that `RecalculatePurchasePriceAsync(bomId, ct)` invokes `IProductPriceErpClient.RecalculatePurchasePrice(bomId, ct)` exactly once with the same arguments.
- Test that the cancellation token is forwarded verbatim.
- Test that exceptions thrown by `IProductPriceErpClient` propagate unchanged.

**Acceptance criteria:**
- Tests use xUnit + FluentAssertions + Moq, matching the project test conventions.
- Tests follow the Arrange-Act-Assert structure.

### FR-7: Remove allowlist entry and validate architecture test
The `PurchaseAllowlist` in `ModuleBoundariesTests` MUST be reduced (or removed entirely if this was its only entry) to assert that the violation is fixed.

**Location:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:84-93`

**Changes:**
- Remove the single allowlist entry: `"Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice.RecalculatePurchasePriceHandler -> Anela.Heblo.Domain.Features.Catalog.Price.IProductPriceErpClient"`.
- The `PurchaseAllowlist` becomes an empty `HashSet<string>` (keep the field for future entries — matches the pattern used for `PackingMaterials` / `ExpeditionListArchive` / `Analytics` rules).
- The associated comment block describing the violation MUST be removed; if the set is empty, the descriptive comment about the allowlist's purpose can be reduced to one line.

**Acceptance criteria:**
- `Consumer_types_should_not_reference_provider_owned_namespaces` MUST pass for the `Purchase -> Catalog` rule after the changes.
- No other allowlist entry is added or expanded as part of this work.

## Non-Functional Requirements

### NFR-1: Performance
The adapter is a pure delegation layer. Acceptable overhead is one additional virtual dispatch per call. There MUST be no measurable change to recalculation throughput or latency.

### NFR-2: Security
No new public attack surface is introduced. The adapter is internal-sealed, registered via DI only. Authentication and authorization behaviors of the existing `RecalculatePurchasePrice` endpoint are unchanged because the handler entry point is untouched.

### NFR-3: Backward compatibility
- The MediatR contract (`RecalculatePurchasePriceRequest` / `RecalculatePurchasePriceResponse`) is NOT modified.
- The HTTP API surface (controller endpoint, route, request/response shape, status codes) is NOT modified.
- No database schema or persistence changes.
- No frontend changes — the OpenAPI client output MUST remain bit-identical.

### NFR-4: Architecture conformance
After this change, the `ModuleBoundariesTests` reflection-based test enforces — and MUST continue to enforce — that no type in `Anela.Heblo.Application.Features.Purchase.*` references any type in `Anela.Heblo.Domain.Features.Catalog.*`, `Anela.Heblo.Application.Features.Catalog.*`, or `Anela.Heblo.Persistence.Catalog.*` without an explicit allowlist entry. The Purchase allowlist MUST be empty after this work.

### NFR-5: Testability and conventions
- All code follows `dotnet format` and nullable reference type rules.
- C# style: classes (not records) for service implementations; `sealed` and explicit access modifiers; expression-bodied members only where they remain readable.
- Test naming preserves the existing `MethodUnderTest_Scenario_ExpectedBehavior` pattern.

## Data Model
No data model changes. The single value carried across the new contract boundary is `int bomId`, which is already a primitive integer in the existing code path.

## API / Interface Design

### Internal interface (new)
```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceRecalculationService
{
    Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);
}
```

### Internal adapter (new)
```csharp
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

### DI registration (added to `CatalogModule.AddCatalogModule`)
```csharp
services.AddScoped<IPurchasePriceRecalculationService, CatalogPurchasePriceRecalculationAdapter>();
```

### External API surface
No changes. The existing controller route and MediatR request/response remain in place.

## Dependencies
- No new NuGet packages.
- No changes to existing references between projects (`Anela.Heblo.Application` already references the namespaces involved).
- The existing implementation of `IProductPriceErpClient` (e.g. `FlexiProductPriceErpClient` in `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs`) is untouched.

## Out of Scope
The following are explicitly **not** part of this work:

- Decoupling `FinancialOverview` from `IProductPriceErpClient`. `StockValueService` and `FinancialOverviewModule` reference the same Catalog-owned interface; this is a separate boundary violation tracked outside this task.
- Relocating `IProductPriceErpClient` itself out of the Catalog domain. The interface is an ERP integration boundary and may legitimately live in Catalog or be moved to a shared integrations namespace; that decision is outside this task.
- Renaming `IProductPriceErpClient.RecalculatePurchasePrice` to use the `Async` suffix.
- Adding integration tests or end-to-end tests for the recalculation flow beyond what already exists.
- Changing any behavior of the recalculation logic (validation rules, error codes, logging, iteration semantics).
- Updating documentation files other than what is strictly required by the change (no edits to `development_guidelines.md` are needed — this change conforms to the existing rules).

## Open Questions
None.

## Status: COMPLETE