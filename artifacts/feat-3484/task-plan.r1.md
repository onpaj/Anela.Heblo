# Implementation Plan: Relocate AzureBlobStorageService to the Azure adapter layer

This is a Clean Architecture compliance refactor. It moves one Azure-SDK-backed service class
(`AzureBlobStorageService`) and its DI wiring out of the Application ring into the Azure adapter
ring, removes the `Azure.Storage.Blobs` package reference from `Anela.Heblo.Application`, and
updates two test files. Runtime behavior is unchanged.

The repo root during implementation is the feature worktree
(`/home/user/worktrees/feature-3484-Arch-Review-Filestorage-Azureblobstorageservice-Pl`). All
paths below are relative to that root unless stated otherwise. All backend `dotnet` commands are
run from the `backend/` directory.

There are two tasks:

1. `relocate-service-and-rewire-di` — all production-code changes (move the class, add the adapter
   DI extension, strip Azure registrations from `FileStorageModule`, remove the package reference,
   wire the new extension into `Program.cs`). Must be applied atomically; the src builds only when
   all of these are done together.
2. `update-filestorage-tests` — update the two affected test files so the test project compiles and
   all touched tests pass.

Task 2 depends on Task 1. The full solution (including the test project) compiles only after Task 2.

---

### task: relocate-service-and-rewire-di

**Goal**
Move `AzureBlobStorageService` from the Application layer into `Anela.Heblo.Adapters.Azure`, add a
new `AddAzureBlobStorageService` extension to `AzureAdapterModule` that owns the `BlobServiceClient`
factory and the `IBlobStorageService` binding, remove those registrations (and the Azure `using`)
from `FileStorageModule`, remove the `Azure.Storage.Blobs` package reference from the Application
project, and wire the new extension into `Program.cs`. Behavior is byte-for-byte identical.

**Files to create/modify**
- Move (git mv to preserve history):
  - FROM `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`
  - TO   `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`
- Modify `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`
- Modify `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
- Modify `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
- Modify `backend/src/Anela.Heblo.API/Program.cs`

**Implementation steps**

1. **Move the service file with git so history is preserved.** From the repo root:
   ```bash
   git mv \
     backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs \
     backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs
   ```
   The `Services/` folder in the Application FileStorage feature may become empty after this — leave
   it; do not delete other files. (There are no other files in it; git will simply stop tracking the
   moved file at the old path.)

2. **Update the namespace and usings of the moved file.** In the new file
   `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`,
   replace the top-of-file `using` block and namespace (currently lines 1–7) with the following. The
   only changes are: (a) the namespace, and (b) a new
   `using Anela.Heblo.Application.Features.FileStorage;` so the reference to
   `FileStorageModule.FileDownloadClientName` on line 38 still resolves. Do **not** touch any method
   body, the `BlobDownloadStream` helper, the `_containerExists` cache, or the content-type helpers.

   Change:
   ```csharp
   using System.Collections.Concurrent;
   using Azure.Storage.Blobs;
   using Azure.Storage.Blobs.Models;
   using Anela.Heblo.Domain.Features.FileStorage;
   using Microsoft.Extensions.Logging;

   namespace Anela.Heblo.Application.Features.FileStorage.Services;
   ```
   to:
   ```csharp
   using System.Collections.Concurrent;
   using Azure.Storage.Blobs;
   using Azure.Storage.Blobs.Models;
   using Anela.Heblo.Application.Features.FileStorage;
   using Anela.Heblo.Domain.Features.FileStorage;
   using Microsoft.Extensions.Logging;

   namespace Anela.Heblo.Adapters.Azure.Features.FileStorage;
   ```
   Note: the reference to the named HTTP client on line 38 stays exactly as
   `_httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName)` — do NOT duplicate the
   `"FileDownload"` string literal.

