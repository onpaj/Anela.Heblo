# Relocate ProductExportDownloadJob from FileStorage to Catalog — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ProductExportDownloadJob` and its product-export-specific options out of `Features/FileStorage` (generic blob transport) and into `Features/Catalog` (product-export domain owner), restoring SRP for FileStorage without changing runtime behavior.

**Architecture:** Split the current `ProductExportOptions` into two: a Catalog-owned `ProductExportOptions` carrying only domain config (`Url`, `ContainerName`) and a new FileStorage-owned `FileDownloadOptions` carrying generic download tuning (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`). Rename FileStorage's named HttpClient constant from product-export terminology to neutral terminology. The job, the slimmed options class, and the DI registration of those options all move to the Catalog module; `IRecurringJob` auto-discovery (assembly scan in `ServiceCollectionExtensions.AddRecurringJobs`) picks the job up in its new location with no explicit registration needed.

**Tech Stack:** .NET 8, MediatR, Hangfire (PostgreSQL storage), Polly v8 (resilience pipeline), `IOptions<T>` pattern, xUnit + FluentAssertions + Moq.

---

## File Structure (after this plan)

**New files**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileDownloadOptions.cs` — generic download-tuning options (timeouts, retry).
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs` — domain options (`Url`, `ContainerName`) for the Shoptet product-export job.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` — moved verbatim from FileStorage (namespace + using directives only).
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/FileDownloadOptionsTests.cs` — replaces the obsolete `ProductExportOptionsTests` (defaults + config binding for the four timeout/retry properties).
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` — moved verbatim from FileStorage tests (namespace + using directives only).

**Modified files**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — rename `ProductExportDownloadClientName` → `FileDownloadClientName`, add `Configure<FileDownloadOptions>(…)`.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — inject `IOptions<FileDownloadOptions>` and use new constant name.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` — use new constant name only.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — inject `IOptions<FileDownloadOptions>`; update validation error message.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — bind `ProductExportOptions` from `configuration.GetSection("ProductExportOptions")`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — remove the `Configure<ProductExportOptions>(…)` line and the now-unused `using Anela.Heblo.Application.Features.FileStorage;` import.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — switch options Configure to `FileDownloadOptions`, update constant name + value.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — update constant name in CreateClient setups.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — switch field type to `IOptions<FileDownloadOptions>`, update constant name in factory setup.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs` — switch `CreateOptions` to return `IOptions<FileDownloadOptions>`.

**Deleted files**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` (relocated)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` (relocated)
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs` (content split between FileDownloadOptionsTests and the relocated Catalog job tests; no remaining assertions for the slimmed ProductExportOptions are warranted).
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` (relocated)

**Configuration**
- `backend/src/Anela.Heblo.API/appsettings.json` — `"ProductExportOptions"` section unchanged (already neutral, no rename needed). No new section required for `FileDownloadOptions` because the class-level defaults (10 s HEAD, 120 s download, 3 retries, 2 s base delay) match the values currently in code; if env-specific overrides are ever needed, callers may add `"FileStorage": { "Download": { ... } }`.

---

## Conventions and Invariants (read once before starting)

- **Catalog → FileStorage direction only.** Catalog depends on `IBlobStorageService` and MediatR contracts owned by FileStorage. FileStorage MUST NOT reference any `Catalog` namespace after this plan.
- **Hangfire job identity.** `ProductExportDownloadJob.Metadata.JobName = "product-export-download"` is the Hangfire recurring-job key; do **not** change it. The `ILogger<T>` category name *will* change with the namespace move (`...FileStorage.Infrastructure.Jobs.ProductExportDownloadJob` → `...Catalog.Infrastructure.Jobs.ProductExportDownloadJob`); this is accepted per the spec.
- **Auto-discovery.** `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` line 373–392 (`AddRecurringJobs`) scans the Application assembly for `IRecurringJob` implementations and registers them. No explicit job registration exists or needs to exist — the job is picked up by type, not location.
- **Configuration section unchanged.** `"ProductExportOptions"` already lives at the root of `appsettings.json` (line 158), not under `"FileStorage"`. No appsettings rename, no Key Vault migration.
- **DTO rule.** Both `ProductExportOptions` and `FileDownloadOptions` are options classes consumed by `IOptions<T>` binding; they remain `class` types with mutable `public set;` properties (the binder requires writable setters). They are not crossed by the OpenAPI client generator, so the project-specific "DTOs must be classes" rule does not bite here either way.
- **Commit cadence.** Commit at the end of every task. Each task is shaped so the solution builds and all tests pass at task end. Conventional commit prefix: `refactor:` for the moves, `test:` for test-only changes inside a single task.
- **Working directory.** All paths below are absolute under the worktree root `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-filestorage-productexportdow/`. Bash commands assume this is the current directory.

---

### Task 1: Create `FileDownloadOptions` in FileStorage

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileDownloadOptions.cs`

- [ ] **Step 1: Create the new options class**

Create the file with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.FileStorage;

/// <summary>
/// Generic configuration for the FileStorage download pipeline (HEAD probe, retry, timeout).
/// Domain-specific download targets (URL, container) live in their owning module.
/// </summary>
public class FileDownloadOptions
{
    /// <summary>
    /// Timeout for the HTTP HEAD probe used to verify file availability before download.
    /// </summary>
    public TimeSpan HeadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for the full file download.
    /// </summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Maximum number of retry attempts for transient HTTP failures.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay for the exponential back-off retry policy.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
```

- [ ] **Step 2: Verify the solution still builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero new warnings or errors. The new class is unused for now; that is intentional.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/FileDownloadOptions.cs
git commit -m "refactor: introduce FileDownloadOptions in FileStorage"
```

---

### Task 2: Register `FileDownloadOptions` in `FileStorageModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`

- [ ] **Step 1: Add the options binding inside `AddFileStorageModule`**

In `FileStorageModule.cs`, locate the `AddFileStorageModule` method. Immediately before the final `return services;` (currently around line 58), insert:

```csharp
        // Bind generic download tuning options. Defaults are correct production values;
        // callers may override under "FileStorage:Download" if env-specific tuning is needed.
        services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));
