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