3. **Add the `AddAzureBlobStorageService` extension to `AzureAdapterModule`.** Edit
   `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`. Add the required `using`
   directives and a second public static extension method. The `BlobServiceClient` factory body is
   moved verbatim from `FileStorageModule` (old lines 42–57), including the exact warning message
   string. The extension takes `IHostEnvironment` (not `IConfiguration`) because the options binding
   and validation stay in `FileStorageModule`; the factory only needs `environment.EnvironmentName`
   for the fallback warning.

   Replace the whole file contents with:
   ```csharp
   // backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
   using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
   using Anela.Heblo.Adapters.Azure.Features.FileStorage;
   using Anela.Heblo.Application.Features.ExpeditionList;
   using Anela.Heblo.Application.Features.FileStorage;
   using Anela.Heblo.Application.Shared.Printing;
   using Anela.Heblo.Domain.Features.FileStorage;
   using Azure.Storage.Blobs;
   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   namespace Anela.Heblo.Adapters.Azure;

   public static class AzureAdapterModule
   {
       public static IServiceCollection AddAzurePrintQueueSink(
           this IServiceCollection services,
           IConfiguration configuration)
       {
           services.AddSingleton(provider =>
           {
               var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
               return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
           });

           services.AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>();

           return services;
       }

       public static IServiceCollection AddAzureBlobStorageService(
           this IServiceCollection services,
           IHostEnvironment environment)
       {
           // Register Azure Blob Storage client. The factory reads the already-validated options,
           // so ValidateOnStart() (registered by FileStorageModule) runs before any consumer
           // resolves the BlobServiceClient.
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

           // Register blob storage service as Singleton so the _containerExists cache survives across requests.
           // BlobServiceClient is already Singleton — no thread-safety concerns.
           services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

           return services;
       }
   }
   ```
   (`Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` are
   available transitively — the Application project references both, the adapter references
   Application, and the sibling `AzureBlobPrintQueueSink` already uses `Microsoft.Extensions.Logging`.
   No new package reference is needed in `Anela.Heblo.Adapters.Azure.csproj`.)

4. **Strip the Azure registrations and Azure `using`s from `FileStorageModule`.** Edit
   `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`.

   4a. Remove these two `using` lines (old lines 3 and 5):
   ```csharp
   using Anela.Heblo.Application.Features.FileStorage.Services;
   ...
   using Azure.Storage.Blobs;
   ```

   4b. Remove the entire `BlobServiceClient` factory block (old lines 40–57), i.e. delete:
   ```csharp
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

   ```

   4c. Remove the `IBlobStorageService` binding and its two-line comment (old lines 82–84), i.e. delete:
   ```csharp
           // Register blob storage service as Singleton so the _containerExists cache survives across requests.
           // BlobServiceClient is already Singleton — no thread-safety concerns.
           services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

   ```

   After these edits the file must look exactly like this (verify against this target):
   ```csharp
   using System.Net;
   using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
   using Anela.Heblo.Domain.Features.FileStorage;
   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   namespace Anela.Heblo.Application.Features.FileStorage;

   public static class FileStorageModule
   {
       public const string FileDownloadClientName = "FileDownload";

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

           // Register named HttpClient for product export downloads.
           // PooledConnectionLifetime recycles sockets and refreshes DNS every 5 minutes,
           // preventing the stale-socket and DNS-pinning problems of a long-lived singleton HttpClient.
           // AutomaticDecompression handles gzip/brotli responses from the export URL transparently.
           services.AddHttpClient(FileDownloadClientName)
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

           services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));

           return services;
       }
   }
   ```
   Note: `using Microsoft.Extensions.Options;` STAYS — it is still used by `.AddOptions<>()` /
   `.Bind()` / `.Validate()` / `.ValidateOnStart()`. `using Microsoft.Extensions.Logging;` STAYS —
   removing it is not required and other lines/analyzers may reference it; leave it as-is to keep the
   diff surgical. (If `dotnet format` flags it as unused, remove it then — see acceptance.)

5. **Remove the `Azure.Storage.Blobs` package reference from the Application project.** Edit
   `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` and delete line 12:
   ```xml
       <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
   ```
   Leave every other `PackageReference` untouched.

6. **Wire the new extension into the API composition root.** Edit
   `backend/src/Anela.Heblo.API/Program.cs`. The `using Anela.Heblo.Adapters.Azure;` already exists
   (line 5). Immediately after the existing `AddApplicationServices` call (line 103), add the
   unconditional registration. Change:
   ```csharp
           builder.Services.AddApplicationServices(builder.Configuration, builder.Environment); // Vertical slice modules from Application layer
           builder.Services.AddScoped<ISmartsuppWebhookMetrics, SmartsuppWebhookMetrics>();
   ```
   to:
   ```csharp
           builder.Services.AddApplicationServices(builder.Configuration, builder.Environment); // Vertical slice modules from Application layer
           builder.Services.AddAzureBlobStorageService(builder.Environment); // Azure adapter: BlobServiceClient + IBlobStorageService binding (moved out of Application ring)
           builder.Services.AddScoped<ISmartsuppWebhookMetrics, SmartsuppWebhookMetrics>();
   ```
   Do NOT place this call inside `AddPrintQueueSink`'s switch (that would make the binding
   conditional on `ExpeditionList:PrintSink`, regressing `FileSystem`/`Cups` environments) and do NOT
   place it in `ApplicationModule` (the Application ring cannot reference the adapter — it would
   create a reference cycle).