```

After the change, the closing region of the method should read:

```csharp
        // Register blob storage service as Singleton so the _containerExists cache survives across requests.
        // BlobServiceClient is already Singleton — no thread-safety concerns.
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        // Bind generic download tuning options. Defaults are correct production values;
        // callers may override under "FileStorage:Download" if env-specific tuning is needed.
        services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));

        return services;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs
git commit -m "refactor: register FileDownloadOptions in FileStorageModule"
```

---

### Task 3: Migrate `DownloadFromUrlHandler` to use `FileDownloadOptions`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

- [ ] **Step 1: Update the handler's field type**

In `DownloadFromUrlHandler.cs` line 21, change:

```csharp
    private readonly IOptions<ProductExportOptions> _options;
```

to:

```csharp
    private readonly IOptions<FileDownloadOptions> _options;
```

- [ ] **Step 2: Update the handler's constructor parameter**

In `DownloadFromUrlHandler.cs` line 28, change:

```csharp
        IOptions<ProductExportOptions> options,
```

to:

```csharp
        IOptions<FileDownloadOptions> options,
```

No other code in the handler changes — `_options.Value.HeadTimeout` at line 141 references a property name that exists identically on the new type.

- [ ] **Step 3: Update the handler test field type**

In `DownloadFromUrlHandlerTests.cs` line 29, change:

```csharp
    private readonly IOptions<ProductExportOptions> _options;
```

to:

```csharp
    private readonly IOptions<FileDownloadOptions> _options;
```

- [ ] **Step 4: Update the constructor initialization in the test**

In `DownloadFromUrlHandlerTests.cs` lines 36–39, change:

```csharp
        _options = Options.Create(new ProductExportOptions
        {
            HeadTimeout = TimeSpan.FromSeconds(5),
        });
```

to:

```csharp
        _options = Options.Create(new FileDownloadOptions
        {
            HeadTimeout = TimeSpan.FromSeconds(5),
        });
```

- [ ] **Step 5: Run the handler tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DownloadFromUrlHandlerTests`
Expected: all DownloadFromUrlHandler tests pass.

