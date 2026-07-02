# Specification: Remove DataQuality → Catalog Application-layer boundary violation in ProductPairingDqtComparer

## Summary
`ProductPairingDqtComparer` (DataQuality module) directly depends on `ICatalogResilienceService`, an Application-layer internal service owned by the Catalog module. This violates the project's documented module-boundary rule that cross-module reads must go through a consumer-owned contract. This spec defines a DataQuality-owned resilience contract (`IDqtResilienceService`), implemented by a Catalog-side adapter, following the exact pattern already established in this codebase for `IStockOperationQuery`/`IStockTakingQuery`. No behavioral change to retry/circuit-breaker/timeout semantics is intended — this is a pure dependency-direction fix.

## Background
`docs/architecture/development_guidelines.md` establishes the cross-module communication rule: when module A needs read/behavioral access to something owned by module B, **A defines the contract in its own `Contracts/` folder**, and **B implements an adapter that delegates to its internal service**, registered by B in its own `{Module}.cs`. This pattern is already implemented twice in this exact module pair:

- `Anela.Heblo.Application.Features.DataQuality.Contracts.IStockOperationQuery` → implemented by `Anela.Heblo.Application.Features.Catalog.Infrastructure.DataQualityStockOperationQueryAdapter`, registered in `CatalogModule.cs`.
- `Anela.Heblo.Application.Features.DataQuality.Contracts.IStockTakingQuery` → implemented by `Anela.Heblo.Application.Features.Catalog.Infrastructure.DataQualityStockTakingQueryAdapter`, registered in `CatalogModule.cs`.

`ProductPairingDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`) does not follow this pattern for its resilience needs: it imports `Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService` directly and injects the Catalog-owned singleton `CatalogResilienceService` to wrap its two calls to `IEshopStockClient.ListAsync` and `IErpStockClient.ListAsync`. This means:

- DataQuality's resilience behavior (retry count, backoff, circuit-breaker thresholds, timeout) silently changes whenever Catalog tunes its own pipeline for Catalog's workload — with no corresponding DataQuality code change or review trigger.
- DataQuality cannot tune resilience appropriate to its own (batch, non-interactive, tolerant-of-latency) comparison workload independently of Catalog's (interactive, cache-refresh) workload.
- This is the same violation pattern previously filed as #3433 (`FinancialOverview.StockValueService` importing Catalog-owned ERP interfaces directly), flagged by the same daily arch-review routine, confirming Catalog is a repeat source of leaked Application-layer internals.

This is a finding from the daily architecture-review routine (filed 2026-07-01), not a functional bug report — there is no reported production incident. The fix is scoped narrowly to `ProductPairingDqtComparer`'s dependency on `ICatalogResilienceService`; it does not address `ProductPairingDqtComparer`'s existing (unflagged) direct dependency on the Catalog-owned Domain interfaces `IEshopStockClient`/`IErpStockClient`, which live in `Anela.Heblo.Domain.Features.Catalog.Stock` and are already consumed directly by other modules (e.g. `FinancialOverview.StockValueService`) — that is a separate, out-of-scope architectural question.

### Why Option A (contract + adapter) over Option B (push resilience into the shared client adapters)
The brief proposes two options. Investigation of the actual code rules out Option B as the correct choice for this fix:

- `IEshopStockClient` is implemented by `ShoptetStockClient` and `IErpStockClient` by `FlexiStockClient`, both in the **Adapters** layer (`Anela.Heblo.Adapters.ShoptetApi`, `Anela.Heblo.Adapters.Flexi`), not in Catalog's Application layer. They are shared infrastructure consumed by multiple modules — Catalog itself (`CatalogDataRefreshService`, `EshopStockDomainService`), FinancialOverview (`StockValueService`), and DataQuality (`ProductPairingDqtComparer`).
- Neither adapter currently wraps its calls with `ICatalogResilienceService` or any retry/circuit-breaker logic; each has its own bespoke try/catch/log-and-rethrow. Moving `ExecuteWithResilienceAsync` calls into these adapters would apply Catalog's resilience policy to **every** consumer of these shared clients (including ones that today have no resilience wrapping at all, like `StockValueService` and `CatalogDataRefreshService`'s direct calls), which is a materially larger behavioral change than this finding calls for and conflicts with the "surgical changes" project rule.
- Option A exactly mirrors the two adapters already in place for this same module pair (`DataQualityStockOperationQueryAdapter`, `DataQualityStockTakingQueryAdapter`), so it is the lowest-risk, most consistent fix and requires no code changes outside DataQuality's contract + Catalog's adapter registration.

This spec therefore specifies **Option A**.

## Functional Requirements

