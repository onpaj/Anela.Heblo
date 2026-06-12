# Externalize Product Mapping Codes for Invoice Import Transformation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the hard-coded Shoptet→ERP product code mapping (`"1287"` → `"SLU000001"`) out of `InvoicesModule.cs`'s DI factory lambda and into a strongly-typed `ProductMappingOptions` class bound from `appsettings.json`, matching the Options pattern already used by `MeetingTasksOptions`, `OrgChartOptions`, etc.

**Architecture:** Net-new POCO `ProductMappingOptions` with `[Required]` data annotations, bound via `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` in a re-signatured `AddInvoicesModule(IServiceCollection, IConfiguration)`. The transformation's existing two-string constructor stays unchanged — its DI factory now resolves `IOptions<ProductMappingOptions>` instead of using literals. The single call site in `ApplicationModule.cs:95` is updated to pass `configuration`. Behavior is identical to today; the only operational change is that the codes can now be tuned via configuration without a rebuild.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Configuration`, `System.ComponentModel.DataAnnotations`, xUnit, `Microsoft.Extensions.DependencyInjection`. No new NuGet packages.

---

## Pre-Flight Context (read before starting)

- **Spec:** Both Shoptet (`"1287"`) and ERP (`"SLU000001"`) codes are business reference data, not secrets — they belong in `appsettings.json`, not Key Vault.
- **Sibling pattern to copy:** `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` (the POCO) and `MeetingTasksModule.cs:14-21` (the binding). Use these as the literal template.
- **Transformation class is UNCHANGED:** `ProductMappingIssuedInvoiceImportTransformation.cs` already accepts the two codes via constructor. Do not touch it; the seam already exists. Existing tests (`ProductMappingIssuedInvoiceImportTransformationTests.cs`) continue to use hard-coded `TEST001`/`NEW001` and must keep passing.
- **Test bootstrap:** `HebloWebApplicationFactory.cs:39` calls `builder.UseEnvironment("Test")`. ASP.NET Core's default host layers `appsettings.Test.json` **on top of** `appsettings.json` — it does not replace it. So once `ProductMapping` lives in the base `appsettings.json`, all `WebApplicationFactory`-based integration tests will inherit it. No change to `appsettings.Test.json` is required.
- **Registration order matters for visual/audit reasons** (not correctness — the three transformations operate on different codes). Preserve today's order: `GiftWithoutVAT` → `RemoveDAtTheEnd` → `ProductMapping`.

---

### Task 1: Create the `ProductMappingOptions` class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs`

This is a plain options POCO with two `[Required]` strings — no behavior to test in isolation. The class is exercised end-to-end by the wiring test in Task 3.

- [ ] **Step 1: Create the options class**

Write to `backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Invoices;

public class ProductMappingOptions
{
    public const string SectionName = "ProductMapping";

    [Required]
    public string ShoptetCode { get; set; } = string.Empty;

    [Required]
    public string ErpCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Verify the project compiles**

Run from repo root:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs
git commit -m "feat: add ProductMappingOptions class for invoice import transformation"
```

---

### Task 2: Write the failing wiring test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs`

This test pins down FR-2 and FR-6: after `AddInvoicesModule(configuration)` is called with a config that contains a `ProductMapping` section, the resolved `IEnumerable<IIssuedInvoiceImportTransformation>` must include a `ProductMappingIssuedInvoiceImportTransformation` that rewrites the configured `ShoptetCode` to the configured `ErpCode`. It will not compile today because `AddInvoicesModule` does not yet take an `IConfiguration` — that's the failure we want.

- [ ] **Step 1: Write the failing wiring test**