- [ ] **Step 6: Full build to check for collateral damage**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds. (Other consumers of `ProductExportOptions` — DownloadResilienceService, the job, the module test — still compile because `ProductExportOptions` still has all six properties; they are migrated in later tasks.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
git commit -m "refactor: switch DownloadFromUrlHandler to FileDownloadOptions"
```

---

### Task 4: Migrate `DownloadResilienceService` to use `FileDownloadOptions`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`

- [ ] **Step 1: Update the service field type**

In `DownloadResilienceService.cs` line 17, change:

```csharp
    private readonly ProductExportOptions _options;
```

to:

```csharp
    private readonly FileDownloadOptions _options;
```

- [ ] **Step 2: Update the constructor parameter type**

In `DownloadResilienceService.cs` line 20, change:

```csharp
        IOptions<ProductExportOptions> options,
```

to:

```csharp
        IOptions<FileDownloadOptions> options,
```

- [ ] **Step 3: Update the validation error message to reference the new type name**

In `DownloadResilienceService.cs` line 32, change:

```csharp
                $"ProductExportOptions: MaxRetryAttempts ({_options.MaxRetryAttempts}) * DownloadTimeout ({_options.DownloadTimeout}) " +
```

to:

```csharp
                $"FileDownloadOptions: MaxRetryAttempts ({_options.MaxRetryAttempts}) * DownloadTimeout ({_options.DownloadTimeout}) " +
```

- [ ] **Step 4: Update the resilience service test helper return type**

In `DownloadResilienceServiceTests.cs` line 16, change:

```csharp
    private static IOptions<ProductExportOptions> CreateOptions(
```

to:

```csharp
    private static IOptions<FileDownloadOptions> CreateOptions(
```

- [ ] **Step 5: Update the resilience service test helper body type**

In `DownloadResilienceServiceTests.cs` line 21, change:

```csharp
        var options = new ProductExportOptions
```

to:

```csharp
        var options = new FileDownloadOptions
```

- [ ] **Step 6: Update the CreateService helper parameter type**

In `DownloadResilienceServiceTests.cs` line 33, change:

```csharp
        IOptions<ProductExportOptions> options,
```

to:

```csharp
        IOptions<FileDownloadOptions> options,
```

- [ ] **Step 7: Run the resilience tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DownloadResilienceServiceTests`
Expected: all DownloadResilienceService tests pass.

- [ ] **Step 8: Full build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs
git commit -m "refactor: switch DownloadResilienceService to FileDownloadOptions"
```

---

### Task 5: Replace `ProductExportOptionsTests` with `FileDownloadOptionsTests`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/FileDownloadOptionsTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`

The current `ProductExportOptionsTests.cs` only asserts defaults and config binding for the four properties (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`) that have moved to `FileDownloadOptions`. The slimmed `ProductExportOptions` will carry no defaults worth asserting, so no replacement test for it is added here.

- [ ] **Step 1: Create the new test file**

Create the file with this exact content:

```csharp
using Anela.Heblo.Application.Features.FileStorage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Tests.Features.FileStorage.Configuration;

public sealed class FileDownloadOptionsTests
{
    [Fact]
    public void Defaults_HeadTimeout_Is10Seconds()
    {
        // Arrange / Act
        var options = new FileDownloadOptions();

        // Assert
        options.HeadTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Defaults_DownloadTimeout_Is120Seconds()
    {
        // Arrange / Act
        var options = new FileDownloadOptions();

        // Assert
        options.DownloadTimeout.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Defaults_MaxRetryAttempts_Is3()
    {
        // Arrange / Act
        var options = new FileDownloadOptions();

        // Assert
        options.MaxRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void Defaults_RetryBaseDelay_Is2Seconds()
    {
        // Arrange / Act
        var options = new FileDownloadOptions();

        // Assert
        options.RetryBaseDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Configuration_BindsTimeSpanFromString()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "HeadTimeout", "00:00:30" },
            })
            .Build();

        // Act
        var options = configuration.Get<FileDownloadOptions>()!;

        // Assert
        options.HeadTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Configuration_BindsMaxRetryAttempts_FromInteger()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "MaxRetryAttempts", "5" },
            })
            .Build();

        // Act
        var options = configuration.Get<FileDownloadOptions>()!;

        // Assert
        options.MaxRetryAttempts.Should().Be(5);
    }
}
```

- [ ] **Step 2: Delete the obsolete test file**

```bash
git rm backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs
```

- [ ] **Step 3: Run the new test class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~FileDownloadOptionsTests`
Expected: 6 tests pass.

- [ ] **Step 4: Full build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds. (`ProductExportOptions` still has the timeout/retry properties — they are no longer referenced by handler/resilience code but compile fine; they are removed in the next task.)

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/FileDownloadOptionsTests.cs
git commit -m "test: replace ProductExportOptionsTests with FileDownloadOptionsTests"
```

---

### Task 6: Trim `ProductExportOptions` to `Url` + `ContainerName` only

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`

- [ ] **Step 1: Replace the class body**

Overwrite `ProductExportOptions.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.FileStorage;

/// <summary>
/// Configuration options for the Shoptet product-export download job.
/// Lives in the FileStorage namespace temporarily; relocated to Catalog in a later task.
/// </summary>
public class ProductExportOptions
{
    /// <summary>
    /// The URL from which product export files will be downloaded.
    /// </summary>
    public string Url { get; set; } = null!;

    /// <summary>
    /// Target blob container for the exported CSV.
    /// </summary>
    public string ContainerName { get; set; } = null!;
}
```

Note: `ContainerName` becomes non-nullable to match the property contract (the job reads it without null-checking). The current file leaves it un-initialized; adding `= null!` matches the `Url` style and silences nullable-reference warnings without changing runtime behavior.

- [ ] **Step 2: Build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds. The only consumers of the trimmed type at this point are:
- `ProductExportDownloadJob` — reads `Url` and `ContainerName` (still on the class).
- `ServiceCollectionExtensions.cs` line 365 — binds the options from configuration (binder only uses the properties that exist; absent timeout/retry keys are ignored).
- `FileStorageModuleTests.cs` lines 21–26 — calls `services.Configure<ProductExportOptions>(opts => { opts.MaxRetryAttempts = …; … })`; this will now fail to compile.

If `FileStorageModuleTests.cs` does not compile, that is expected and is fixed in Task 7.

- [ ] **Step 3: Don't commit yet — Task 7 finishes this slice**

Hold the working tree as-is and proceed to Task 7. (If you must commit for any reason, use `git commit --allow-empty-message -m "wip"` and rebase later; otherwise carry the broken build into Task 7 which restores green.)

---

### Task 7: Repair `FileStorageModuleTests` after the options split

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`

The test fixture currently pre-configures the four timeout/retry properties on `ProductExportOptions` so `DownloadResilienceService` can construct without throwing. Those properties now live on `FileDownloadOptions`.

- [ ] **Step 1: Switch the base-services Configure block to `FileDownloadOptions`**

In `FileStorageModuleTests.cs` lines 21–26, change:

```csharp
        services.Configure<ProductExportOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });
