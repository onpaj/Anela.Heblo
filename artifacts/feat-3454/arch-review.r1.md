# Architecture Review: Remove DataQuality → Catalog Application-layer boundary violation in ProductPairingDqtComparer

## Skip Design: true

Pure backend dependency-direction refactor: new interface, one adapter class, one DI registration line, one constructor type swap, test-double type swap. No controller, MediatR contract, HTTP surface, or UI is touched.

## Architectural Fit Assessment

This fits the codebase's established cross-module decoupling pattern precisely, and it fits it in the most literal way possible: **the exact same module pair (DataQuality ↔ Catalog) already has two working instances of this pattern**, committed and tested:

- `Anela.Heblo.Application.Features.DataQuality.Contracts.IStockOperationQuery` → `Anela.Heblo.Application.Features.Catalog.Infrastructure.DataQualityStockOperationQueryAdapter`
- `Anela.Heblo.Application.Features.DataQuality.Contracts.IStockTakingQuery` → `Anela.Heblo.Application.Features.Catalog.Infrastructure.DataQualityStockTakingQueryAdapter`

Both are `internal sealed` adapters in `Catalog/Infrastructure/`, both registered in `CatalogModule.AddCatalogModule()` at lines 61–62 with the comment `// DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.`, both have adapter-level tests under `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`. `docs/architecture/development_guidelines.md` codifies this as the canonical pattern (the `ILeafletKnowledgeSource` write-up: consumer defines the contract, provider implements an adapter, provider registers the DI binding). The proposed `IDqtResilienceService` / `DataQualityResilienceAdapter` pair is a structural clone of these two adapters. There is no interpretation risk here — this is "do it a third time, the same way."

One important fact the spec did not surface: **this exact violation is already tracked by an automated, enforced architecture test.** See Decision 3 below — this changes what "done" means for this fix.

## Proposed Architecture

### Component Overview

```
DataQuality module (consumer)                    Catalog module (provider)
──────────────────────────────                   ──────────────────────────
ProductPairingDqtComparer                          CatalogModule.AddCatalogModule()
  ├─ IEshopStockClient        (unchanged,             registers:
  │   Domain.Catalog.Stock,                           IDqtResilienceService →
  │   out of scope)                                     DataQualityResilienceAdapter
  ├─ IErpStockClient          (unchanged,
  │   Domain.Catalog.Stock,                          DataQualityResilienceAdapter
  │   out of scope)                                    (Catalog.Infrastructure, internal sealed)
  └─ IDqtResilienceService  ──────depends on──────►      └─ ICatalogResilienceService (injected)
      (DataQuality.Contracts,                               └─ CatalogResilienceService (Singleton,
       NEW)                                                      Polly pipeline, unchanged)
```

Call-time flow is unchanged: `ProductPairingDqtComparer.CompareAsync` calls `_resilienceService.ExecuteWithResilienceAsync(...)`, now typed as `IDqtResilienceService`; DI resolves it to `DataQualityResilienceAdapter`, which forwards 1:1 to the existing `ICatalogResilienceService` singleton and its Polly pipeline (3 retries, 50%/3-min-throughput circuit breaker, 30s break, 30s timeout — all untouched in `CatalogResilienceService.cs`).

### Key Design Decisions

#### Decision 1: Option A (consumer-owned contract + provider adapter) over Option B (push resilience into shared clients)

**Options considered:** Brief proposed both. Spec chose A with reasoning I independently verified against the actual adapter code.

**Chosen approach:** Option A.

