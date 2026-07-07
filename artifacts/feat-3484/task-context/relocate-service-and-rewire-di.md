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