7. **Confirm no stray Application-layer references to the Azure SDK remain.** From `backend/`:
   ```bash
   grep -rn "Azure.Storage\|BlobServiceClient\|BlobContainerClient" src/Anela.Heblo.Application
   ```
   Expected output: no matches (empty). If anything is printed, it must be resolved before the task
   is complete.

**Tests to write/update**
None in this task. Test-file updates are Task 2 (`update-filestorage-tests`). The test project will
not compile until Task 2 is done — that is expected. Build only the production projects here (see
acceptance).

**Acceptance criteria**
1. The moved file exists at
   `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`
   with namespace `Anela.Heblo.Adapters.Azure.Features.FileStorage`, and no file exists at
   `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`.
2. The production projects build. From `backend/`:
   ```bash
   dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
   ```
   Expected: `Build succeeded.` with `0 Error(s)`. (This transitively builds
   `Anela.Heblo.Application` and `Anela.Heblo.Adapters.Azure`, proving the package removal is safe and
   the move compiles. Do NOT run a full-solution build here — the test project intentionally does not
   compile until Task 2.)
3. The Application project no longer uses any Azure SDK type. From `backend/`:
   ```bash
   grep -rn "Azure.Storage\|BlobServiceClient\|BlobContainerClient" src/Anela.Heblo.Application
   ```
   Expected: empty output.
4. `Anela.Heblo.Application.csproj` no longer contains `Azure.Storage.Blobs`; verify:
   ```bash
   grep -n "Azure.Storage.Blobs" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```
   Expected: empty output. And `Anela.Heblo.Adapters.Azure.csproj` still contains it:
   ```bash
   grep -n "Azure.Storage.Blobs" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj
   ```
   Expected: one match (`Version="12.25.0"`).
5. `Program.cs` contains exactly one `AddAzureBlobStorageService(builder.Environment)` call,
   immediately after `AddApplicationServices`.
6. Formatting is clean. From `backend/`:
   ```bash
   dotnet format --verify-no-changes
   ```
   Expected: no changes reported. (If it reports an unused `using Microsoft.Extensions.Logging;` in
   `FileStorageModule.cs`, remove that line and re-run.)

**Commit**
```bash
git add -A
git commit -m "Relocate AzureBlobStorageService and its DI to the Azure adapter"
```

---

### task: update-filestorage-tests

**Goal**
Update the two affected test files so the test project compiles against the relocated type and the
new adapter DI extension, and all touched tests pass. `AzureBlobStorageServiceTests` only needs a
`using`/namespace update. Three registration tests currently in `FileStorageModuleTests` assert the
moved registrations and must be relocated into a new `AzureAdapterModuleTests` that calls both
`AddFileStorageModule` (for the options binding + validation) and `AddAzureBlobStorageService` (for
the `BlobServiceClient` + `IBlobStorageService` binding).

This task depends on `relocate-service-and-rewire-di` being complete.

**Files to create/modify**
- Modify `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`
- Modify `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
- Create `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureAdapterModuleTests.cs`

(No project reference change is needed — `Anela.Heblo.Tests.csproj` already references
`Anela.Heblo.Adapters.Azure`.)

**Implementation steps**

1. **Fix the `using`s in `AzureBlobStorageServiceTests.cs`.** Edit
   `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`. Only the
   type's namespace changed; the test constructs `AzureBlobStorageService` directly (no DI) and still
   references `FileStorageModule.FileDownloadClientName`. Change the first two `using` lines:
   ```csharp
   using Anela.Heblo.Application.Features.FileStorage;
   using Anela.Heblo.Application.Features.FileStorage.Services;
   ```
   to:
   ```csharp
   using Anela.Heblo.Adapters.Azure.Features.FileStorage;
   using Anela.Heblo.Application.Features.FileStorage;
   ```
   (`using Anela.Heblo.Application.Features.FileStorage;` is retained because the test references
   `FileStorageModule.FileDownloadClientName`; the `...Services` namespace no longer exists.) Make no
   other changes to this file.

2. **Remove the three relocated registration tests from `FileStorageModuleTests.cs`.** Edit
   `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`.

   2a. Delete these three test methods entirely (they assert registrations that now live in the
   adapter):
   - `AddFileStorageModule_RegistersBlobStorageService_AsSingleton` (old lines 47–59)
   - `AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance` (old lines 61–75)
   - `AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning`
     (old lines 157–193)

   2b. Remove the now-unused `using` directives from the top of the file. After deleting the three
   methods, `FileStorageModuleTests` no longer references `IBlobStorageService`, `BlobServiceClient`,
   `AzureBlobStorageService`, or `NullLogger`/`Mock<ILogger<...>>`-of-blob-service. Delete these
   `using` lines:
   ```csharp
   using Anela.Heblo.Application.Features.FileStorage.Services;
   using Azure.Storage.Blobs;
   ```
   Keep everything else. The remaining tests (`RegistersNamedHttpClient_FileDownload`,
   `DoesNotRegisterTransientHttpClient`, `RegistersDownloadResilienceService_AsSingleton`,
   `NamedClient_ConstantIsExported`, `NonDevelopmentEnvironmentWithMissingKey_FailsValidation`) all
   reference only non-Azure registrations that still live in `FileStorageModule` and must remain
   unchanged. `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`
   (NullLogger), and `Moq` `using`s STAY — `BuildBaseServices()` still registers
   `NullLogger<>` and `Mock.Of<ITelemetryService>()`, and other retained tests use them.

   After 2a/2b the file must be exactly:
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
   using Microsoft.Extensions.Options;
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
           services.Configure<FileDownloadOptions>(opts =>
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
       public void AddFileStorageModule_RegistersNamedHttpClient_FileDownload()
       {
           // Arrange
           var services = BuildBaseServices();
           services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));
           var provider = services.BuildServiceProvider();

           // Act
           var factory = provider.GetRequiredService<IHttpClientFactory>();
           var client = factory.CreateClient(FileStorageModule.FileDownloadClientName);

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
           Assert.Equal("FileDownload", FileStorageModule.FileDownloadClientName);
       }

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
   }
   ```

