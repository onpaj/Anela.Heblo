# Remove dead PlaceholderStockValueService and simplify StockValueService DI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the dead `PlaceholderStockValueService` from production code, replace its two test usages with inline Moq stubs, and replace the manual factory lambda in `FinancialOverviewModule` with standard typed DI registration.

**Architecture:** Pure backend cleanup inside one Vertical Slice module. No public API, DTO, MediatR, or domain-type change. After this work, `FinancialOverviewModule` registers `IStockValueService` via `services.AddScoped<IStockValueService, StockValueService>()` (matching every other module in the codebase), and both test sites construct their stub via `Mock<IStockValueService>` returning `Array.Empty<MonthlyStockChange>()`.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, Microsoft.Extensions.DependencyInjection.

## Background context for the implementer

You are working on a Clean Architecture monorepo (.NET 8 backend + React frontend). Only the backend is touched.

- `IStockValueService` is the domain abstraction in `Anela.Heblo.Domain.Features.FinancialOverview.IStockValueService`. Its only method is `Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)`.
- The real implementation `StockValueService` lives in `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs` and depends on `IErpStockClient`, `IProductPriceErpClient`, and `ILogger<StockValueService>`.
- `PlaceholderStockValueService` is a dead test fake currently living in production code. Its XML doc claims it is "automatically injected in Test environments via FinancialOverviewModule" — that claim is false; the production module never wires it up. Two test files instantiate it manually.
- The codebase uses xUnit (`[Fact]`), FluentAssertions (`Should().*`), and Moq (`Mock.Of<T>()`, `new Mock<T>()`, `mock.Setup(...)`). All three are already referenced by `Anela.Heblo.Tests`.
- Project completion checklist (from `CLAUDE.md`): `dotnet build` + `dotnet format` must succeed, and all tests touched by the change must pass.

## Task ordering rationale

The task order keeps the build green and all existing tests passing at every checkpoint:

1. Simplify DI registration first — existing tests still pass (factory → typed registration is observable only as "resolves the same `StockValueService` instance with the same dependencies").
2. Rename the misnamed factory test next — pure rename, no behavior change.
3. Replace the `CanOverridePlaceholderService` test with a Moq-based equivalent — `FinancialOverviewModuleTests.cs` stops referencing `PlaceholderStockValueService`.
4. Replace the integration-test factory's placeholder usage with Moq — `FinancialOverviewTests.cs` stops referencing `PlaceholderStockValueService`.
5. Delete `PlaceholderStockValueService.cs` — safe now because no caller remains.
6. Final verification (grep, build, format, full affected-test run).

If a later task fails, you do not have to revert earlier ones — each task leaves the codebase compiling and passing the affected tests.

## File Structure

No new files are created. Net file changes:

- **Modify:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — replace factory lambda with typed registration; remove stale comment.
- **Modify:** `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs` — rename two tests, rewrite override test to use Moq.
- **Modify:** `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs` — rewrite `FinancialOverviewTestFactory.ConfigureTestServices` to register a Moq stub instead of the placeholder.
- **Delete:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs`.

---

### Task 1: Replace factory lambda with typed registration in `FinancialOverviewModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs:18-25`

**Spec reference:** FR-1.

**What changes:** The current factory hand-resolves three constructor dependencies the DI container can resolve itself. Replace it with `services.AddScoped<IStockValueService, StockValueService>()` and remove the misleading "tests can override this" comment.

- [ ] **Step 1: Run existing tests once to capture the baseline**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests|FullyQualifiedName~Anela.Heblo.Tests.Features.FinancialOverviewTests"
```

Expected: all tests pass before any code change. If any test is already failing on this branch, stop and investigate before continuing.

- [ ] **Step 2: Replace the factory block with typed registration**

In `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs`, replace lines 18-25:

```csharp
        // Register default implementation - tests can override this
        services.AddScoped<IStockValueService>(provider =>
        {
            var stockClient = provider.GetRequiredService<IErpStockClient>();
            var priceClient = provider.GetRequiredService<IProductPriceErpClient>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StockValueService>>();
            return new StockValueService(stockClient, priceClient, logger);
        });
```

with:

```csharp
        services.AddScoped<IStockValueService, StockValueService>();
```

Do not change any other line in the file. Do not touch the surrounding `AddMemoryCache()`, `AddScoped<IFinancialAnalysisService, FinancialAnalysisService>()`, `RegisterBackgroundRefreshTasks(services)`, or `services.Configure<FinancialAnalysisOptions>(...)` calls.

- [ ] **Step 3: Remove the now-unused `Microsoft.Extensions.Logging` reference if any**

The file currently does NOT have a top-level `using Microsoft.Extensions.Logging;` — `ILogger<StockValueService>` is qualified inline inside the lambda you just deleted. So after Step 2 there is nothing to remove from the `using` block.

Verify by running:

