# Architecture Review: Move StockValueService to Catalog Module as Cross-Module Adapter

## Skip Design: true

## Architectural Fit Assessment

The violation is real and unambiguous. `StockValueService` at `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs` imports `Anela.Heblo.Domain.Features.Catalog.Price.IProductPriceErpClient` and `Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient` — two Catalog-owned domain contracts — directly into a FinancialOverview-namespace type. This is the exact anti-pattern the cross-module adapter rule exists to prevent.

The contract ownership is already correct: `IStockValueService` lives in `Anela.Heblo.Domain.Features.FinancialOverview`, a FinancialOverview-owned type. Only the implementation placement is wrong.

The fix is a textbook application of the established adapter pattern. Three identical prior examples exist in `Catalog.Infrastructure`:

| Consumer contract (consumer-owned) | Provider adapter (Catalog-owned) | DI registration |
|---|---|---|
| `IManufactureCatalogSource` (Manufacture) | `CatalogManufactureCatalogSourceAdapter` | `CatalogModule` |
| `ILeafletKnowledgeSource` (Leaflet) | `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase) | `KnowledgeBaseModule` |
| `IStockValueService` (FinancialOverview) | `FinancialOverviewStockValueAdapter` ← **to create** | `CatalogModule` ← **to update** |

`ModuleBoundariesTests` already guards nine other module-pair boundaries by the same reflection-based check. Adding the `FinancialOverview → Catalog` rule here closes the loop and pins the fix permanently.

One dependency direction consequence warrants naming: the new adapter inside `Anela.Heblo.Application.Features.Catalog.Infrastructure` will reference `IStockValueService` and `MonthlyStockChange` / `StockChangeByType` from `Anela.Heblo.Domain.Features.FinancialOverview`. This is a **Catalog → FinancialOverview.Domain** reference, which is the _provider implementing a consumer-owned contract_ direction — exactly the accepted pattern for all other adapters in this codebase. It does not create a circular dependency (FinancialOverview.Domain has no reference to Catalog). No new `ModuleBoundaryRule` needs to guard the reverse direction because FinancialOverview.Domain is in the Domain layer, not the Application layer, and the existing `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` test already ensures Domain has no Application-layer back-references.

The `FinancialOverviewModuleTests` class currently asserts `stockValueService.Should().BeOfType<StockValueService>()` in two tests. These tests will need to be updated as a consequence of the move — they are the only observable side-effect outside the four FRs.

## Proposed Architecture

### Component Overview

```
Domain layer
  Anela.Heblo.Domain.Features.FinancialOverview
    IStockValueService           ← contract (unchanged, FinancialOverview-owned)
    MonthlyStockChange           ← return type (unchanged)
    StockChangeByType            ← return type (unchanged)

  Anela.Heblo.Domain.Features.Catalog.Stock
    IErpStockClient              ← Catalog-owned (unchanged)

  Anela.Heblo.Domain.Features.Catalog.Price
    IProductPriceErpClient       ← Catalog-owned (unchanged)

Application layer
  Anela.Heblo.Application.Features.Catalog.Infrastructure
    FinancialOverviewStockValueAdapter   ← NEW (implements IStockValueService)
      deps: IErpStockClient, IProductPriceErpClient, ILogger<>

  Anela.Heblo.Application.Features.Catalog
    CatalogModule                ← ADD one registration line

  Anela.Heblo.Application.Features.FinancialOverview
    FinancialOverviewModule      ← REMOVE StockValueService registration
    [Services/StockValueService.cs]  ← DELETE

Test layer
  Anela.Heblo.Tests.Architecture
    ModuleBoundariesTests        ← ADD "FinancialOverview -> Catalog" rule
  Anela.Heblo.Tests.Application.FinancialOverview
    FinancialOverviewModuleTests ← UPDATE two BeOfType assertions
