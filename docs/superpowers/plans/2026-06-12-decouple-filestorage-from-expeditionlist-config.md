# Decouple FileStorage Module from ExpeditionList Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the `FileStorage` module its own configuration namespace (`FileStorage:BlobConnectionString`), remove its cross-module read of `ExpeditionList:BlobConnectionString`, and add fail-fast validation so a missing key never silently degrades to the development storage emulator in non-Development environments.

**Architecture:** Introduce a strongly-typed `FileStorageOptions` class under `Anela.Heblo.Application/Features/FileStorage/`, bind it via the standard `services.AddOptions<T>().Bind(...)` pattern, register `BlobServiceClient` as a Singleton factory that reads `IOptions<FileStorageOptions>`, and branch validation on `IHostEnvironment.IsDevelopment()` — strict `ValidateOnStart()` in non-Development, soft fallback to `UseDevelopmentStorage=true` (with a warning) in Development. The `ExpeditionList` module is untouched; its `BlobConnectionString` key remains in active use by `AzureBlobPrintQueueSink`.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Hosting.Abstractions`, `Azure.Storage.Blobs`, xUnit + Moq.

---

## Prerequisites (out-of-code, MUST complete before merge)

These are operational prerequisites called out in spec NFR-3 and the architecture review. They are **not** TDD steps — perform them and record the result in the eventual PR description.

- [ ] **P1: Confirm the production Key Vault name.** Run `az keyvault list --query "[].name" -o tsv` (or ask the deploying engineer). Spec says "likely `kv-heblo-prod`" — confirm the exact name before continuing.

- [ ] **P2: Provision `FileStorage--BlobConnectionString` in the staging vault.** Use the same connection string that currently powers production `ExpeditionList--BlobConnectionString`:

```bash
az keyvault secret set \
  --vault-name kv-heblo-stg \
  --name "FileStorage--BlobConnectionString" \
  --value "<staging-connection-string>"
```

- [ ] **P3: Provision `FileStorage--BlobConnectionString` in the production vault** (name confirmed in P1):

```bash
az keyvault secret set \
  --vault-name <prod-vault-name-from-P1> \
  --name "FileStorage--BlobConnectionString" \
  --value "<production-connection-string>"
```

- [ ] **P4: Verify both secrets exist.** This must succeed in both vaults before the code change is merged:

```bash
az keyvault secret show --vault-name kv-heblo-stg --name "FileStorage--BlobConnectionString" --query "name" -o tsv
az keyvault secret show --vault-name <prod-vault-name-from-P1> --name "FileStorage--BlobConnectionString" --query "name" -o tsv
```

Expected: each command prints `FileStorage--BlobConnectionString`. Record the exact vault names in the PR description as required by NFR-3.

- [ ] **P5: Confirm `ASPNETCORE_ENVIRONMENT` for the Test app slot.** Per `docs/architecture/environments.md`, the Test slot runs with `ASPNETCORE_ENVIRONMENT=Test`. Because Test is not `Development`, the new validation will fail at startup unless the connection string is present. Decide between:
  - (a) Adding `FileStorage:BlobConnectionString = "UseDevelopmentStorage=true"` to `appsettings.Test.json` (mirrors the existing `ExpeditionList:BlobConnectionString` pattern in that file), **or**
  - (b) Provisioning the secret wherever the Test slot pulls its config from.

The plan below uses option (a) — appsettings.Test.json gets `UseDevelopmentStorage=true` — because that is the established pattern for the Test slot (see `appsettings.Test.json:18`).

---

## File Structure

**New file:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs` — strongly-typed options class with `SectionName` const and `BlobConnectionString` property.