3. **Create `AzureAdapterModuleTests.cs` with the three relocated tests.** Create
   `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureAdapterModuleTests.cs`. These tests
   assert the registrations now owned by `AddAzureBlobStorageService`. Because the options binding +
   validation stay in `FileStorageModule`, each test calls BOTH `AddFileStorageModule(config, env)`
   AND `AddAzureBlobStorageService(env)` — this coupling is intentional and documented in the class
   comment. The three tests are the exact analogues of the ones removed from `FileStorageModuleTests`
   (Singleton lifetime, same-instance resolve, and the Development missing-key fallback warning),
   updated to resolve against the adapter registration.

   ```csharp
   using Anela.Heblo.Adapters.Azure;
   using Anela.Heblo.Adapters.Azure.Features.FileStorage;
   using Anela.Heblo.Application.Features.FileStorage;
   using Anela.Heblo.Domain.Features.FileStorage;
   using Anela.Heblo.Xcc.Telemetry;
   using Azure.Storage.Blobs;
   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;
   using Xunit;

   namespace Anela.Heblo.Tests.Features.FileStorage;

   /// <summary>
   /// Tests for AzureAdapterModule.AddAzureBlobStorageService — the BlobServiceClient factory and
   /// the IBlobStorageService binding relocated from FileStorageModule to the Azure adapter ring.
   ///
   /// Each test calls BOTH AddFileStorageModule (which binds and validates FileStorageOptions) and
   /// AddAzureBlobStorageService (which registers BlobServiceClient + IBlobStorageService). The
   /// options binding intentionally stays in FileStorageModule, so the adapter registration depends
   /// on it being present.
   /// </summary>
   public class AzureAdapterModuleTests
   {
       private static IServiceCollection BuildBaseServices()
       {
           var services = new ServiceCollection();
           services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
           services.AddSingleton(Mock.Of<ITelemetryService>());
           services.Configure<FileDownloadOptions>(opts =>
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
       public void AddAzureBlobStorageService_RegistersBlobStorageService_AsSingleton()
       {
           // Arrange
           var services = BuildBaseServices();
           var environment = BuildEnvironment(Environments.Development);

           // Act
           services.AddFileStorageModule(BuildConfiguration(), environment);
           services.AddAzureBlobStorageService(environment);

           // Assert — IBlobStorageService must be Singleton so _containerExists cache survives requests
           var descriptor = services.Single(s => s.ServiceType == typeof(IBlobStorageService));
           Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
       }

       [Fact]
       public void AddAzureBlobStorageService_ResolvingBlobStorageServiceTwice_ReturnsSameInstance()
       {
           // Arrange
           var services = BuildBaseServices();
           var environment = BuildEnvironment(Environments.Development);
           services.AddFileStorageModule(BuildConfiguration(), environment);
           services.AddAzureBlobStorageService(environment);
           var provider = services.BuildServiceProvider();

           // Act
           var first = provider.GetRequiredService<IBlobStorageService>();
           var second = provider.GetRequiredService<IBlobStorageService>();

           // Assert — same instance proves Singleton registration is working
           Assert.Same(first, second);
       }

       [Fact]
       public void AddAzureBlobStorageService_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning()
       {
           // Arrange — Development environment, no FileStorage:BlobConnectionString
           var services = new ServiceCollection();
           services.AddSingleton(Mock.Of<ITelemetryService>());
           services.Configure<FileDownloadOptions>(opts =>
           {
               opts.MaxRetryAttempts = 3;
               opts.DownloadTimeout = TimeSpan.FromSeconds(120);
               opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
           });

           var warningLogger = new Mock<ILogger<AzureBlobStorageService>>();
           services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
           // Override the AzureBlobStorageService logger so we can verify the warning was emitted.
           services.AddSingleton(warningLogger.Object);

           var environment = BuildEnvironment(Environments.Development);
           var configuration = BuildConfiguration(blobConnectionString: null);
           services.AddFileStorageModule(configuration, environment);
           services.AddAzureBlobStorageService(environment);
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
   }
   ```