```

to:

```csharp
        services.Configure<FileDownloadOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 3: Run the file-storage module tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~FileStorageModuleTests`
Expected: all 6 tests pass.

- [ ] **Step 4: Run the full backend test suite to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 5: Commit (covers Task 6 + Task 7 together)**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs
git commit -m "refactor: trim ProductExportOptions to domain properties only"
```

---

### Task 8: Rename `ProductExportDownloadClientName` → `FileDownloadClientName`

The named HttpClient that FileStorage registers is generic — it configures `SocketsHttpHandler` + `PooledConnectionLifetime` + `AutomaticDecompression` and is used by `DownloadFromUrlHandler` (HEAD probe) and `AzureBlobStorageService` (download). Its name should match its purpose.

**Files (all reference the constant):**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

- [ ] **Step 1: Rename the constant in `FileStorageModule.cs`**

Line 13 currently reads:

```csharp
    public const string ProductExportDownloadClientName = "ProductExportDownload";
```

Change to:

```csharp
    public const string FileDownloadClientName = "FileDownload";
```

Then line 35 currently reads:

```csharp
        services.AddHttpClient(ProductExportDownloadClientName)
```

Change to:

```csharp
        services.AddHttpClient(FileDownloadClientName)
```

- [ ] **Step 2: Update `DownloadFromUrlHandler.cs`**

Line 95:

```csharp
                FileStorageModule.ProductExportDownloadClientName,
```

→

```csharp
                FileStorageModule.FileDownloadClientName,
```

Line 144:

```csharp
            var client = _httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName);
```

→

```csharp
            var client = _httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName);
```

- [ ] **Step 3: Update `AzureBlobStorageService.cs`**

Line 38:

```csharp
            var httpClient = _httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName);
```

→

```csharp
            var httpClient = _httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName);
```

- [ ] **Step 4: Update `FileStorageModuleTests.cs`**

Line 73:

```csharp
        var client = factory.CreateClient(FileStorageModule.ProductExportDownloadClientName);
```

→

```csharp
        var client = factory.CreateClient(FileStorageModule.FileDownloadClientName);
```

Also rename the surrounding test method on line 64 from `AddFileStorageModule_RegistersNamedHttpClient_ProductExportDownload` to `AddFileStorageModule_RegistersNamedHttpClient_FileDownload` so the test name reflects what it tests.

Line 120:

```csharp
        Assert.Equal("ProductExportDownload", FileStorageModule.ProductExportDownloadClientName);
```

→

```csharp
        Assert.Equal("FileDownload", FileStorageModule.FileDownloadClientName);
```

- [ ] **Step 5: Update `AzureBlobStorageServiceTests.cs`**

Three call sites use `FileStorageModule.ProductExportDownloadClientName` (lines 30, 73, 85, 184, 424 — search and replace all occurrences). Run a verification grep before editing:

```bash
grep -n "ProductExportDownloadClientName" backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs
```

Then replace every `FileStorageModule.ProductExportDownloadClientName` with `FileStorageModule.FileDownloadClientName` in this file.

- [ ] **Step 6: Update `DownloadFromUrlHandlerTests.cs`**

Line 73:

```csharp
            .Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
```

→

```csharp
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
```

Line 282:

```csharp
                FileStorageModule.ProductExportDownloadClientName,
```

→

```csharp
                FileStorageModule.FileDownloadClientName,
```

- [ ] **Step 7: Verify nothing else references the old constant**

```bash
grep -rn "ProductExportDownloadClientName\|\"ProductExportDownload\"" backend/src backend/test
```

Expected: no results.

- [ ] **Step 8: Build + test**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage backend/test/Anela.Heblo.Tests/Features/FileStorage
git commit -m "refactor: rename ProductExportDownloadClientName to FileDownloadClientName"
```

---

### Task 9: Relocate `ProductExportOptions` to the Catalog namespace

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` (add `using` for new namespace — the file is moved in Task 11; here we only update the import so the build stays green while the job is still in FileStorage)
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (replace the old import with the new namespace)

- [ ] **Step 1: Create the relocated options class**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Configuration options for the Shoptet product-export download job.
/// </summary>
public class ProductExportOptions
{
    /// <summary>
    /// The URL from which product export files will be downloaded.
    /// </summary>
    public string Url { get; set; } = null!;

    /// <summary>
    /// Target blob container for the exported CSV.
    /// </summary>
    public string ContainerName { get; set; } = null!;
}
```

- [ ] **Step 2: Delete the FileStorage copy**

```bash
git rm backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs
```

- [ ] **Step 3: Update the job's `using` directive**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`, remove the implicit reference to the old namespace by adding an explicit `using` for the new one. The job currently resolves `ProductExportOptions` because it shares the namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs` (which sees `Anela.Heblo.Application.Features.FileStorage.ProductExportOptions` via the outer namespace). After Step 2 that resolution is gone.

At the top of the file, add this `using` (alongside the existing imports — keep them sorted):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
```

The job's class body is unchanged — `ProductExportOptions` now resolves to the Catalog namespace.