**Modified files (production code):**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — replace direct `configuration["ExpeditionList:BlobConnectionString"]` read with options binding + env-aware `BlobServiceClient` factory + non-Development `ValidateOnStart()`. Signature changes from `(IServiceCollection, IConfiguration)` to `(IServiceCollection, IConfiguration, IHostEnvironment)`.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs:83` — propagate the existing `IHostEnvironment? environment` parameter (declared on `AddApplicationServices` line 57) into the `AddFileStorageModule` call.

**Modified files (configuration):**
- `backend/src/Anela.Heblo.API/appsettings.json` — add a top-level `"FileStorage"` section with a placeholder `BlobConnectionString`. **Do not touch** the existing `ExpeditionList` section (lines 526–536).
- `backend/src/Anela.Heblo.API/appsettings.Development.json` — add `"FileStorage": { "BlobConnectionString": "UseDevelopmentStorage=true" }` so local dev keeps working without Key Vault.
- `backend/src/Anela.Heblo.API/appsettings.Staging.json` — add an empty `"FileStorage": { "BlobConnectionString": "" }` placeholder; real value comes from `kv-heblo-stg` overlay.
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — add an empty `"FileStorage": { "BlobConnectionString": "" }` placeholder; real value comes from production Key Vault overlay.
- `backend/src/Anela.Heblo.API/appsettings.Test.json` — add `"FileStorage": { "BlobConnectionString": "UseDevelopmentStorage=true" }` (decision from prerequisite P5).
- `backend/src/Anela.Heblo.API/appsettings.Conductor.json` — Conductor runs as a Development-style local variant; leave the connection string empty/absent — the Development fallback path will handle it. No change required.

**Modified files (tests):**
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — extend `BuildBaseServices` to provide a Development `IHostEnvironment`, seed `FileStorage:BlobConnectionString` (or rely on Development fallback), and add two new tests covering FR-4 (fail-fast in non-Development, warning-logged fallback in Development).

**Modified files (docs):**
- `docs/architecture/environments.md` — add a short subsection listing module-owned Key Vault secrets, including the new `FileStorage--BlobConnectionString` entry for staging and production.

**Not touched (deliberate, per spec "Out of Scope"):**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/**` and `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — `ExpeditionList:BlobConnectionString` stays in active production use.
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — exercises `AzureBlobPrintQueueSink`, which is ExpeditionList's concern.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:404` — reads `ExpeditionList:PrintSink`, which is also ExpeditionList's concern.

---

### Task 1: Add `FileStorageOptions` class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`

- [ ] **Step 1: Create the options class**

Write `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.FileStorage;

/// <summary>
/// Configuration options for the FileStorage module's Azure Blob Storage client.
/// </summary>
/// <remarks>
/// <see cref="BlobConnectionString"/> is intentionally not marked <c>[Required]</c>: the Development
/// environment is allowed to leave it empty so <see cref="FileStorageModule"/> can fall back to
/// <c>UseDevelopmentStorage=true</c>. In non-Development environments,
/// <see cref="FileStorageModule.AddFileStorageModule"/> registers a stricter <c>.Validate()</c>
/// rule that fails fast at startup when the value is missing or whitespace.
/// </remarks>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string BlobConnectionString { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to confirm the class compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS (build succeeds; no other code references the new class yet).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs
git commit -m "feat(filestorage): add FileStorageOptions class"
```

---

### Task 2: Update existing `FileStorageModuleTests` to the new module signature (RED)