Write to `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class InvoicesModuleTests
{
    private const string TestShoptetCode = "TEST-SHOPTET";
    private const string TestErpCode = "TEST-ERP";

    [Fact]
    public async Task AddInvoicesModule_BindsProductMappingOptions_AndTransformationUsesThem()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductMapping:ShoptetCode"] = TestShoptetCode,
                ["ProductMapping:ErpCode"] = TestErpCode,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act — find the product-mapping transformation and run it against an invoice
        // whose only item carries the configured Shoptet code.
        var transformations = provider.GetServices<IIssuedInvoiceImportTransformation>().ToList();
        var productMapping = transformations
            .OfType<Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations.ProductMappingIssuedInvoiceImportTransformation>()
            .Single();

        var invoice = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem { Code = TestShoptetCode, Name = "Test Product" }
            }
        };

        var result = await productMapping.TransformAsync(invoice);

        // Assert
        Assert.Equal(TestErpCode, result.Items.Single().Code);
    }

    [Fact]
    public void AddInvoicesModule_RegistersOptions_BoundFromConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductMapping:ShoptetCode"] = TestShoptetCode,
                ["ProductMapping:ErpCode"] = TestErpCode,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act
        var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;

        // Assert
        Assert.Equal(TestShoptetCode, opts.ShoptetCode);
        Assert.Equal(TestErpCode, opts.ErpCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm a compile-time failure**

Run from repo root:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: compile error — something like `CS1501: No overload for method 'AddInvoicesModule' takes 1 arguments` (because the current signature has no `IConfiguration` parameter and we're trying to pass one). This proves the test will exercise the signature change.

> Do NOT commit yet — the codebase does not build. Proceed to Task 3 to make it build and pass.

---

### Task 3: Refactor `InvoicesModule` to bind options and update the call site

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs:95`

This task makes the failing test from Task 2 pass. It changes `AddInvoicesModule`'s signature to accept `IConfiguration`, binds `ProductMappingOptions` with `ValidateDataAnnotations().ValidateOnStart()`, switches the product-mapping factory to resolve `IOptions<ProductMappingOptions>`, and updates the one call site.

- [ ] **Step 1: Update `InvoicesModule.cs`**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` with:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Invoices;

/// <summary>
/// Module for registering Invoice-related services
/// </summary>
public static class InvoicesModule
{
    public static IServiceCollection AddInvoicesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind ProductMappingOptions from configuration and validate at startup so
        // a missing or incomplete "ProductMapping" section fails fast instead of
        // silently registering a transformation with empty codes.
        services.AddOptions<ProductMappingOptions>()
            .Bind(configuration.GetSection(ProductMappingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register repositories
        services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();

        // Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (PackingMaterials) — keeps the dependency direction inverted properly.
        services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();

        // Cross-module contract: Invoices implements Analytics' IInvoiceImportStatisticsSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (Analytics) — mirrors the IInvoiceConsumptionSource pattern above. Scoped because
        // the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();

        // Cross-module contracts: Invoices implements DataQuality's IInvoiceShoptetSource
        // and IInvoiceErpClient via adapters. Lifetimes mirror the wrapped services exactly:
        //   - IIssuedInvoiceSource is registered Singleton in Program.cs:119, so the adapter
        //     must also be Singleton (and DataQuality consumers must resolve it from a Scoped
        //     scope as usual — Singleton from Scoped is legal, the inverse is captive).
        //   - IIssuedInvoiceClient is registered Scoped in FlexiAdapterServiceCollectionExtensions.cs:93,
        //     so the adapter must also be Scoped.
        services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
        services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();

        // Register services
        services.AddScoped<IInvoiceImportService, InvoiceImportService>();

        // Hangfire jobs are now automatically discovered via IRecurringJob interface

        // Register FlexiBee client (from SDK)
        // Note: IIssuedInvoiceClient registration should be done in Flexi adapter module

        // Register transformations — preserve registration order; the import pipeline
        // enumerates IEnumerable<IIssuedInvoiceImportTransformation> in this order.
        services.AddTransient<IIssuedInvoiceImportTransformation, GiftWithoutVATIssuedInvoiceImportTransformation>();
        services.AddTransient<IIssuedInvoiceImportTransformation, RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation>();
        services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
        {
            var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;
            return new ProductMappingIssuedInvoiceImportTransformation(opts.ShoptetCode, opts.ErpCode);
        });

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}
```