- [ ] **Step 4: Update `ServiceCollectionExtensions.cs`**

Line 28 currently reads:

```csharp
using Anela.Heblo.Application.Features.FileStorage;
```

This import is used only for `ProductExportOptions` at line 365. Replace it with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
```

Verify the import is not needed for anything else in the file:

```bash
grep -n "FileStorage" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected: only the (now-changed) `using` line and unrelated `Anela.Heblo.Adapters.FileSystem` should appear. If any other `FileStorage` symbol is referenced, leave the original `using` in place and add the new one alongside it instead of replacing.

- [ ] **Step 5: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. The DI registration on line 365 (`services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"))`) now binds the Catalog-namespaced class — the binder is type-driven, the configuration section path is unchanged, runtime behavior is identical.

- [ ] **Step 6: Run the full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass. `ProductExportDownloadJobTests.cs` still lives in the FileStorage test folder; its `using Anela.Heblo.Application.Features.FileStorage;` directive (line 11) needs to resolve `ProductExportOptions` from somewhere. **Check**: that line will no longer find `ProductExportOptions`. Update the test file's imports now: change line 11 from

```csharp
using Anela.Heblo.Application.Features.FileStorage;
```

to

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
```

Re-run `dotnet test`. Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs
git rm --cached backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs 2>/dev/null || true
git commit -m "refactor: relocate ProductExportOptions to Catalog namespace"
```

---

### Task 10: Move DI registration of `ProductExportOptions` to `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

Catalog owns the options class; Catalog should own its DI registration. Today that registration lives inside `AddHangfireServices` in the API layer (line 365), which is a pure historical accident.

- [ ] **Step 1: Add a `using` import for the Catalog Infrastructure namespace in `CatalogModule.cs`**

If `CatalogModule.cs` does not already have it, add at the top alongside other `Anela.Heblo.Application.Features.Catalog.Infrastructure` imports:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
```

(The file already imports `Anela.Heblo.Application.Features.Catalog.Infrastructure` for other types — verify with a quick grep:

```bash
grep -n "Features.Catalog.Infrastructure" backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
```

If a `using` line already covers the namespace, skip this step.)

- [ ] **Step 2: Add the `Configure<ProductExportOptions>` binding inside `AddCatalogModule`**

In `CatalogModule.cs`, locate the configuration block around lines 102–112 (where `DataSourceOptions` and `CatalogCacheOptions` are bound). Immediately after `services.Configure<CatalogCacheOptions>(...)`, add:

```csharp
        // Configure product-export job options (URL + container) from configuration.
        // Section path "ProductExportOptions" preserved for backward compatibility with
        // existing Key Vault secrets and appsettings entries.
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

- [ ] **Step 3: Remove the old registration from `ServiceCollectionExtensions.cs`**

Line 365 currently reads:

```csharp
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

Delete this line. Then re-evaluate whether the `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` import added in Task 9 Step 4 is still needed:

```bash
grep -n "ProductExportOptions\|Catalog.Infrastructure" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected: no references remain. If so, delete the `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` line. If anything else still uses it, leave it.

- [ ] **Step 4: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds.

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: move ProductExportOptions DI registration to CatalogModule"
```

---

### Task 11: Relocate `ProductExportDownloadJob.cs` to the Catalog namespace

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`