```bash
grep -n "Microsoft.Extensions.Logging" backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
```

Expected: no output (zero matches). If the grep returns lines, do NOT remove them — they belong to other code in the file. Stop and re-read the file.

- [ ] **Step 4: Build and verify the existing test suite still passes**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests|FullyQualifiedName~Anela.Heblo.Tests.Features.FinancialOverviewTests"
```

Expected: build succeeds, all tests pass. The renamed/rewritten tests do not exist yet — the existing `AddFinancialOverviewModule_RegistersServicesCorrectly`, `AddFinancialOverviewModule_RegistersDefaultRealService`, `AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting`, `AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern`, `AddFinancialOverviewModule_UsesRefreshTaskSystem_InsteadOfBackgroundService`, `AddFinancialOverviewModule_RegistersRefreshTasks_ForBackgroundDataRefresh`, `AddFinancialOverviewModule_RegistersMemoryCache`, and the entire `FinancialOverviewTests` integration suite continue to pass because typed registration produces the same `StockValueService` instance and `PlaceholderStockValueService` is still present.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
git commit -m "refactor: register IStockValueService via standard typed DI in FinancialOverviewModule"
```

---

### Task 2: Rename misleading "UsesFactoryPattern" test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs:138-162`

**Spec reference:** FR-4.

**What changes:** Pure rename. The test body verifies a real invariant (module can be registered and `IStockValueService` resolved without the `BuildServiceProvider`-during-registration antipattern). After Task 1 the module no longer uses a factory, so the name lies. Body stays identical.

- [ ] **Step 1: Rename the test method**

In `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs`, change line 139 from:

```csharp
    public void AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern()
```

to:

```csharp
    public void AddFinancialOverviewModule_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern()
```

Do not change any other line of the method or its body. The inline `// Act & Assert - This test verifies that the factory pattern is used` comment block above the `Record.Exception` call should be replaced to match the new intent. Change:

```csharp
        // Act & Assert - This test verifies that the factory pattern is used
        // The fact that we can successfully register and resolve services without 
        // calling BuildServiceProvider during registration proves the antipattern is avoided
```

to:

```csharp
        // Act & Assert - Registering the module and resolving IStockValueService must not
        // require BuildServiceProvider during registration (antipattern guard).
```

- [ ] **Step 2: Run the renamed test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests.AddFinancialOverviewModule_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern"
```

Expected: 1 test passes.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs
git commit -m "test: rename UsesFactoryPattern test to reflect typed DI registration"
```

---

### Task 3: Rewrite the override test to use a Moq stub instead of `PlaceholderStockValueService`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs:45-73`

**Spec reference:** FR-3b.

**What changes:** Rename `AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting` to `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting`, and rebuild its body around a Moq-backed stub. Same assertion shape (test that override descriptor wins), but no reference to the deleted placeholder type. ERP clients and `ILedgerService` are pre-registered (per arch-review) to keep the test resilient to lifetime changes — after Task 1, the module's typed registration validates `StockValueService` dependencies at resolve time if the override is removed.

- [ ] **Step 1: Replace the test method**

In `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs`, replace lines 45-73 (the entire `AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting` method including the `[Fact]` attribute) with:

```csharp
    [Fact]
    public void AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act - Register module first, then override
        services.AddFinancialOverviewModule(CreateMockConfiguration());

        var stockValueDescriptor = services.SingleOrDefault(
            s => s.ServiceType == typeof(IStockValueService));
        if (stockValueDescriptor != null)
        {
            services.Remove(stockValueDescriptor);
        }

        var stubStockValueService = Mock.Of<IStockValueService>();
        services.AddScoped(_ => stubStockValueService);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var resolved = serviceProvider.GetRequiredService<IStockValueService>();
        resolved.Should().BeSameAs(stubStockValueService);
    }
```

Do not change any other test or any other line in this file. All required `using` directives (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `Moq`, `FluentAssertions`, `Anela.Heblo.Domain.Features.FinancialOverview`, `Anela.Heblo.Domain.Features.Catalog.Price`, `Anela.Heblo.Domain.Features.Catalog.Stock`, `Anela.Heblo.Domain.Accounting.Ledger`, `Anela.Heblo.Application.Features.FinancialOverview`) are already present in the file.

Important: do NOT remove `using Anela.Heblo.Application.Features.FinancialOverview.Services;` from this file. Tests at lines 41 and 94 still assert `.BeOfType<StockValueService>()`, and `StockValueService` lives in that namespace.

- [ ] **Step 2: Run the rewritten test plus the other tests in the same file**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests"
```

Expected: all seven `FinancialOverviewModuleTests` tests pass, including the new `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs
git commit -m "test: replace PlaceholderStockValueService override with Moq stub"
```

---

### Task 4: Replace placeholder usage in `FinancialOverviewTestFactory` with a Moq stub

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs:1-205`