**Tests to write/update**
Covered by steps 1–3 above:
- `AzureBlobStorageServiceTests.cs` — `using`/namespace update only.
- `FileStorageModuleTests.cs` — three registration tests removed; unused `using`s removed.
- `AzureAdapterModuleTests.cs` — new file with the three relocated registration tests.

**Acceptance criteria**
1. The full solution builds. From `backend/`:
   ```bash
   dotnet build
   ```
   Expected: `Build succeeded.` with `0 Error(s)` (the test project now compiles).
2. All FileStorage tests pass. From `backend/`:
   ```bash
   dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FileStorage"
   ```
   Expected: all tests pass, `Failed: 0`. This must include:
   - the unchanged `AzureBlobStorageServiceTests` cases,
   - the retained `FileStorageModuleTests` cases
     (`RegistersNamedHttpClient_FileDownload`, `DoesNotRegisterTransientHttpClient`,
     `RegistersDownloadResilienceService_AsSingleton`, `NamedClient_ConstantIsExported`,
     `NonDevelopmentEnvironmentWithMissingKey_FailsValidation`),
   - the three new `AzureAdapterModuleTests` cases
     (`RegistersBlobStorageService_AsSingleton`, `ResolvingBlobStorageServiceTwice_ReturnsSameInstance`,
     `DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning`).
3. Formatting is clean. From `backend/`:
   ```bash
   dotnet format --verify-no-changes
   ```
   Expected: no changes reported.

**Commit**
```bash
git add -A
git commit -m "Update FileStorage tests for relocated AzureBlobStorageService"
```

---

## Plan self-review

- **Spec requirement coverage:**
  - FR-1 (move class, new namespace, identical logic, `FileDownloadClientName` reference intact) →
    Task 1 steps 1–2, acceptance 1.
  - FR-2 (move `BlobServiceClient` factory + `IBlobStorageService` bind to `AddAzureBlobStorageService`;
    `FileStorageModule` keeps options/HTTP/resilience) → Task 1 steps 3–4, acceptance 2–3.
  - FR-3 (wire into startup, unconditional, correct validation timing) → Task 1 step 6, acceptance 5;
    validation timing preserved because `FileStorageModule` still owns the binding + `ValidateOnStart`.
  - FR-4 (remove `Azure.Storage.Blobs` from Application; repo-wide grep clean) → Task 1 step 5 & 7,
    acceptance 3–4.
  - FR-5 (both test files compile/pass; assertions relocated to adapter test) → Task 2 (all steps),
    acceptance 1–2.
  - NFR-1/NFR-2 (Singleton lifetimes, dev-fallback warning verbatim) → factory moved verbatim
    (Task 1 step 3); warning-message assertion preserved (Task 2 step 3).
  - NFR-3 (build + format + full test suite) → Task 1 acceptance 2 & 6; Task 2 acceptance 1–3.
- **Design amendments honored:** extension takes `IHostEnvironment` (not `IConfiguration`); wired
  from `Program.cs` (not `ApplicationModule`, not inside `AddPrintQueueSink`); three specific tests
  relocated to a new adapter-module test calling both modules.
- **Out-of-scope respected:** `AzureBlobConflictTelemetryFilter` (doc-comment-only reference) is not
  touched; no shared `BlobServiceClient`/`BlobContainerClient`; no config keys added.
- **Placeholder scan:** no "TBD"/"similar to"/"add error handling" placeholders; all code blocks are
  concrete and copy-ready.
- **Type/name consistency:** `AddAzureBlobStorageService(IServiceCollection, IHostEnvironment)`,
  namespace `Anela.Heblo.Adapters.Azure.Features.FileStorage`, and
  `FileStorageModule.FileDownloadClientName` are used identically across both tasks and match the
  actual source read from the repo.
