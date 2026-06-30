# Review: fix-tests-and-add-arch-guard (r1)

## Verification Results

### 1. `ModuleBoundariesTests.cs` — new arch boundary rule

**Path:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

Rule found at lines 623–632:

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

Checks:
- `InspectedNamespacePrefix`: `Anela.Heblo.Application.Features.FinancialOverview` — PASS
- `ForbiddenNamespacePrefixes` (all three required):
  - `Anela.Heblo.Domain.Features.Catalog` — PRESENT
  - `Anela.Heblo.Application.Features.Catalog` — PRESENT
  - `Anela.Heblo.Persistence.Catalog` — PRESENT
- `Allowlist`: `new HashSet<string>(StringComparer.Ordinal)` — empty, PASS
- Rule is in the `Rules()` `TheoryData` method — PASS (last entry, correctly terminates without trailing comma issue)

### 2. `FinancialOverviewModuleTests.cs` — no direct `IStockValueService` resolution via `AddFinancialOverviewModule()` alone

**Path:** `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs`

Every test that resolves `IStockValueService` from the container first registers the adapter manually:

```csharp
services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();
```

This line appears before `AddFinancialOverviewModule()` in all three tests that build the provider and call `GetRequiredService<IStockValueService>()` (lines 35, 95, 162). The adapter is registered from `Anela.Heblo.Application.Features.Catalog.Infrastructure` — the correct Catalog-owned namespace.

The two tests that do NOT resolve `IStockValueService` (`AddFinancialOverviewModule_UsesRefreshTaskSystem_InsteadOfBackgroundService` and `AddFinancialOverviewModule_RegistersRefreshTasks_ForBackgroundDataRefresh`) omit the adapter pre-registration correctly, as they never call `BuildServiceProvider()` to resolve that service.

The `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting` test does not pre-register the adapter; it registers the module, removes whatever descriptor was added for `IStockValueService`, then adds a mock stub. This tests the override pattern — no direct dependency on `AddFinancialOverviewModule()` resolving the adapter.

No test attempts to resolve `IStockValueService` through `AddFinancialOverviewModule()` alone without prior adapter registration. PASS

### 3. Git commit

```
01af867 feat(feat-3433): add ModuleBoundaries arch guard and fix module tests
```

Commit present, message is consistent with the task. PASS

### 4. Structural consistency with existing rules

The new rule follows the exact same pattern used by all other Catalog-boundary rules in the file (`Purchase -> Catalog`, `Logistics -> Catalog`, `DataQuality -> Catalog`, etc.): three forbidden namespace prefixes covering Domain, Application, and Persistence layers, and an inline empty `HashSet` for the allowlist. PASS

## Summary

All acceptance criteria are met:
- `FinancialOverview -> Catalog` boundary rule exists with empty allowlist and all three forbidden namespace prefixes.
- No test resolves `IStockValueService` directly through `AddFinancialOverviewModule()` alone.
- Developer reports 28/28 `ModuleBoundariesTests` pass (including zero violations for the new rule) and 7/7 `FinancialOverviewModuleTests` pass.
- Commit is present.