**Spec reference:** FR-3a, NFR-4.

**What changes:** Rewrite the `IStockValueService` override block inside `FinancialOverviewTestFactory.ConfigureTestServices` to register a `Mock<IStockValueService>` returning `Array.Empty<MonthlyStockChange>()`. This preserves the existing test behavior (deterministic empty stock data, no ERP dependency) without keeping a production class alive solely for tests. Also remove the now-unused `using Anela.Heblo.Application.Features.FinancialOverview.Services;` and a stale comment.

- [ ] **Step 1: Update the `using` directives**

In `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs`, change lines 1-12 from:

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
```

to:

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
```

The two differences: `using Anela.Heblo.Application.Features.FinancialOverview.Services;` is removed (no longer needed once `PlaceholderStockValueService` is gone), and `using Anela.Heblo.Domain.Features.FinancialOverview;` is added (so `IStockValueService` and `MonthlyStockChange` resolve unqualified).

- [ ] **Step 2: Rewrite `FinancialOverviewTestFactory.ConfigureTestServices`**

In the same file, replace the entire `ConfigureTestServices` method body (lines 180-204). Change:

```csharp
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Override ILedgerService with mock
        var ledgerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILedgerService));
        if (ledgerDescriptor != null)
        {
            services.Remove(ledgerDescriptor);
        }
        services.AddSingleton(MockLedgerService.Object);

        // Override IStockValueService with placeholder for testing
        var stockValueDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(Anela.Heblo.Domain.Features.FinancialOverview.IStockValueService));
        if (stockValueDescriptor != null)
        {
            services.Remove(stockValueDescriptor);
        }
        services.AddScoped<Anela.Heblo.Domain.Features.FinancialOverview.IStockValueService>(provider =>
        {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PlaceholderStockValueService>>();
            return new PlaceholderStockValueService(logger);
        });

        // Background services are now handled by centralized refresh system
        // No specific background service removal needed for testing
    }
```

to:

```csharp
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Override ILedgerService with mock
        var ledgerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILedgerService));
        if (ledgerDescriptor != null)
        {
            services.Remove(ledgerDescriptor);
        }
        services.AddSingleton(MockLedgerService.Object);

        // Override IStockValueService with a stub returning no stock changes
        var stockValueDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStockValueService));
        if (stockValueDescriptor != null)
        {
            services.Remove(stockValueDescriptor);
        }

        var stockValueMock = new Mock<IStockValueService>();
        stockValueMock
            .Setup(s => s.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MonthlyStockChange>());
        services.AddSingleton(stockValueMock.Object);
    }
```

Notes for the implementer:
- `IStockValueService` and `MonthlyStockChange` are now unqualified because of the new `using Anela.Heblo.Domain.Features.FinancialOverview;` added in Step 1.
- The mock is registered as `AddSingleton` (not `AddScoped`) so the same stub instance is returned to every scope — matches the spec example. Lifetime difference is irrelevant because the stub holds no per-request state.
- `Array.Empty<MonthlyStockChange>()` is preferred over `new List<MonthlyStockChange>()` — no per-call heap allocation and it satisfies the `IReadOnlyList<MonthlyStockChange>` return type via covariance.

- [ ] **Step 3: Update the stale comment in `GetFinancialOverview_WithIncludeStockDataTrue_ReturnsFinancialAndStockData`**

Lines 158-164 contain a comment referencing the placeholder. Change:

```csharp
        // Verify stock data is included (even if empty with placeholder service)
        if (content.Data.Any())
        {
            var monthData = content.Data.First();
            // With placeholder service, stock changes will be null but properties should exist
            monthData.TotalStockValueChange.Should().NotBeNull();
            monthData.TotalBalance.Should().NotBeNull();
        }
```

to:

```csharp
        // Verify stock data is included (empty stub, but properties populated)
        if (content.Data.Any())
        {
            var monthData = content.Data.First();
            monthData.TotalStockValueChange.Should().NotBeNull();
            monthData.TotalBalance.Should().NotBeNull();
        }
```

Do not change the assertions themselves — only the two comment lines.

- [ ] **Step 4: Build and run the affected integration tests**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FinancialOverviewTests"
```

Expected: build succeeds, all `FinancialOverviewTests` tests pass. The mock returns the same empty list the placeholder did, so `GetFinancialOverview_WithIncludeStockDataTrue_ReturnsFinancialAndStockData` continues to observe non-null `TotalStockValueChange` and `TotalBalance` (the application-layer aggregation code populates them from any returned list, including an empty one).

If `GetFinancialOverview_WithIncludeStockDataTrue_ReturnsFinancialAndStockData` fails, the application code probably treats null and empty differently. Re-read `FinancialOverviewService` (or whichever handler aggregates these results) to confirm — do NOT change the production code as a fix; instead adjust the mock to return whatever shape the placeholder used to produce. The placeholder returned `new List<MonthlyStockChange>()` wrapped as `IReadOnlyList<MonthlyStockChange>`, which is shape-equivalent to `Array.Empty<MonthlyStockChange>()`.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs
git commit -m "test: replace PlaceholderStockValueService in integration factory with Moq stub"
```