```

### Key Design Decisions

#### Decision 1: Adapter name — `FinancialOverviewStockValueAdapter`
**Options considered:**
- `StockValueAdapter` (short, generic)
- `FinancialOverviewStockValueAdapter` (consumer-prefixed, matches convention)

**Chosen approach:** `FinancialOverviewStockValueAdapter`

**Rationale:** Every existing cross-module adapter in `Catalog.Infrastructure` names the consumer module in the class name when the contract is consumer-owned and the adapter lives in the provider: `CatalogManufactureCatalogSourceAdapter`, `DataQualityStockOperationQueryAdapter`, `DataQualityStockTakingQueryAdapter`. The naming makes the dependency direction instantly readable from the file list without opening the file. `StockValueAdapter` would be ambiguous and would not survive code review under the existing convention.

#### Decision 2: Visibility — `internal sealed`
**Options considered:**
- `public` (matches current `StockValueService`)
- `internal sealed` (matches all other adapters in `Catalog.Infrastructure`)

**Chosen approach:** `internal sealed`

**Rationale:** All existing adapters in `Catalog.Infrastructure` (`CatalogManufactureCatalogSourceAdapter`, `CatalogPackingProductSourceAdapter`, `LogisticsCatalogSourceAdapter`, etc.) are `internal sealed`. The implementation is an internal detail of the Catalog module; consumers depend on `IStockValueService`. The current `StockValueService` is `public` only because it lived in the FinancialOverview module where the test file referenced it by concrete type. The test assertions must change when the concrete type moves, so there is no reason to keep `public`.

#### Decision 3: Logic fidelity — verbatim copy, no refactoring
**Options considered:**
- Copy body verbatim, changing only namespace/class name/visibility
- Opportunistic cleanup (e.g., the double `Task.WhenAll` in `CalculateMonthlyStockChangeAsync`)

**Chosen approach:** Verbatim copy.

**Rationale:** This is a structural move, not a bug-fix or cleanup PR. Mixing logic changes into a rename makes the diff harder to review and violates the "surgical changes" principle from CLAUDE.md. The double `Task.WhenAll` (lines 100–102 of the original) is a pre-existing oddity; log it in `memory/gotchas/` if desired, but do not touch it here.

#### Decision 4: DI lifetime — `AddScoped`
**Options considered:**
- `AddScoped` (matches current registration)
- `AddTransient` (lighter, service is stateless)

**Chosen approach:** `AddScoped`

**Rationale:** NFR-1 is explicit: lifetime must remain `Scoped`. The service holds no mutable state, so either lifetime would work functionally, but changing the lifetime is a behavior change outside this PR's scope.

## Implementation Guidance

### Directory / Module Structure

Files to **create**:
```
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs
```

Files to **modify**:
```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs
```

Files to **delete**:
```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs
```

### Interfaces and Contracts

No interface changes. The only contract is `IStockValueService` in `Anela.Heblo.Domain.Features.FinancialOverview` — it is unchanged.

The new adapter's class declaration:
```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class FinancialOverviewStockValueAdapter : IStockValueService
```

Required `using` directives in the adapter file:
- `Anela.Heblo.Domain.Features.Catalog.Price` — for `IProductPriceErpClient`
- `Anela.Heblo.Domain.Features.Catalog.Stock` — for `IErpStockClient`
- `Anela.Heblo.Domain.Features.FinancialOverview` — for `IStockValueService`, `MonthlyStockChange`, `StockChangeByType`
- `Microsoft.Extensions.Logging`

The `ILogger<>` type parameter must change from `ILogger<StockValueService>` to `ILogger<FinancialOverviewStockValueAdapter>`.

Registration line to add to `CatalogModule.AddCatalogModule()`:
```csharp
services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();
```
Add the corresponding `using Anela.Heblo.Domain.Features.FinancialOverview;` to `CatalogModule.cs`.

Registration line to remove from `FinancialOverviewModule.AddFinancialOverviewModule()`:
```csharp
services.AddScoped<IStockValueService, StockValueService>();
```
Remove `using Anela.Heblo.Application.Features.FinancialOverview.Services;` if `StockValueService` was its only use (it is — the only other services registered in that file are `FinancialAnalysisService`, `FinancialAnalysisOptions`, and the background refresh task, all of which are in other namespaces or auto-discovered).

`ModuleBoundariesTests` rule to add inside `Rules()`:
```csharp
new ModuleBoundaryRule(
    Name: "FinancialOverview -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

No allowlist entries. Once FR-1 through FR-3 are complete there are zero violations; an empty allowlist proves it.

### Data Flow

Unchanged at runtime. The call path remains:

```
GetFinancialOverviewHandler
  → IFinancialAnalysisService (FinancialAnalysisService)
    → IStockValueService (now resolved to FinancialOverviewStockValueAdapter)
      → IErpStockClient.StockToDateAsync(date, warehouseId, ct)
      → IProductPriceErpClient.GetAllAsync(forceReload, ct)
      ← IReadOnlyList<MonthlyStockChange>
```

DI resolves `IStockValueService` from `CatalogModule`'s registration instead of `FinancialOverviewModule`'s. Both modules are registered in `Program.cs`; the resolution works as long as no duplicate registration exists (there will be none once FR-3 removes the old one).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `FinancialOverviewModuleTests` hardcodes `BeOfType<StockValueService>()` in two test methods — these will fail to compile after deletion | High | Update both assertions to `BeOfType<FinancialOverviewStockValueAdapter>()` and move the mock registrations for `IErpStockClient` / `IProductPriceErpClient` to the `AddCatalogModule` call rather than `AddFinancialOverviewModule`. Alternatively, change the assertions to `BeAssignableTo<IStockValueService>()` if the concrete type is not the intended test invariant. | 
| `ILogger<StockValueService>` log category name changes to `ILogger<FinancialOverviewStockValueAdapter>` | Low | This is a cosmetic change to structured log entries (the `{SourceContext}` field). No monitoring alerts or dashboards are expected to be keyed on the exact logger category name. Document in the commit message. |
| Duplicate `IStockValueService` registration if `CatalogModule` registers it before `FinancialOverviewModule` removes it during a deployment with only partial code applied | Low | Non-issue in practice; the change is applied as a single commit. The last registration wins in .NET DI, so even a partial deploy would resolve to whichever was registered last. |
| `Catalog → FinancialOverview.Domain` reference opens a new direction that could leak Application-layer types in the future | Low | The existing `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` test already prevents Domain←Application references. The Catalog adapter's reference to `IStockValueService` (Domain) and `MonthlyStockChange` (Domain) is correct by layer: Application referencing Domain is always permitted. No new rule is needed. |

## Specification Amendments

The spec is complete and accurate. One item to make explicit:

**`FinancialOverviewModuleTests` must be updated.** The spec's FR-3 acceptance criteria cover `FinancialOverviewModule.cs` and the deleted `.cs` file, but the two test assertions `stockValueService.Should().BeOfType<StockValueService>()` in `FinancialOverviewModuleTests.cs` will produce a compile error after the file is deleted. The implementation task must update these assertions as part of FR-3. The spec should be read to include this under FR-3's acceptance criteria: "All tests in `FinancialOverviewModuleTests` that resolve `IStockValueService` and assert its concrete type are updated to match the new type or changed to assert interface assignability."

## Prerequisites

None. All required types exist:
- `IErpStockClient` — `Anela.Heblo.Domain.Features.Catalog.Stock` (exists)
- `IProductPriceErpClient` — `Anela.Heblo.Domain.Features.Catalog.Price` (exists)
- `IStockValueService` — `Anela.Heblo.Domain.Features.FinancialOverview` (exists)
- `MonthlyStockChange`, `StockChangeByType` — `Anela.Heblo.Domain.Features.FinancialOverview` (exist)
- `Catalog.Infrastructure/` folder — exists with 19 files already present

No migrations, no infrastructure changes, no new NuGet packages.