- [ ] **Step 2: Update the call site in `ApplicationModule.cs`**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, change line 95 from:

```csharp
        services.AddInvoicesModule();
```

to:

```csharp
        services.AddInvoicesModule(configuration);
```

- [ ] **Step 3: Build the solution to verify both files compile**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Run the wiring tests from Task 2 and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices.InvoicesModuleTests" \
  --no-build
```
Expected: 2 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs
git commit -m "refactor: bind ProductMappingOptions from configuration in InvoicesModule"
```

---

### Task 4: Add the `ProductMapping` section to `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

The wiring tests in Task 3 already pass because they build their own `IConfiguration`. But the running application boots from `appsettings.json` and, with `ValidateOnStart()` now active, will throw `OptionsValidationException` at startup unless the `ProductMapping` section is present. This task adds the production-equivalent values so behavior is byte-for-byte identical to today (`"1287"` → `"SLU000001"`). Place it adjacent to the existing `InvoiceImport` section (`appsettings.json:299-302`) for discoverability.

- [ ] **Step 1: Add the `ProductMapping` section**

In `backend/src/Anela.Heblo.API/appsettings.json`, find the existing `InvoiceImport` block (around line 299):

```json
  "InvoiceImport": {
    "MinimumDailyThreshold": 10,
    "DefaultDaysBack": 30
  },
```

Insert a new `ProductMapping` block immediately after it:

```json
  "InvoiceImport": {
    "MinimumDailyThreshold": 10,
    "DefaultDaysBack": 30
  },
  "ProductMapping": {
    "ShoptetCode": "1287",
    "ErpCode": "SLU000001"
  },
```

- [ ] **Step 2: Verify the JSON is well-formed**

```bash
python3 -c "import json; json.load(open('backend/src/Anela.Heblo.API/appsettings.json'))" && echo OK
```
Expected: `OK` (and no JSON parsing error). The file contains `//` comments on a few lines — `python3 -c "import json; ..."` will fail on those if present in the section you edited. The `InvoiceImport` block does not contain comments, so inserting clean JSON next to it is safe. If `python3` reports a comment-related error elsewhere in the file (e.g. on existing lines like `appsettings.json:525`), that error is pre-existing and unrelated to your edit — ignore.

- [ ] **Step 3: Build and run the full test project to confirm the WebApplicationFactory-based tests still boot**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices" \
  --no-build
```
Expected: all invoice tests pass, including `InvoiceImportIntegrationTests` (which boots `HebloWebApplicationFactory` and would now fail at host startup if `ProductMapping` were missing).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add ProductMapping section to appsettings.json"
```

---

### Task 5: Add the failing startup-validation test (FR-5)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs`

FR-5 requires that the application fail fast at startup if `ProductMapping` is missing or its required fields are empty. The arch review's FR-6 clarification notes that `.ValidateOnStart()` only fires on `IHost.StartAsync()`, but the simpler equivalent is to build a provider and resolve `IOptions<ProductMappingOptions>.Value` — this triggers the data-annotation validator and throws `OptionsValidationException`. Use that.

- [ ] **Step 1: Append the validation tests to `InvoicesModuleTests.cs`**

Add the following two tests to the existing `InvoicesModuleTests` class (before the closing `}`):

```csharp
    [Fact]
    public void AddInvoicesModule_ThrowsOptionsValidationException_WhenProductMappingSectionMissing()
    {
        // Arrange — empty configuration: no ProductMapping section at all
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act + Assert — resolving .Value fires DataAnnotation validation
        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value);

        Assert.Contains(nameof(ProductMappingOptions.ShoptetCode), string.Join("|", ex.Failures));
        Assert.Contains(nameof(ProductMappingOptions.ErpCode), string.Join("|", ex.Failures));
    }

    [Fact]
    public void AddInvoicesModule_ThrowsOptionsValidationException_WhenShoptetCodeEmpty()
    {
        // Arrange — ProductMapping section present, ShoptetCode is empty string
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductMapping:ShoptetCode"] = "",
                ["ProductMapping:ErpCode"] = "SLU000001",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act + Assert
        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value);

        Assert.Contains(nameof(ProductMappingOptions.ShoptetCode), string.Join("|", ex.Failures));
    }
```

