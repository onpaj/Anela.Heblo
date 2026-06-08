```markdown
# Specification: Remove dead PlaceholderStockValueService and simplify StockValueService DI registration

## Summary
`PlaceholderStockValueService` carries XML documentation claiming it is "automatically injected in Test environments via FinancialOverviewModule," but no such registration exists — the production module unconditionally registers `StockValueService` via an unnecessary manual factory. The placeholder is used only by two test classes that wire it up manually. This spec removes the placeholder from production code, replaces its test usages with mocks, and simplifies the production DI registration to standard typed registration so future constructor changes flow through DI automatically.

## Background
During the daily architecture review on 2026-06-06, `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs` was flagged for misleading documentation. Its XML comment states the class is auto-injected in Test environments via `FinancialOverviewModule`, but `FinancialOverviewModule.AddFinancialOverviewModule` at `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs:19-25` only ever registers the real `StockValueService`. The placeholder is never bound by production DI.

The same DI block uses a manual factory lambda whose comment says "tests can override this," implying the factory exists to support test-time placeholder injection. In practice nothing inside the module does this — the manual factory only adds noise and forces every future `StockValueService` constructor change to be mirrored in the lambda by hand.

The placeholder class is, however, referenced by two test files as a hand-rolled fake (see grep results):
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs:63-72` — a test asserting that a custom `IStockValueService` registration can be substituted into the module.
- `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs:196-200` — a `WebApplicationFactory` override that swaps `IStockValueService` for `PlaceholderStockValueService` so integration tests do not hit ERP.

A naive deletion of `PlaceholderStockValueService.cs` (as the brief suggests) breaks both files. The spec accounts for this by relocating or replacing those usages before deletion.

## Functional Requirements

### FR-1: Simplify production DI registration of `IStockValueService`
Replace the manual factory in `FinancialOverviewModule.AddFinancialOverviewModule` with the standard typed registration so the DI container resolves all `StockValueService` constructor dependencies automatically.

Before (current, `FinancialOverviewModule.cs:18-25`):
```csharp
// Register default implementation - tests can override this
services.AddScoped<IStockValueService>(provider =>
{
    var stockClient = provider.GetRequiredService<IErpStockClient>();
    var priceClient = provider.GetRequiredService<IProductPriceErpClient>();
    var logger = provider.GetRequiredService<ILogger<StockValueService>>();
    return new StockValueService(stockClient, priceClient, logger);
});
```

After:
```csharp
services.AddScoped<IStockValueService, StockValueService>();
```

The "tests can override this" comment must be removed — the override mechanism (removing the descriptor and re-adding) works identically regardless of registration style and does not need a comment in production code.

**Acceptance criteria:**
- `FinancialOverviewModule.cs` registers `IStockValueService` as `services.AddScoped<IStockValueService, StockValueService>();`.
- No manual factory lambda for `IStockValueService` remains in `FinancialOverviewModule.cs`.
- The "tests can override this" comment is removed.
- Resolving `IStockValueService` from a built service provider (with `IErpStockClient`, `IProductPriceErpClient`, and `ILogger<>` registered) returns a `StockValueService` instance — verified by `AddFinancialOverviewModule_RegistersServicesCorrectly` and `AddFinancialOverviewModule_RegistersDefaultRealService`.