**Rationale (verified, not just asserted):**
- `IEshopStockClient` is implemented by `ShoptetStockClient` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs`) and `IErpStockClient` by `FlexiStockClient` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs`). I grepped both files for `Resilience`/`Polly`/`CircuitBreaker` — zero matches. Neither adapter currently has any retry/circuit-breaker wrapping.
- Both clients are consumed directly by multiple modules beyond DataQuality: Catalog itself (`CatalogDataRefreshService`, `EshopStockDomainService`) and FinancialOverview (`StockValueService`, confirmed via its `using Anela.Heblo.Domain.Features.Catalog.Stock;`). Option B would silently impose Catalog's resilience policy (tuned for Catalog's interactive cache-refresh workload) onto every one of these consumers — a materially larger, unrequested behavior change that violates the project's "surgical changes" rule.
- Option A is a structural no-op relative to two adapters already in production in this exact module pair (see Architectural Fit above), so it is lower-risk and faster to review than introducing new behavior into shared adapters.

I concur with the spec's choice. Do not revisit this decision during implementation.

#### Decision 2: Registration lifetime — Scoped adapter over a Singleton `CatalogResilienceService`

**Options considered:** Match `IDqtResilienceService` lifetime to the underlying `Singleton` `ICatalogResilienceService`, or `Scoped` to match sibling adapters.

**Chosen approach:** `Scoped`, per spec FR-3.

**Rationale:** Verified `CatalogModule.cs` line 92 registers `ICatalogResilienceService` as `Singleton`, while lines 61–62 register the two DataQuality-facing adapters as `Scoped`. The adapter itself is stateless (pure delegation), so a `Scoped` wrapper around a `Singleton` dependency is safe — ASP.NET Core allows a scoped service to depend on a singleton (the reverse would be the captive-dependency hazard, not this direction). Matching sibling adapter lifetime keeps `CatalogModule.cs` predictable for future readers.

#### Decision 3: The architecture-boundary check already exists — the spec's NFR-3/Open-Questions section is wrong and must be corrected

**Options considered:** N/A — this is a factual correction, not a design choice.

**Finding:** The spec states (NFR-3): *"This is a review-time check, not an automated build-time one (no architecture-test tooling for this is confirmed present in the repo as part of this change; see Open Questions)"* and lists **Open Questions: None**. This is incorrect. `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` is a reflection-based test suite that enforces exactly this rule for many module pairs, including a rule named **`"DataQuality -> Catalog"`** (line 546 of that file), driven by a `DataQualityCatalogAllowlist` set. That allowlist currently contains this exact entry:

```csharp
// ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing,
// wrapped in ICatalogResilienceService for transient-fault protection.
"Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService",
```

alongside four other (deliberately out-of-scope, per the spec) entries for `IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`, `EshopStock`. The allowlist comment block even states the intended long-term fix: *"Track follow-up: introduce DataQuality-owned IProductPairingQuery contract and Catalog-side adapter that surfaces eshop/erp product snapshots without leaking Catalog types."* — i.e., this spec is a partial, deliberately-scoped-down execution of an already-anticipated cleanup, not a novel idea.

**Consequence for implementation:** After FR-1–FR-4 land, the `ICatalogResilienceService` allowlist line becomes a stale/dead entry — the test will keep passing either way (an unused allowlist entry doesn't fail anything), but every other allowlist block in this file carries the explicit convention *"Entries should be removed as the underlying violations are fixed."* Leaving it in place would (a) contradict that convention, (b) misdescribe `ProductPairingDqtComparer`'s actual dependencies to the next reader, and (c) is a one-line diff that costs nothing. This is not optional cleanup — it's completing the fix by the codebase's own stated rule for this file. See Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

No new directories. Matches the existing pattern exactly:

```
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/
  └── IDqtResilienceService.cs                      # NEW — consumer-owned contract

backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/
  ├── DataQualityStockOperationQueryAdapter.cs        # existing precedent (read-only)
  ├── DataQualityStockTakingQueryAdapter.cs           # existing precedent (read-only)
  └── DataQualityResilienceAdapter.cs                 # NEW — provider adapter

backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs  # +1 registration line
backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs  # type swap only

backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs           # mock type swap
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs                             # remove 1 allowlist line (see amendment)
```

### Interfaces and Contracts

Exactly as spec FR-1/FR-2/FR-4 define them — verified the shape matches `ICatalogResilienceService.ExecuteWithResilienceAsync<T>` byte-for-byte, so `ProductPairingDqtComparer`'s call sites require zero body changes:

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

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;
    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService) => _resilienceService = resilienceService;
    public Task<T> ExecuteWithResilienceAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