---

### Task 5: Delete `PlaceholderStockValueService.cs`

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs`

**Spec reference:** FR-2.

**What changes:** The class is now unreferenced. Delete the file.

- [ ] **Step 1: Verify nothing references the type before deleting**

```bash
grep -rn "PlaceholderStockValueService" backend/
```

Expected: exactly one match — the file itself (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs:...`). If the grep returns matches in any other file, STOP. Re-do Tasks 3 and 4 before proceeding — deleting the file with live references will break the build.

- [ ] **Step 2: Delete the file**

```bash
git rm backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs
```

- [ ] **Step 3: Verify the deletion**

```bash
ls backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs 2>&1
```

Expected: `ls: ... No such file or directory`.

- [ ] **Step 4: Build the solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with zero errors. Warnings unrelated to this change can be ignored.

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor: delete dead PlaceholderStockValueService"
```

---

### Task 6: Final verification

**Files:** none — verification only.

- [ ] **Step 1: Confirm zero remaining references to `PlaceholderStockValueService` in the repo**

```bash
grep -rn "PlaceholderStockValueService" backend/
```

Expected: zero matches.

- [ ] **Step 2: Confirm `FinancialOverviewModule.cs` registers `IStockValueService` with typed registration and no factory lambda**

```bash
grep -n "IStockValueService" backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
```

Expected: exactly one line matching the pattern `services.AddScoped<IStockValueService, StockValueService>();`. No lambda or `provider =>` arrow visible on adjacent lines.

- [ ] **Step 3: Confirm the "tests can override this" comment is gone**

```bash
grep -n "tests can override" backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
```

Expected: zero matches.

- [ ] **Step 4: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: completes with no errors. Any whitespace adjustments produced by the formatter on edited files are acceptable — commit them if any appear (`git status` should reveal modified files only if formatting changed something).

If `dotnet format` modifies files:

```bash
git add -A
git commit -m "chore: apply dotnet format"
```

- [ ] **Step 5: Run the full affected test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests|FullyQualifiedName~Anela.Heblo.Tests.Features.FinancialOverviewTests"
```

Expected: all `FinancialOverviewModuleTests` (7 tests including the renamed `_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern` and the rewritten `_CanOverrideStockValueService_ForTesting`) and all `FinancialOverviewTests` integration tests pass.

- [ ] **Step 6: Run the full backend test suite as a regression guard**

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: zero new failures attributable to this change. Pre-existing failures unrelated to the FinancialOverview slice (e.g., flaky integration tests against external systems) are out of scope — note them in the final report but do not fix them as part of this work.

- [ ] **Step 7: Final report**

Confirm to the operator:
- All `PlaceholderStockValueService` references are gone (production and tests).
- `FinancialOverviewModule` uses standard typed DI registration.
- `dotnet build` clean; `dotnet format` clean; affected tests green; full suite has no new failures.
- Commits on the branch (run `git log --oneline main..HEAD`):
  1. `refactor: register IStockValueService via standard typed DI in FinancialOverviewModule`
  2. `test: rename UsesFactoryPattern test to reflect typed DI registration`
  3. `test: replace PlaceholderStockValueService override with Moq stub`
  4. `test: replace PlaceholderStockValueService in integration factory with Moq stub`
  5. `refactor: delete dead PlaceholderStockValueService`
  6. (optional) `chore: apply dotnet format`

---

## Spec coverage map

| Spec requirement | Task(s) | Verification |
|---|---|---|
| FR-1: Simplify production DI registration | Task 1 | Task 6 Step 2 grep + `RegistersServicesCorrectly` test |
| FR-2: Remove `PlaceholderStockValueService` | Task 5 | Task 6 Step 1 grep |
| FR-3a: Replace placeholder in `FinancialOverviewTests.cs` | Task 4 | Task 4 Step 4 test run |
| FR-3b: Replace placeholder in `FinancialOverviewModuleTests.cs` override test | Task 3 | Task 3 Step 2 test run |
| FR-4: Rename misleading factory-pattern test | Task 2 | Task 2 Step 2 test run |
| NFR-1: Performance | Task 1 (no per-resolution cost change with typed registration) | n/a |
| NFR-2: Security | No security surface touched | n/a |
| NFR-3: Maintainability | Task 1 eliminates the manual-factory drift trap | n/a |
| NFR-4: Test isolation preserved | Task 4 (Moq fully replaces real `IStockValueService`; ERP clients never resolved) | Task 4 Step 4 + Task 6 Step 5 |