### FR-1: DataQuality-owned resilience contract
Define a new interface `IDqtResilienceService` in `Anela.Heblo.Application.Features.DataQuality.Contracts` (file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs`), mirroring the shape of `ICatalogResilienceService`:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

**Acceptance criteria:**
- Interface lives under `DataQuality/Contracts/`, namespace `Anela.Heblo.Application.Features.DataQuality.Contracts`.
- Method signature is identical in shape to `ICatalogResilienceService.ExecuteWithResilienceAsync<T>` (same generic execution wrapper pattern), so the call sites in `ProductPairingDqtComparer` require no logic changes beyond the injected type and `using` statement.
- No DataQuality code references `Anela.Heblo.Application.Features.Catalog.*` anywhere after this change (verified by `grep -rn "Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality` returning no `using` of Catalog Application-layer namespaces from Application-layer DataQuality files under `Services/`, excluding the pre-existing, out-of-scope `IEshopStockClient`/`IErpStockClient` Domain-layer references).

### FR-2: Catalog-side adapter implementing the contract
Add an adapter class in Catalog's Infrastructure folder that implements `IDqtResilienceService` by delegating to the existing `CatalogResilienceService`/`ICatalogResilienceService` singleton, following the naming convention of the existing `DataQualityStockOperationQueryAdapter` / `DataQualityStockTakingQueryAdapter`.

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs`

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;

    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService)
    {
        _resilienceService = resilienceService;
    }

    public Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
```

**Acceptance criteria:**
- Class is `internal sealed`, matching the visibility of the existing two adapters in the same folder.
- Class lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure` (provider-owned), not in DataQuality.
- Adapter delegates 1:1 to `ICatalogResilienceService` — no new resilience logic, no behavior change to retry count, backoff, circuit-breaker thresholds, or timeout for calls that currently go through this path.
- `operationName` values passed through unchanged (`"ProductPairingDqtComparer.EshopList"`, `"ProductPairingDqtComparer.ErpList"`) so existing log correlation / circuit-breaker operation-key semantics are preserved.

### FR-3: DI registration owned by the provider
Register the adapter in `CatalogModule.cs` (provider-owned registration, per the documented rule that "the consumer module never touches this registration"), immediately adjacent to the existing `IStockOperationQuery`/`IStockTakingQuery` DataQuality-adapter registrations for discoverability, with a comment matching the existing style:

```csharp
// DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

Location: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, near lines 60–62 (the existing `IStockOperationQuery`/`IStockTakingQuery` registrations) or near line 92 (`ICatalogResilienceService` registration) — implementer's choice, but must be grouped with one of these two existing blocks and carry an explanatory comment.

**Acceptance criteria:**
- Registration lifetime is `Scoped`, matching the other two DataQuality-facing adapters in `CatalogModule.cs` (not `Singleton`, even though the underlying `ICatalogResilienceService` is registered as `Singleton` — the adapter itself has no state, so lifetime mismatch is not a functional concern, but `Scoped` keeps it consistent with sibling adapters).
- `DataQualityModule.cs` is **not** modified to register `IDqtResilienceService` — registration must live in `CatalogModule.cs` per the "provider registers" rule.
- Application still resolves `IDqtResilienceService` successfully at startup (no DI resolution failure) whenever both `CatalogModule.AddCatalogModule()` and `DataQualityModule.AddDataQualityModule()` are registered together, which is the case in `Program.cs` for all current hosting entry points.

### FR-4: Update `ProductPairingDqtComparer` to depend on the new contract
Modify `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`:
- Remove `using Anela.Heblo.Application.Features.Catalog.Infrastructure;`.
- Add `using Anela.Heblo.Application.Features.DataQuality.Contracts;`.
- Change the constructor parameter and backing field type from `ICatalogResilienceService` to `IDqtResilienceService` (field name `_resilienceService` may stay unchanged; only the type changes).
- No change to `CompareAsync` method body — both `_resilienceService.ExecuteWithResilienceAsync(...)` call sites are unchanged since the interface shape is identical.

**Acceptance criteria:**
- `ProductPairingDqtComparer` no longer references any type from `Anela.Heblo.Application.Features.Catalog.*`.
- `ProductPairingDqtComparer`'s public behavior (mismatch detection logic, `DriftComparisonResult` output, exception propagation on resilience exhaustion) is unchanged — this is a pure type-substitution refactor.

### FR-5: Update existing unit tests
Modify `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`:
- Replace `Mock<ICatalogResilienceService> _resilienceMock` with `Mock<IDqtResilienceService> _resilienceMock`.
- Replace the `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` with `using Anela.Heblo.Application.Features.DataQuality.Contracts;`.
- All 5 existing test cases (`CompareAsync_ReturnsEmpty_WhenAllProductsPaired`, `CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp`, `CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp`, `CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts`, `CompareAsync_WrapsBothListCalls_WithResilience`) must continue to pass unmodified in their assertions — only the mocked type changes.

**Acceptance criteria:**
- All 5 existing tests pass after the type substitution with no assertion changes.
- No test references `ICatalogResilienceService` anywhere in the DataQuality test namespace after this change.

### FR-6 (recommended, optional): Adapter-level unit test
Add a small test for `DataQualityResilienceAdapter` verifying it delegates to the injected `ICatalogResilienceService` unchanged (operation, operationName, cancellationToken all passed through, return value passed through). This is optional given the adapter is a one-line delegation, but is consistent with test coverage on the two sibling adapters if such tests exist.

**Acceptance criteria:**
- If added, test lives under `backend/test/Anela.Heblo.Tests/Features/Catalog/` (or wherever sibling adapter tests, if any, are located) and verifies pass-through delegation only — no new resilience behavior to test.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence
This change must produce byte-for-byte identical runtime behavior for `ProductPairingDqtComparer`: same retry count (3), same exponential backoff with jitter, same circuit-breaker thresholds (50% failure ratio, min throughput 3, 1-minute sampling, 30s break duration), same 30s timeout, same log messages emitted by `CatalogResilienceService`, same `InvalidOperationException` wrapping on `BrokenCircuitException`. The adapter introduces zero new logic — it is a pure pass-through. (A future, separate change may choose to give DataQuality its own tuned resilience pipeline; that is explicitly out of scope here — see Out of Scope.)

### NFR-2: Security
No change. No new external dependencies, no new secrets, no change to authentication/authorization. Internal DI wiring change only.

### NFR-3: Compile-time boundary enforcement
After this change, no source file under `Anela.Heblo.Application/Features/DataQuality/` may contain a `using Anela.Heblo.Application.Features.Catalog.*;` directive that references Catalog's `Infrastructure`, `Services`, `Cache`, `CostProviders`, `Validators`, `UseCases`, or `DashboardTiles` namespaces (i.e. anything other than Catalog's own `Contracts/` folder, which does not apply here since DataQuality is the consumer, not Catalog). This is a review-time check, not an automated build-time one (no architecture-test tooling for this is confirmed present in the repo as part of this change; see Open Questions).