### FR-2: Remove `PlaceholderStockValueService` from production code
Delete `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs`. It is unreachable from any production DI path, and its existence inside the application module creates the false impression of an environment-aware test injection mechanism.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs` no longer exists.
- No file under `backend/src/` references the type `PlaceholderStockValueService` (verified by grep).
- `dotnet build` succeeds for the entire solution.

### FR-3: Replace test usages of `PlaceholderStockValueService` with inline mocks
Both test files that currently instantiate `PlaceholderStockValueService` must be migrated to use `Moq` (already a dependency of the test project — see `FinancialOverviewModuleTests.cs:12`) to construct a stub `IStockValueService` returning an empty `IReadOnlyList<MonthlyStockChange>`. This preserves the existing test behavior (predictable empty stock data, no ERP dependency) without keeping a production class alive solely for tests.

#### FR-3a: `FinancialOverviewTests.cs` integration-test factory
At `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs:191-200`, replace the placeholder factory with a Moq-based stub. Suggested shape:

```csharp
var stockValueDescriptor = services.SingleOrDefault(
    d => d.ServiceType == typeof(IStockValueService));
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
```

The `using` for `Anela.Heblo.Application.Features.FinancialOverview.Services` may be removed if nothing else in the file requires it.

#### FR-3b: `FinancialOverviewModuleTests.cs` override-pattern test
At `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs:46-73`, the test `AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting` asserts that the module's `IStockValueService` registration can be replaced by a test override. The placeholder type was only the vehicle; the assertion is about the override pattern itself. Rewrite the test to override with a Moq-backed `IStockValueService` and assert that the resolved service is the mock instance.

Suggested shape:

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

    // Act
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

**Acceptance criteria for FR-3 (a and b combined):**
- No file under `backend/test/` references `PlaceholderStockValueService` (verified by grep).
- `FinancialOverviewTestFactory.ConfigureTestServices` in `FinancialOverviewTests.cs` registers a Moq-backed `IStockValueService` that returns an empty `MonthlyStockChange` list for any date range.
- `FinancialOverviewModuleTests.AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting` (renamed from `_CanOverridePlaceholderService_`) passes and exercises the same override pattern with a mock rather than the deleted placeholder.
- All previously passing tests in `Anela.Heblo.Tests.Features.FinancialOverviewTests` and `Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests` continue to pass.

### FR-4: Update the "factory pattern" test name and intent
The test `AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern` at `FinancialOverviewModuleTests.cs:138-162` asserts (via its name) that the module uses a factory lambda. Once FR-1 lands, the module no longer uses a factory — it uses standard typed registration. The behavior actually exercised by the test body (the module can be registered and `IStockValueService` can be resolved without an exception, with no `BuildServiceProvider`-during-registration antipattern) is still valuable.

Rename the test to `AddFinancialOverviewModule_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern` (or equivalent) and keep its body. The rename is required so the test name does not lie about the production code.

**Acceptance criteria:**
- The test method is renamed to remove the false "UsesFactoryPattern" claim.
- The test body is unchanged in behavior (register module, build provider, resolve `IStockValueService`, assert non-null).
- The renamed test passes.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance change is expected. Standard typed scoped registration has the same per-resolution cost as the existing factory lambda.

### NFR-2: Security
No security impact. The change is internal to DI wiring and test scaffolding. No secrets, auth, or external-facing surface is touched.

### NFR-3: Maintainability
After this change, adding a new constructor dependency to `StockValueService` requires registering the dependency in the appropriate module and nothing else — the DI container resolves it automatically. The current factory lambda silently breaks (compile error) when a new constructor parameter is added but easily falls out of sync if the parameter has a default value or if a developer adds a new overload. Removing the factory eliminates that maintenance trap.

### NFR-4: Test isolation preserved
The integration-test factory in `FinancialOverviewTests.cs` must continue to ensure no real `IErpStockClient` / `IProductPriceErpClient` calls escape during tests. The Moq stub registered in FR-3a fully replaces the registered `IStockValueService`, so the production `StockValueService` (which depends on ERP clients) is never instantiated under the test web factory.

## Data Model
No data-model changes. `MonthlyStockChange` and `IStockValueService` are unchanged.

## API / Interface Design
No public API changes. `IStockValueService` remains the only consumer-facing abstraction. The DI registration style is a purely internal implementation detail.

## Dependencies
- `Moq` (already referenced by `Anela.Heblo.Tests`, see `FinancialOverviewModuleTests.cs:12`).
- `FluentAssertions` (already in use across the affected tests).
- No new package references are required for production or test code.

## Out of Scope
- Refactoring or behavioral changes to `StockValueService` itself.
- Refactoring or behavioral changes to `FinancialAnalysisService`, the background refresh system, or `FinancialAnalysisOptions`.
- Introducing a shared test fake / test-fixture module for `IStockValueService` beyond the inline Moq stubs in the two affected test files. If a future feature needs the same stub in three or more places, a `Anela.Heblo.Tests.Common` helper can be introduced then.
- Changing `MonthlyStockChange` or any other domain type.
- Adding new tests beyond the renamed/updated ones described in FR-3 and FR-4.

## Open Questions
None.

## Status: COMPLETE
```