- [ ] **Step 1: Create the new file at the Catalog location**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` with the following content. The class body is byte-for-byte identical to the FileStorage version; only the namespace declaration changes, and the redundant `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` added in Task 9 is dropped (now implicit via the new namespace):

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class ProductExportDownloadJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProductExportDownloadJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ITelemetryService _telemetryService;
    private readonly IOptions<ProductExportOptions> _productExportOptions;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "product-export-download",
        DisplayName = "Product Export Download",
        Description = "Downloads product export data from external systems",
        CronExpression = "0 2 * * *",
        DefaultIsEnabled = true
    };

    public ProductExportDownloadJob(
        IMediator mediator,
        ILogger<ProductExportDownloadJob> logger,
        IRecurringJobStatusChecker statusChecker,
        ITelemetryService telemetryService,
        IOptions<ProductExportOptions> productExportOptions)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
        _telemetryService = telemetryService;
        _productExportOptions = productExportOptions;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Skipped",
            });
            return;
        }

        var exportUrl = _productExportOptions.Value.Url;
        if (string.IsNullOrEmpty(exportUrl))
        {
            throw new InvalidOperationException("Product export URL is not configured");
        }

        var timestamp = DateTime.UtcNow;
        var fileName = $"products_{timestamp:yy_MM_dd_HH_mm}.csv";

        _logger.LogInformation(
            "Starting {JobName} at {Timestamp}. Downloading from {Url} as {FileName}",
            Metadata.JobName, timestamp, exportUrl, fileName);

        var sw = Stopwatch.StartNew();
        DownloadFromUrlResponse? response = null;

        try
        {
            response = await _mediator.Send(new DownloadFromUrlRequest
            {
                FileUrl = exportUrl,
                ContainerName = _productExportOptions.Value.ContainerName,
                BlobName = fileName,
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation("Job {JobName} was cancelled.", Metadata.JobName);
            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Cancelled",
                ["ElapsedMs"] = sw.ElapsedMilliseconds.ToString(),
            });
            throw;
        }

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds.ToString();
        var attemptCount = response?.Params != null && response.Params.TryGetValue("attemptCount", out var ac)
            ? ac
            : "1";

        if (response is { Success: true })
        {
            _logger.LogInformation(
                "{JobName} completed successfully. File: {FileName}, Blob: {BlobUrl}, Size: {Size}",
                Metadata.JobName, response.BlobName, response.BlobUrl, response.FileSizeBytes);

            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["AttemptCount"] = attemptCount,
                ["ElapsedMs"] = elapsedMs,
                ["FileName"] = fileName,
                ["BlobUrl"] = response.BlobUrl ?? string.Empty,
                ["FileSize"] = response.FileSizeBytes.ToString(),
            });
            return;
        }

        // Failure path
        _logger.LogError(
            "{JobName} failed. ErrorCode: {ErrorCode}, Params: {Params}",
            Metadata.JobName, response?.ErrorCode, response?.FullError());

        var props = new Dictionary<string, string>
        {
            ["Status"] = "Failed",
            ["AttemptCount"] = attemptCount,
            ["ElapsedMs"] = elapsedMs,
            ["ErrorCode"] = response?.ErrorCode?.ToString() ?? "FileDownloadFailed",
        };
        if (response?.Params != null && response.Params.TryGetValue("cause", out var cause))
        {
            props["Cause"] = cause;
        }

        _telemetryService.TrackBusinessEvent("ProductExportDownload", props);

        // Rethrow so Hangfire records run as Failed. [AutomaticRetry(Attempts=0)] prevents re-execution.
        var causeForMsg = response?.Params != null && response.Params.TryGetValue("cause", out var c) ? c : "unknown";
        throw new InvalidOperationException(
            $"ProductExportDownload failed (cause={causeForMsg}, attempts={attemptCount}, elapsedMs={elapsedMs}).");
    }
}
```

- [ ] **Step 2: Delete the FileStorage copy**

```bash
git rm backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs
```

- [ ] **Step 3: Verify the FileStorage `Jobs` folder is empty and remove it**

```bash
ls backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/
```

Expected: empty. If empty, remove the folder:

```bash
rmdir backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs
```

(If the directory is not empty, do not delete it — investigate which other job lives there and confirm with the user before continuing.)

- [ ] **Step 4: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. The job's `IRecurringJob` interface is auto-discovered by `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:373` — the discovery scans the entire Application assembly, so the new location is picked up automatically.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs
git commit -m "refactor: relocate ProductExportDownloadJob to Catalog namespace"
```

---

### Task 12: Relocate `ProductExportDownloadJobTests.cs` to the Catalog test folder

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`

- [ ] **Step 1: Create the new test file at the Catalog location**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` with this exact content. The body is identical to the previous file aside from namespace + `using` directives (Catalog Infrastructure replaces FileStorage):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure.Jobs;

public sealed class ProductExportDownloadJobTests
{
    // Captures (eventName, properties) pairs from TrackBusinessEvent calls
    private readonly List<(string EventName, Dictionary<string, string> Properties)> _trackedEvents = new();

    private Mock<ITelemetryService> CreateTelemetryMock()
    {
        var mock = new Mock<ITelemetryService>();
        mock
            .Setup(t => t.TrackBusinessEvent(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>(
                (eventName, props, _) =>
                    _trackedEvents.Add((eventName, props ?? new Dictionary<string, string>())));
        return mock;
    }

    private static ProductExportDownloadJob CreateJob(
        Mock<IMediator> mediatorMock,
        Mock<IRecurringJobStatusChecker> statusCheckerMock,
        Mock<ITelemetryService> telemetryMock,
        string exportUrl = "https://export.example.com/products.csv",
        string containerName = "exports")
    {
        var options = Options.Create(new ProductExportOptions
        {
            Url = exportUrl,
            ContainerName = containerName,
        });

        return new ProductExportDownloadJob(
            mediatorMock.Object,
            new NullLogger<ProductExportDownloadJob>(),
            statusCheckerMock.Object,
            telemetryMock.Object,
            options);
    }

    private static Mock<IRecurringJobStatusChecker> EnabledStatusChecker()
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Mock<IRecurringJobStatusChecker> DisabledStatusChecker()
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return mock;
    }

    [Fact]
    public void Job_HasAutomaticRetryAttribute_WithZeroAttempts()
    {
        // Use CustomAttributeData to inspect without instantiating AutomaticRetryAttribute,
        // whose constructor accesses Hangfire's global LogProvider which may reference a
        // disposed ILoggerFactory from another test's WebApplicationFactory.
        var attributeData = typeof(ProductExportDownloadJob)
            .GetCustomAttributesData()
            .FirstOrDefault(d => d.AttributeType == typeof(AutomaticRetryAttribute));

        attributeData.Should().NotBeNull("ProductExportDownloadJob must have [AutomaticRetry] attribute");

        var attempts = attributeData!.NamedArguments
            .Where(a => a.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .Select(a => (int)a.TypedValue.Value!)
            .FirstOrDefault();

        attempts.Should().Be(0, "Hangfire retries must be disabled to avoid Polly x Hangfire retry multiplication");
    }

    [Fact]
    public async Task Execute_OnSuccess_EmitsExactlyOneSuccessEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = "https://blob/file.csv",
                BlobName = "file.csv",
                ContainerName = "exports",
                FileSizeBytes = 1024,
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Success");
    }

    [Fact]
    public async Task Execute_OnHandlerFailure_EmitsFailedEvent_AndRethrows()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.FileDownloadFailed,
                Params = new Dictionary<string, string>
                {
                    ["cause"] = "retry-exhausted",
                    ["attemptCount"] = "4",
                },
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        var act = async () => await job.ExecuteAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Failed");
        _trackedEvents[0].Properties["Cause"].Should().Be("retry-exhausted");
        _trackedEvents[0].Properties["AttemptCount"].Should().Be("4");
    }

    [Fact]
    public async Task Execute_OnCallerCancellation_EmitsCancelledEvent_AndRethrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .Returns<DownloadFromUrlRequest, CancellationToken>((_, ct) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(ct);
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        var act = async () => await job.ExecuteAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Cancelled");
    }

    [Fact]
    public async Task Execute_OnJobDisabled_EmitsSkippedEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, DisabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Skipped");

        mediator.Verify(
            m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_OnSuccess_DoesNotEmitFailedEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = "https://blob/file.csv",
                BlobName = "file.csv",
                ContainerName = "exports",
                FileSizeBytes = 2048,
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().NotContain(e => e.Properties["Status"] == "Failed");
    }
}
```