The existing tests call `services.AddFileStorageModule(BuildConfiguration())` with an empty configuration. After Task 3 the module signature changes to `(IServiceCollection, IConfiguration, IHostEnvironment)`. Update the tests **first** so they describe the target behavior — they will fail to compile until Task 3 lands.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`

- [ ] **Step 1: Replace the test file with the updated shape**

Rewrite the file:

```csharp
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Domain.Features.FileStorage;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class FileStorageModuleTests
{
    private static IServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Mock.Of<ITelemetryService>());
        services.Configure<ProductExportOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });
        return services;
    }

    private static IConfiguration BuildConfiguration(string? blobConnectionString = "UseDevelopmentStorage=true")
    {
        var dict = new Dictionary<string, string?>();
        if (blobConnectionString is not null)
        {
            dict["FileStorage:BlobConnectionString"] = blobConnectionString;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static IHostEnvironment BuildEnvironment(string environmentName) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName);

    [Fact]
    public void AddFileStorageModule_RegistersBlobStorageService_AsSingleton()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));

        // Assert — IBlobStorageService must be Singleton so _containerExists cache survives requests
        var descriptor = services.Single(s => s.ServiceType == typeof(IBlobStorageService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance()
    {
        // Arrange
        var services = BuildBaseServices();
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IBlobStorageService>();
        var second = provider.GetRequiredService<IBlobStorageService>();

        // Assert — same instance proves Singleton registration is working
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFileStorageModule_RegistersNamedHttpClient_ProductExportDownload()
    {
        // Arrange
        var services = BuildBaseServices();
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));
        var provider = services.BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(FileStorageModule.ProductExportDownloadClientName);

        // Assert — named client is registered and timeout is infinite (per-call CTS enforces timeout)
        Assert.NotNull(client);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void AddFileStorageModule_DoesNotRegisterTransientHttpClient()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));

        // Assert — the old services.AddTransient<HttpClient>() self-registers HttpClient with
        // ImplementationType == typeof(HttpClient). AddHttpClient(...) registers a transient with
        // an ImplementationFactory instead, which is the correct IHttpClientFactory pattern.
        // We check for the explicit self-registration to confirm the bug is gone.
        var hasBareTransientHttpClient = services.Any(d =>
            d.ServiceType == typeof(HttpClient) &&
            d.Lifetime == ServiceLifetime.Transient &&
            d.ImplementationType == typeof(HttpClient));

        Assert.False(hasBareTransientHttpClient);
    }

    [Fact]
    public void AddFileStorageModule_RegistersDownloadResilienceService_AsSingleton()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));

        // Assert — IDownloadResilienceService must be Singleton with the correct implementation
        var descriptor = services.Single(d => d.ServiceType == typeof(IDownloadResilienceService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DownloadResilienceService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddFileStorageModule_NamedClient_ConstantIsExported()
    {
        // Assert — the constant must be stable so all consumers reference the same string
        Assert.Equal("ProductExportDownload", FileStorageModule.ProductExportDownloadClientName);
    }
}
```

- [ ] **Step 2: Run the test project to confirm it fails to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — compilation error mentioning that `AddFileStorageModule` does not take a third `IHostEnvironment` argument. This is intentional; Task 3 fixes it.

---

### Task 3: Replace `FileStorageModule` body with options binding + env-aware factory (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` (entire file)

- [ ] **Step 1: Rewrite the module**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` with:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage;

public static class FileStorageModule
{
    public const string ProductExportDownloadClientName = "ProductExportDownload";

    public static IServiceCollection AddFileStorageModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        var optionsBuilder = services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName));

        if (!environment.IsDevelopment())
        {
            // Fail fast in non-Development environments: missing or whitespace connection string
            // surfaces at startup, never silently as a write to the storage emulator in production.
            optionsBuilder
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.BlobConnectionString),
                    $"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.BlobConnectionString)} must be configured.")
                .ValidateOnStart();
        }

        // Register Azure Blob Storage client. The factory reads the already-validated options,
        // so ValidateOnStart() runs before any consumer resolves the BlobServiceClient.
        services.AddSingleton<BlobServiceClient>(provider =>
        {
            var opts = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.BlobConnectionString))
            {
                // Reachable only in Development — validation blocks the empty path elsewhere.
                // Log a warning so the storage-emulator fallback is never silent.
                var logger = provider.GetRequiredService<ILogger<AzureBlobStorageService>>();
                logger.LogWarning(
                    "FileStorage:BlobConnectionString is empty in {Environment}; falling back to UseDevelopmentStorage=true.",
                    environment.EnvironmentName);
                return new BlobServiceClient("UseDevelopmentStorage=true");
            }

            return new BlobServiceClient(opts.BlobConnectionString);
        });

        // Register named HttpClient for product export downloads.
        // PooledConnectionLifetime recycles sockets and refreshes DNS every 5 minutes,
        // preventing the stale-socket and DNS-pinning problems of a long-lived singleton HttpClient.
        // AutomaticDecompression handles gzip/brotli responses from the export URL transparently.
        services.AddHttpClient(ProductExportDownloadClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            })
            .ConfigureHttpClient(c =>
            {
                // Intentional: per-call timeout is enforced by linked CancellationTokenSource
                // inside DownloadResilienceService and around the HEAD probe in
                // DownloadFromUrlHandler. HttpClient.Timeout is left infinite so it does
                // not race with the linked CTS.
                c.Timeout = Timeout.InfiniteTimeSpan;
            });

        // Register resilience service as Singleton — it holds no request state and
        // its internal Polly pipeline is rebuilt per-call (see BuildPipeline).
        services.AddSingleton<IDownloadResilienceService, DownloadResilienceService>();

        // Register blob storage service as Singleton so the _containerExists cache survives across requests.
        // BlobServiceClient is already Singleton — no thread-safety concerns.
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}
```

- [ ] **Step 2: Build the application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: FAIL — `ApplicationModule.cs:83` still calls the two-argument overload. Task 4 fixes that.

---

### Task 4: Propagate `IHostEnvironment` from `ApplicationModule` to `AddFileStorageModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs:83`

- [ ] **Step 1: Update the call site**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, change the line:

```csharp
        services.AddFileStorageModule(configuration);
```

to:

```csharp
        services.AddFileStorageModule(configuration, environment ?? throw new InvalidOperationException(
            "IHostEnvironment must be supplied to AddApplicationServices so FileStorage can branch validation on Development."));
```

(`environment` is the existing nullable parameter declared on line 57: `public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)`. The null-check turns the historical optional argument into a hard precondition for the FileStorage call.)

- [ ] **Step 2: Build the full solution**

Run: `dotnet build`
Expected: PASS — both the application project and the test project compile.

- [ ] **Step 3: Run the FileStorage module tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorageModuleTests"`
Expected: PASS for all six existing tests (the ones rewritten in Task 2).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs
git commit -m "refactor(filestorage): bind options from FileStorage section, drop ExpeditionList read"
```

---

### Task 5: Add fail-fast test for non-Development with missing key (FR-4 / NFR-4)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` (append a new `[Fact]`)

- [ ] **Step 1: Write the failing test**

Append the following test to `FileStorageModuleTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void AddFileStorageModule_NonDevelopmentEnvironmentWithMissingKey_FailsValidation()
    {
        // Arrange — Production environment with no FileStorage:BlobConnectionString seeded
        var services = BuildBaseServices();
        var configuration = BuildConfiguration(blobConnectionString: null);
        services.AddFileStorageModule(configuration, BuildEnvironment(Environments.Production));
        var provider = services.BuildServiceProvider();

        // Act — resolving IOptions<FileStorageOptions>.Value triggers the same .Validate pipeline
        // that ValidateOnStart() runs at host start. This is the unit-test analogue: we want to
        // confirm the rule fires and the message names the missing key (per spec NFR-2: no value
        // leakage; the key name is mentioned, not the offending value).
        var act = () => provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;

        // Assert
        var ex = Assert.Throws<OptionsValidationException>(act);
        Assert.Contains("FileStorage:BlobConnectionString", ex.Message);
    }
```

- [ ] **Step 2: Add the required using directive**

If the file does not already have it (it does not), add at the top:

```csharp
using Microsoft.Extensions.Options;
```

- [ ] **Step 3: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddFileStorageModule_NonDevelopmentEnvironmentWithMissingKey_FailsValidation"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs
git commit -m "test(filestorage): fail fast when BlobConnectionString missing in non-Development"
```

---

### Task 6: Add Development-fallback warning test (FR-4 Development branch)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` (append another `[Fact]`)

- [ ] **Step 1: Write the failing test**

Append:

```csharp
    [Fact]
    public void AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning()
    {
        // Arrange — Development environment, no FileStorage:BlobConnectionString
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ITelemetryService>());
        services.Configure<ProductExportOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });

        var warningLogger = new Mock<ILogger<AzureBlobStorageService>>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // Override the AzureBlobStorageService logger so we can verify the warning was emitted.
        services.AddSingleton(warningLogger.Object);

        var configuration = BuildConfiguration(blobConnectionString: null);
        services.AddFileStorageModule(configuration, BuildEnvironment(Environments.Development));
        var provider = services.BuildServiceProvider();

        // Act — resolving the BlobServiceClient runs the factory, which emits the warning
        // and returns a client pointed at UseDevelopmentStorage=true.
        var client = provider.GetRequiredService<BlobServiceClient>();

        // Assert — client is constructed (no throw) and the warning was logged once.
        Assert.NotNull(client);
        warningLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("FileStorage:BlobConnectionString")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Add the required using directive**

If not already present, add:

```csharp
using Anela.Heblo.Application.Features.FileStorage.Services;
using Azure.Storage.Blobs;
```

- [ ] **Step 3: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning"`
Expected: PASS.

- [ ] **Step 4: Run the full FileStorage test file**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorageModuleTests"`
Expected: PASS for all eight tests (six original + two new).

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs
git commit -m "test(filestorage): warn-and-fallback to storage emulator in Development"
```

---

### Task 7: Add the new `FileStorage` configuration section to `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add the section**

Open `backend/src/Anela.Heblo.API/appsettings.json`. Locate the existing `"ExpeditionList"` section (around line 526). Add a sibling top-level `"FileStorage"` section **immediately before** `"ExpeditionList"`:

```json
  "FileStorage": {
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net"
  },
```

Do not change, remove, or reorder the existing `"ExpeditionList"` block. The placeholder value mirrors the existing `ExpeditionList:BlobConnectionString` placeholder in the same file.

- [ ] **Step 2: Verify the JSON is valid**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: PASS (build copies `appsettings.json` and would surface a JSON parse error).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "config: add FileStorage section to appsettings.json"
```

---

### Task 8: Add per-environment overrides for the new section

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.Development.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Test.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Staging.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Production.json`

- [ ] **Step 1: `appsettings.Development.json`**

Add a top-level `"FileStorage"` section (e.g., immediately before the existing `"ExpeditionList"` block around line 64):

```json
  "FileStorage": {
    "BlobConnectionString": "UseDevelopmentStorage=true"
  },
```

- [ ] **Step 2: `appsettings.Test.json`**

Add immediately before the existing `"ExpeditionList"` block (around line 17):

```json
  "FileStorage": {
    "BlobConnectionString": "UseDevelopmentStorage=true"
  },
```

- [ ] **Step 3: `appsettings.Staging.json`**

Add immediately before the existing `"ExpeditionList"` block (around line 77):

```json
  "FileStorage": {
    "BlobConnectionString": ""
  },
```

The empty value is a placeholder; the real value is supplied by the `kv-heblo-stg` Key Vault overlay via the `FileStorage--BlobConnectionString` secret provisioned in prerequisite P2.

- [ ] **Step 4: `appsettings.Production.json`**

Add immediately before the existing `"ExpeditionList"` block (around line 78):

```json
  "FileStorage": {
    "BlobConnectionString": ""
  },
```

Same rationale as Staging — the real value comes from the production Key Vault overlay provisioned in P3.

- [ ] **Step 5: Verify all four JSON files parse**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.Development.json \
        backend/src/Anela.Heblo.API/appsettings.Test.json \
        backend/src/Anela.Heblo.API/appsettings.Staging.json \
        backend/src/Anela.Heblo.API/appsettings.Production.json
git commit -m "config: add FileStorage section to per-environment appsettings"
```

---

### Task 9: Document the new Key Vault secret in `environments.md`

**Files:**
- Modify: `docs/architecture/environments.md`

- [ ] **Step 1: Append a "Module-owned Key Vault secrets" subsection**

At the end of the file, append:

```markdown

---

## 🔐 Module-owned Key Vault Secrets

Modules read their own configuration sections only. Secret keys mirror the configuration path with `--` replacing `:` (the project convention). When adding a new secret-backed config key, list it here.

| Module | Config key | Key Vault secret | Staging vault | Production vault |
|--------|------------|------------------|---------------|------------------|
| FileStorage | `FileStorage:BlobConnectionString` | `FileStorage--BlobConnectionString` | `kv-heblo-stg` | _(confirmed in PR)_ |
| ExpeditionList | `ExpeditionList:BlobConnectionString` | `ExpeditionList--BlobConnectionString` | `kv-heblo-stg` | _(confirmed in PR)_ |

**Rollout note (2026-06-12):** `FileStorage--BlobConnectionString` was introduced when `FileStorageModule` was decoupled from the `ExpeditionList` configuration namespace. The secret must be provisioned in every non-Development target environment **before** the code change is deployed — the module fails fast on missing values in non-Development environments (see `FileStorageModule.AddFileStorageModule`).
```

- [ ] **Step 2: Commit**

```bash
git add docs/architecture/environments.md
git commit -m "docs(architecture): document FileStorage Key Vault secret"
```

---

### Task 10: Verification grep — confirm cleanup is complete (FR-6)

This task runs the repository-wide searches called out in FR-6 and records the findings in the eventual PR description.

- [ ] **Step 1: Confirm no FileStorage code reads `ExpeditionList:` config**

Run: `git grep -n 'ExpeditionList:' -- 'backend/src/Anela.Heblo.Application/Features/FileStorage/**' 'backend/test/Anela.Heblo.Tests/Features/FileStorage/**'`
Expected: empty output.

- [ ] **Step 2: Confirm no code outside `ExpeditionList` reads `ExpeditionList:BlobConnectionString`**

Run: `git grep -n 'ExpeditionList:BlobConnectionString' -- backend/`
Expected: matches only inside (a) `ExpeditionList` source, (b) `Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`, (c) `CombinedPrintQueueSinkRegistrationTests.cs`, and (d) the four `appsettings*.json` files (the existing `ExpeditionList` blocks). No match in `Features/FileStorage` or in `FileStorageModule.cs`. Save the output for the PR.

- [ ] **Step 3: Confirm no code outside `FileStorage` reads `FileStorage:` config**

Run: `git grep -n 'configuration\["FileStorage:' -- backend/`
Expected: empty output (the new module reads through `IOptions<FileStorageOptions>`, not the raw indexer).

Run: `git grep -n '"FileStorage"' -- backend/`
Expected: matches only in `FileStorageOptions.cs` (the `SectionName` const) and the five `appsettings*.json` files modified by this plan. Save the output for the PR.

- [ ] **Step 4: Record findings**

These commands will be re-run by the human in the PR description. No commit for this task; it produces evidence only.

---

### Task 11: Final full-build + format + full-test sweep

- [ ] **Step 1: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no diffs reported, or only whitespace touch-ups inside the files modified by this plan.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS, zero errors, zero warnings introduced by this change.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — including the eight `FileStorageModuleTests` and the untouched `CombinedPrintQueueSinkRegistrationTests` (which still seeds `ExpeditionList:BlobConnectionString` for ExpeditionList's purposes).

- [ ] **Step 4: Commit any formatting changes**

If `dotnet format` produced edits:

```bash
git add -u
git commit -m "chore: dotnet format"
```

Otherwise: skip this step.

---

## Spec Coverage Map

| Requirement | Covered by |
|-------------|------------|
| FR-1: `FileStorage:BlobConnectionString` exists in `appsettings.json` and env files | Tasks 7, 8 |
| FR-1: Key Vault secrets provisioned in staging + production | Prerequisites P1–P4 |
| FR-1: New value matches the currently used connection string | Prerequisites P2, P3 (instructions copy the existing production value) |
| FR-2: `FileStorageModule.cs:20` no longer references `ExpeditionList:` | Task 3 |
| FR-2: Repo grep for `ExpeditionList:BlobConnectionString` inside FileStorage returns zero | Task 10 step 1 |
| FR-2: `ExpeditionList` untouched | "Not touched" list in File Structure; verified by Task 10 step 2 scope |
| FR-3: `FileStorageOptions` class (not record) under FileStorage feature folder | Task 1 |
| FR-3: Options binding via `services.Configure<FileStorageOptions>` / `AddOptions<T>().Bind` | Task 3 |
| FR-3: Service resolves via `IOptions<FileStorageOptions>` | Task 3 (factory reads `provider.GetRequiredService<IOptions<FileStorageOptions>>()`) |
| FR-4: Fail fast in non-Development | Task 3 (env-branched `ValidateOnStart`); Task 5 covers with test |
| FR-4: Development fallback with warning | Task 3 (factory); Task 6 covers with test |
| FR-4: Validation runs at startup, not lazily | Task 3 (`ValidateOnStart()`) |
| FR-5: `environments.md` updated | Task 9 |
| FR-5: Deployment scripts referencing `ExpeditionList:BlobConnectionString` for FileStorage's purpose — none exist (verified by grep) | Task 10 step 2 (recorded in PR) |
| FR-6: Repo grep for `configuration["ExpeditionList:` outside ExpeditionList | Task 10 step 2 |
| FR-6: Repo grep for `configuration["FileStorage:` outside FileStorage | Task 10 step 3 |
| NFR-1: No measurable performance impact | Inherent — configuration-only change; no test needed |
| NFR-2: Secrets stored in Key Vault, not Web App env vars | Prerequisites P2, P3 |
| NFR-2: No legacy secret removal | "Not touched" list; spec Out of Scope |
| NFR-2: No connection string in error messages | Task 3 (error names only the key); Task 5 (test asserts the key name is present) |
| NFR-3: Hard cutover, secret first | Prerequisites P2–P4 run **before** Tasks 3–11 land; PR description records vault names |
| NFR-3: No legacy-key fallback in new code | Task 3 (factory falls back only to `UseDevelopmentStorage=true`, only in Development) |
| NFR-4: Options binding unit-testable | Tasks 2, 5, 6 use `ConfigurationBuilder().AddInMemoryCollection(...)` |
| NFR-4: Fail-fast covered by test | Task 5 |

## Open items deferred to PR review

- The production Key Vault name (P1) must be filled into Task 9's documentation table and the PR description before merge. If the vault is **not** `kv-heblo-prod`, also update the `--vault-name` placeholder in `docs/architecture/environments.md` Rollout note.
- If P5 reveals that the Test slot pulls config from somewhere other than `appsettings.Test.json`, Task 8 step 2 may need to be replaced or supplemented by a secret-provisioning step in that source.