```

Registration in `CatalogModule.AddCatalogModule()`, grouped with the other two DataQuality-facing adapters (lines 60–62) for discoverability, same comment style:

```csharp
// DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

### Data Flow

No change to runtime data flow or shape. `ProductPairingDqtComparer.CompareAsync` still calls `ExecuteWithResilienceAsync` twice (eshop list, ERP list); only the static type of `_resilienceService` and the DI resolution path change. `operationName` strings (`"ProductPairingDqtComparer.EshopList"`, `"ProductPairingDqtComparer.ErpList"`) are passed through unchanged end-to-end, preserving Polly's operation-key-based logging/circuit-breaker correlation.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale `ModuleBoundariesTests.cs` allowlist entry left in place, contradicting the file's own "remove entries when fixed" convention and misdescribing the comparer's real dependencies | Low (won't break the build, but is dead/misleading documentation) | Remove the `ICatalogResilienceService` line from `DataQualityCatalogAllowlist` as part of this change (see amendment below); keep the other 4 entries (`IEshopStockClient`/`IErpStockClient`/`ErpStock`/`ProductType`/`EshopStock`) since they remain genuinely out of scope |
| `DataQualityResilienceAdapter` accidentally registered with a lifetime that creates a captive-dependency warning | Low | Use `Scoped` (matches sibling adapters); a `Scoped` service depending on a `Singleton` is safe in the depended-on direction — verified `ICatalogResilienceService` is `Singleton` at `CatalogModule.cs:92` |
| Future contributor assumes `DataQualityResilienceAdapter` gives DataQuality its own tunable pipeline and starts changing its internals | Low | Adapter is `internal sealed` one-line delegation with no logic of its own — matches the two existing sibling adapters; any real tuning work is explicitly out of scope per spec |
| Broader `IEshopStockClient`/`IErpStockClient` Domain-layer coupling (5 remaining allowlist entries) is left unresolved, and a future arch-review may re-flag it as if this fix ignored it | Low | Already tracked by the pre-existing allowlist comment in `ModuleBoundariesTests.cs` ("introduce DataQuality-owned IProductPairingQuery contract..."); no action needed now, just don't let this fix's PR description claim the boundary is "fully" clean |

## Specification Amendments

1. **Correct NFR-3 / Open Questions.** The spec's claim that no automated architecture-boundary test exists is false. `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already enforces a `"DataQuality -> Catalog"` rule via reflection (`Consumer_types_should_not_reference_provider_owned_namespaces`), gated by an allowlist (`DataQualityCatalogAllowlist`). Update NFR-3 to reference this test by name instead of denying its existence.

2. **Add FR-5b: Update the architecture test allowlist.** In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, remove this line from `DataQualityCatalogAllowlist` (around line 157):
   ```csharp
   "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService",
   ```
   Leave the other four entries in that set untouched (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`, `EshopStock` — all confirmed out of scope). Also trim the block comment above the set (lines 145–148) so it no longer implies `ICatalogResilienceService` is still a live violation — e.g. drop the "wrapped in ICatalogResilienceService for transient-fault protection" clause from the `EshopStock`/`ErpStock` entries' explanatory comment (lines 151–152), since that no longer describes the remaining entries accurately.
   **Acceptance criterion to add:** `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` passes, and the allowlist no longer contains any entry referencing `Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService`.

3. No change to the spec's chosen approach (Option A), file layout, or interface shape — all verified correct against the codebase.

## Prerequisites

None. No migrations, no config, no infrastructure changes. Both `CatalogModule.AddCatalogModule()` and `DataQualityModule.AddDataQualityModule()` are already registered together in `Anela.Heblo.Application/ApplicationModule.cs` (lines 83 and 113) at every current hosting entry point — verified by grep, no `Program.cs` change needed.