Note: the `CreateJob` helper no longer assigns `MaxRetryAttempts`, `DownloadTimeout`, `RetryBaseDelay` on the options instance — those properties no longer exist on the slimmed `ProductExportOptions`. The job itself never read those properties, so this is a pure cleanup.

- [ ] **Step 2: Delete the FileStorage test copy**

```bash
git rm backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs
```

- [ ] **Step 3: Verify and remove the now-empty FileStorage tests subfolder**

```bash
ls backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/
```

Expected: empty.

```bash
rmdir backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs
```

(Leave `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/` in place — it still hosts `DownloadResilienceServiceTests.cs`.)

- [ ] **Step 4: Build + run the relocated tests**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Anela.Heblo.Tests.Features.Catalog.Infrastructure.Jobs.ProductExportDownloadJobTests
```

Expected: build succeeds; 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs
git commit -m "test: relocate ProductExportDownloadJobTests to Catalog"
```

---

### Task 13: Final verification — build, full test suite, manual smoke

- [ ] **Step 1: Search for any lingering references to the old namespaces and constants**

Run each of these greps and confirm zero hits:

```bash
grep -rn "Anela.Heblo.Application.Features.FileStorage.ProductExportOptions\|Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs.ProductExportDownloadJob\|ProductExportDownloadClientName\|\"ProductExportDownload\"" backend/src backend/test
```

Expected: no results.

```bash
grep -rn "ProductExportOptions" backend/src/Anela.Heblo.Application/Features/FileStorage backend/test/Anela.Heblo.Tests/Features/FileStorage
```

Expected: no results.

```bash
grep -rn "ProductExportDownloadJob" backend/src/Anela.Heblo.Application/Features/FileStorage backend/test/Anela.Heblo.Tests/Features/FileStorage
```

Expected: no results.

- [ ] **Step 2: Verify FileStorage is now free of product-export domain leaks**

```bash
ls backend/src/Anela.Heblo.Application/Features/FileStorage
```

Expected listing (no `ProductExportOptions.cs`, no `Infrastructure/Jobs/`):
- `FileDownloadOptions.cs`
- `FileStorageModule.cs`
- `Infrastructure/` (contains `DownloadResilienceService.cs`, `IDownloadResilienceService.cs` only — no `Jobs/`)
- `Services/`
- `UseCases/`

- [ ] **Step 3: Run dotnet format and full build**

```bash
dotnet format backend/Anela.Heblo.sln
dotnet build backend/Anela.Heblo.sln
```

Expected: format runs clean; build succeeds with zero new warnings.

- [ ] **Step 4: Run the full backend test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass; test count is unchanged from the start of this plan (no tests were added or removed net — the `ProductExportOptionsTests` file was replaced 1:1 by `FileDownloadOptionsTests`; the job tests moved location only).

- [ ] **Step 5: Manual smoke check — Hangfire job still scheduled**

Start the API in Development:

```bash
dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

In a separate shell, hit the Hangfire dashboard (path per project convention, usually `/hangfire`). Verify:
- A recurring job with `JobName = "product-export-download"` is registered.
- Its cron expression is `0 2 * * *`.
- Its display name is `Product Export Download`.

(If the dashboard is access-controlled and you cannot reach it locally, instead inspect the seeded configurations via the recurring-jobs admin endpoint that exists in this project, or run a single-shot manual trigger via the dashboard / admin UI per existing conventions.) Stop the API.

- [ ] **Step 6: Confirm the configuration section path resolves**

Inspect `appsettings.json`:

```bash
grep -A3 "\"ProductExportOptions\"" backend/src/Anela.Heblo.API/appsettings.json
```

Expected: the existing `"ProductExportOptions": { "Url": "" }` block is unchanged. Confirm the new `CatalogModule` binding (`configuration.GetSection("ProductExportOptions")`) matches this key.

- [ ] **Step 7: Final clean commit if anything was changed by `dotnet format`**

```bash
git status
```

If `dotnet format` made changes:

```bash
git add -u
git commit -m "chore: dotnet format after refactor"
```

If nothing changed, no commit needed.

- [ ] **Step 8: Summary in PR description (template)**

When opening the PR, include this in the description (replace the bracketed parts):

> **Refactor: Relocate `ProductExportDownloadJob` from FileStorage to Catalog**
>
> - `ProductExportOptions` split into Catalog-owned domain options (`Url`, `ContainerName`) and FileStorage-owned generic download tuning (`FileDownloadOptions` — `HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`).
> - FileStorage named HttpClient constant renamed: `FileStorageModule.ProductExportDownloadClientName` (`"ProductExportDownload"`) → `FileStorageModule.FileDownloadClientName` (`"FileDownload"`).
> - DI registration of `ProductExportOptions` moved from `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` to `Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`. Configuration section path unchanged (`ProductExportOptions:` at root).
> - Tests relocated to mirror source structure; `ProductExportOptionsTests` replaced by `FileDownloadOptionsTests` (its assertions covered the four properties that moved).
> - **Operational note:** `ILogger<ProductExportDownloadJob>` category name changes from `Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs.ProductExportDownloadJob` to `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs.ProductExportDownloadJob`. Hangfire `JobName` (`"product-export-download"`) is unchanged, so recurring-job identity in Hangfire storage is preserved.
> - No new dependencies, no DB migrations, no Key Vault changes, no `appsettings` rename.

---

## Self-Review Notes (run by the plan author)

**Spec coverage check.** Every FR/NFR in `spec.r3.md` is addressed:
- FR-1 (relocate job) → Task 11.
- FR-2 (relocate options) → Tasks 6 + 9; split into FileDownloadOptions per arch-review amendment → Tasks 1–4.
- FR-2a / arch-review (rename HttpClient constant) → Task 8.
- FR-3 (move DI registration) → Task 10.
- FR-4 (config section ownership) → resolved by arch-review (no rename needed); explicitly verified in Task 13 Step 6.
- FR-5 (update all references) → covered across Tasks 3, 4, 7, 8, 9, 10 + verified in Task 13 Step 1.
- FR-6 (preserve `IBlobStorageService` cross-module pattern) → unchanged; job continues to use `IMediator` (which routes through the Domain abstraction in `DownloadFromUrlHandler`).
- FR-7 (tests relocated and green) → Tasks 5, 7, 12, 13.
- NFR-1 (performance) → no allocation/indirection changes; constructor signatures change but call patterns identical.
- NFR-2 (security) → no secret changes; config section unchanged.
- NFR-3 (maintainability / SRP) → final state verified in Task 13 Step 2 (`FileStorage/` contains only generic abstractions).
- NFR-4 (backward compatibility at runtime) → blob filename, container, cadence preserved (job body byte-identical apart from namespace); log category change documented in PR template (Task 13 Step 8).

**Placeholder scan.** No `TBD`/`TODO`/`add appropriate X`/`similar to Task N` placeholders. Every code block shows complete content. Every command shows expected output.

**Type/name consistency check.**
- `FileDownloadOptions` properties defined in Task 1 (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`) are exactly the ones referenced by `DownloadFromUrlHandler._options.Value.HeadTimeout` (Task 3) and `DownloadResilienceService._options.{DownloadTimeout|MaxRetryAttempts|RetryBaseDelay}` (Task 4).
- `ProductExportOptions` properties after Task 6 (`Url`, `ContainerName`) match what `ProductExportDownloadJob` reads (`_productExportOptions.Value.Url`, `_productExportOptions.Value.ContainerName`).
- `FileStorageModule.FileDownloadClientName` (Task 8) replaces every previously-named call site (Tasks 3, 5, 8 substep checks).
- Namespace `Anela.Heblo.Application.Features.Catalog.Infrastructure` (Task 9) is the resolution path for `ProductExportOptions` in the relocated job (Task 11) and relocated job tests (Task 12) — consistent.

**Spec-vs-plan addenda.**
- The arch review identified `DownloadResilienceService` as a consumer of `ProductExportOptions` (timeout/retry properties) — the original spec did not list it. This plan handles it explicitly in Task 4.
- `FileStorageModuleTests.cs` pre-configures the timeout properties; this plan handles the necessary update in Task 7.
- Tests for `ProductExportOptions` defaults cover only the four moved properties; rather than leave a trivial `ProductExportOptionsTests` with zero assertions on the slimmed type, Task 5 replaces it outright with `FileDownloadOptionsTests`. If a future change adds defaults or validation to the Catalog-side `ProductExportOptions`, write a `ProductExportOptionsTests` then.