## Data Model
No data model changes. No new entities, no persistence changes, no DTOs. This is a pure DI/interface-boundary refactor within the Application layer.

## API / Interface Design

**New contract** (DataQuality-owned):
```
Anela.Heblo.Application.Features.DataQuality.Contracts.IDqtResilienceService
    Task<T> ExecuteWithResilienceAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
```

**New adapter** (Catalog-owned, internal):
```
Anela.Heblo.Application.Features.Catalog.Infrastructure.DataQualityResilienceAdapter : IDqtResilienceService
    (delegates to ICatalogResilienceService)
```

**DI registration** (in `CatalogModule.AddCatalogModule`):
```
services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

**Modified consumer**:
```
ProductPairingDqtComparer(
    IEshopStockClient eshopStockClient,
    IErpStockClient erpStockClient,
    IDqtResilienceService resilienceService,   // was: ICatalogResilienceService
    ILogger<ProductPairingDqtComparer> logger)
```

No HTTP endpoints, no MediatR requests/responses, and no UI are affected by this change.

## Dependencies
- Depends on the existing `ICatalogResilienceService`/`CatalogResilienceService` implementation in `Anela.Heblo.Application.Features.Catalog.Infrastructure` remaining functionally unchanged and registered as `Singleton` in `CatalogModule.cs` (line 92, unchanged by this spec).
- Depends on both `CatalogModule.AddCatalogModule()` and `DataQualityModule.AddDataQualityModule()` being registered together at startup (already the case; verify in `Program.cs`, not expected to require changes).
- No new NuGet packages, no new external services.

## Out of Scope
- Giving DataQuality its own independently-tuned resilience pipeline (different retry/circuit-breaker/timeout parameters). This change only relocates the dependency direction; it does not change policy values. A future change could give `DataQualityResilienceAdapter` (or a DataQuality-owned `DqtResilienceService` implementation) its own Polly pipeline if DataQuality's workload characteristics warrant it — that is a product/architecture decision outside this fix.
- `ProductPairingDqtComparer`'s direct dependency on `IEshopStockClient`/`IErpStockClient` (Catalog-owned Domain-layer interfaces in `Anela.Heblo.Domain.Features.Catalog.Stock`). The brief and this finding scope the violation specifically to the Application-layer `ICatalogResilienceService`; the Domain-layer stock-client interfaces are consumed directly by multiple modules today (including `FinancialOverview.StockValueService`) and are not flagged here. Whether Domain-layer interfaces under a module's namespace should also be wrapped in consumer-owned contracts is a broader architectural question for a separate review item.
- Fixing the previously-filed, structurally-similar #3433 (`FinancialOverview.StockValueService` importing Catalog-owned ERP interfaces). Referenced only as precedent; not remediated by this change.
- Any change to `ShoptetStockClient` or `FlexiStockClient` (the Adapters-layer implementations of `IEshopStockClient`/`IErpStockClient`). Option B from the brief (pushing resilience into these adapters) is explicitly rejected for this fix — see Background section for reasoning.
- Automated architecture-boundary enforcement (e.g. an ArchUnit-style test that fails the build if DataQuality references Catalog's Application-layer namespaces). Not currently present in the repo as far as this investigation found; introducing one is a separate, larger initiative.
- Any change to `StockWriteBackDqtComparer`, `InvoiceDqtComparer`, or other `DataQuality/Services/*` classes, even if they have similar or different cross-module dependencies — this spec is scoped to `ProductPairingDqtComparer` only, per the brief.

## Open Questions
None.

## Status: COMPLETE