- [ ] **Step 2: Run the validation tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices.InvoicesModuleTests"
```
Expected: 4 passed (the 2 from Task 2 plus the 2 new ones), 0 failed. These pass on the first run because Task 3 already added `.ValidateDataAnnotations()`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs
git commit -m "test: add ProductMappingOptions startup-validation tests"
```

---

### Task 6: Confirm pre-existing tests still pass and no literals remain

**Files:** (read-only verification)
- Verify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`
- Verify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

Belt-and-braces gate before declaring done. FR-4 ("preserve runtime behavior") and FR-6's "existing tests remain unchanged and continue to pass" must be verified end-to-end.

- [ ] **Step 1: Confirm the literals are gone**

```bash
grep -n '"1287"\|"SLU000001"' backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
```
Expected: no output (zero matches). The literals must only live in `appsettings.json` now.

- [ ] **Step 2: Run the pre-existing transformation tests (must still pass — they use TEST001/NEW001)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests"
```
Expected: 6 passed, 0 failed. These tests instantiate the transformation directly via its constructor — they should be unaffected by the wiring refactor.

- [ ] **Step 3: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln
```
Expected: full suite passes. Pay attention to any `OptionsValidationException` failures in `WebApplicationFactory`-based integration tests across other feature modules — those would signal that `appsettings.Test.json` overrides `appsettings.json` for some module in a way that strips the `ProductMapping` section. (It shouldn't — `appsettings.Test.json` is layered, not replaced — but verify.)

- [ ] **Step 4: Format the touched C# files**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs \
            backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs \
            backend/src/Anela.Heblo.Application/ApplicationModule.cs \
            backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs
```
Expected: no errors. Any formatting changes are stage-and-commit material.

- [ ] **Step 5: Commit any formatting changes (skip if working tree is clean)**

```bash
git status --short
# If anything is modified:
git add -A
git commit -m "style: dotnet format invoices module options refactor"
```

---

## Spec Coverage Recap

| FR  | Requirement | Task(s) |
|-----|-------------|---------|
| FR-1 | New `ProductMappingOptions` class with `[Required]` props and `SectionName` constant | Task 1 |
| FR-2 | `InvoicesModule` accepts `IConfiguration`, binds options, factory uses `IOptions<T>`, `ApplicationModule.cs:95` updated | Task 3 |
| FR-3 | `ProductMapping` section added to `appsettings.json` adjacent to `InvoiceImport` | Task 4 |
| FR-4 | Runtime behavior unchanged (`"1287"` → `"SLU000001"`, transformation Transient, registration order preserved) | Task 3 (order comment), Task 4 (production-equivalent values), Task 6 (regression check) |
| FR-5 | Startup validation via `.ValidateDataAnnotations().ValidateOnStart()` | Task 3 (wiring), Task 5 (proof tests) |
| FR-6 | Wiring tests + validation tests; existing direct-class transformation tests untouched | Task 2 (wiring tests), Task 5 (validation tests), Task 6 (existing tests verified) |
| NFR-3 | Brings Invoices module in line with peer-module Options pattern | Task 3 |

## Risk Recap (from arch review)

- **Test settings override risk:** Mitigated by reading `HebloWebApplicationFactory.cs:39` — uses `UseEnvironment("Test")` which layers `appsettings.Test.json` on top of `appsettings.json`, so the `ProductMapping` section added in Task 4 is inherited by all integration tests. No edits to `appsettings.Test.json` needed. Verified in Task 6, Step 3.
- **Registration order silently changing:** Mitigated by preserving the literal order in Task 3 and adding an inline comment.
- **Unknown caller of `AddInvoicesModule()`:** `grep -rn "AddInvoicesModule" backend/` returns only `InvoicesModule.cs` (definition) and `ApplicationModule.cs` (call site). No test fixture calls it directly. Safe to break the no-arg signature.
