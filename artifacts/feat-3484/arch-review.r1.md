# Architecture Review: Relocate AzureBlobStorageService to the Azure adapter layer

## Skip Design: true

This is a backend-only Clean Architecture compliance refactor: moving one Azure-SDK-backed
service class and its DI wiring from the Application ring to the Azure adapter ring. There are
no new or changed UI components, screens, layouts, or visual decisions. Behavior is unchanged.

## Architectural Fit Assessment

The proposed change is a **strong fit** and, in fact, corrects an existing deviation. The
codebase already encodes the target pattern:

- `filesystem.md` states the **I/O placement rule** verbatim: "Concrete `IPrintQueueSink`
  implementations and any I/O-bound service live in adapter projects under
  `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."
- `AzureBlobPrintQueueSink` (an Azure-SDK-backed I/O service, functionally the same shape as
  `AzureBlobStorageService`) already lives correctly at
  `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/`, with its DI in
  `AzureAdapterModule.AddAzurePrintQueueSink`.

`AzureBlobStorageService` is the lone outlier: it sits in the Application ring and forces
`Anela.Heblo.Application.csproj` to reference `Azure.Storage.Blobs` (line 12). The domain
abstraction (`IBlobStorageService`) and its DTO (`BlobItemInfo`) already live in
`Anela.Heblo.Domain/Features/FileStorage/` with no Azure dependency, and all consumers depend
only on the abstraction. Moving the concrete class is therefore purely mechanical from the
consumers' perspective.

**Integration points verified in the codebase:**

| Concern | Current location | Verified fact |
|---|---|---|
| Concrete service | `Application/Features/FileStorage/Services/AzureBlobStorageService.cs` | `sealed`, ctor deps: `BlobServiceClient`, `IHttpClientFactory`, `ILogger<AzureBlobStorageService>` |
| Abstraction | `Domain/Features/FileStorage/IBlobStorageService` | No Azure dependency; unaffected |
| Azure DI so far | `FileStorageModule` lines 42–57 (`BlobServiceClient`), line 84 (`IBlobStorageService`) | `using Azure.Storage.Blobs;` at line 5 |
| Adapter DI home | `AzureAdapterModule.AddAzurePrintQueueSink` | Public static extension; adapter references Application |
| Adapter project | `Anela.Heblo.Adapters.Azure.csproj` | Already references `Azure.Storage.Blobs` 12.25.0 and `Anela.Heblo.Application` |
| Named HTTP client constant | `FileStorageModule.FileDownloadClientName` = `"FileDownload"` | Stays in Application; adapter references it (adapter → Application ref exists) |
| Startup composition | `ApplicationModule.AddApplicationServices` line 85 calls `AddFileStorageModule`; `Program.cs` line 103 calls `AddApplicationServices`, line 132 calls `AddPrintQueueSink` | See Decision 2 — composition constraint |

## Proposed Architecture

### Component Overview

```
Domain (inner ring)
  └── Features/FileStorage/
        ├── IBlobStorageService        (abstraction — UNCHANGED, stays)
        └── BlobItemInfo               (DTO — UNCHANGED, stays)

Application (middle ring)  — loses its Azure.Storage.Blobs dependency
  └── Features/FileStorage/
        ├── FileStorageModule          (KEEPS: options bind + ValidateOnStart,
        │                                named "FileDownload" HttpClient,
        │                                IDownloadResilienceService, FileDownloadOptions.
        │                                LOSES: BlobServiceClient factory + IBlobStorageService bind)
        ├── FileStorageOptions         (UNCHANGED, stays — read by adapter via IOptions<>)
        ├── FileDownloadOptions        (UNCHANGED, stays)
        └── Infrastructure/DownloadResilienceService (UNCHANGED, stays)

Adapters.Azure (outer ring)  — sole holder of Azure.Storage.Blobs
  ├── Features/FileStorage/
  │     └── AzureBlobStorageService    (MOVED here; ns → Adapters.Azure.Features.FileStorage)
  └── AzureAdapterModule
        ├── AddAzurePrintQueueSink     (UNCHANGED)
        └── AddAzureBlobStorageService (NEW: BlobServiceClient factory + IBlobStorageService bind)

API (composition root)
  └── Program.cs                       (NEW call: builder.Services.AddAzureBlobStorageService(builder.Environment))
```

Data-flow arrows are unchanged at runtime: consumers → `IBlobStorageService` →
`AzureBlobStorageService` → `BlobServiceClient` → Azure. Only the *compile-time* reference graph
changes: `Azure.Storage.Blobs` leaves the Application ring entirely.

### Key Design Decisions

#### Decision 1: One new adapter extension method, mirroring `AddAzurePrintQueueSink`

**Options considered:**
- (a) Add `AddAzureBlobStorageService` to `AzureAdapterModule` (mirrors the print-queue precedent).
- (b) Fold the blob-storage registration into the existing `AddAzurePrintQueueSink`.
- (c) Share a single `BlobServiceClient` between the print sink and the blob service.

**Chosen approach:** (a) — a dedicated `AddAzureBlobStorageService` extension on
`AzureAdapterModule`.

**Rationale:** The print-queue sink registers a `BlobContainerClient` built from
`PrintPickingListOptions`; the blob service needs a `BlobServiceClient` built from
`FileStorageOptions`. They are different SDK client types, different options, different
lifecycles, and are wired at different points in startup (`AddAzurePrintQueueSink` is invoked
*conditionally* from inside `AddPrintQueueSink`'s `switch`, only for `"AzureBlob"`/`"Combined"`).
Folding them together (b) or sharing a client (c) would couple two independent concerns and
change behavior — both are explicitly out of scope per the spec. A separate extension keeps each
concern self-contained and matches the one-extension-per-Azure-concern convention already present.

#### Decision 2: Wire the new extension from the API composition root (`Program.cs`), unconditionally — NOT from `ApplicationModule` and NOT inside `AddPrintQueueSink`

**Options considered:**
- (a) Call `AddAzureBlobStorageService` from `ApplicationModule.AddApplicationServices` (next to `AddFileStorageModule`).
- (b) Call it from inside `AddPrintQueueSink`'s `switch` (next to `AddAzurePrintQueueSink`).
- (c) Call it unconditionally from `Program.cs` (the composition root), after `AddApplicationServices`.

**Chosen approach:** (c).

**Rationale:**
- (a) is **impossible**: `ApplicationModule` lives in `Anela.Heblo.Application`, the middle ring.
  The Azure adapter references Application, not the reverse — Application cannot see
  `AzureAdapterModule`. Placing the call there would create a reference cycle and defeat the
  entire purpose of the refactor. The daily-review "suggested fix" note in `brief.md` mentions
  `ApplicationModule`, but that is not achievable without inverting the dependency; the spec's own
  Open-Questions section already corrects this to "the API composition root."
- (b) would make the `IBlobStorageService` binding **conditional** on `ExpeditionList:PrintSink`.
  `AddAzurePrintQueueSink` runs only for `"AzureBlob"`/`"Combined"`. Blob storage is consumed by
  `DownloadFromUrlHandler` and the `ExpeditionListArchive` handlers regardless of the print sink,
  so the binding must always be present. Today it is unconditional (registered by
  `FileStorageModule`, which always runs); (b) would silently regress environments using
  `"FileSystem"`/`"Cups"`. Rejected.
- (c) preserves "always present in every environment that had it before." `Program.cs` already
  has `using Anela.Heblo.Adapters.Azure;` (line 5) and the API project already references the
  adapter (`.csproj` line 68), so the call site compiles with no new wiring. Registration order
  relative to `AddApplicationServices` is irrelevant because the `BlobServiceClient` factory
  resolves `IOptions<FileStorageOptions>` lazily at first resolve, long after all `Add*` calls
  complete.

#### Decision 3: The new extension takes `IHostEnvironment`, not `IConfiguration`

**Options considered:**
- (a) `AddAzureBlobStorageService(this IServiceCollection, IConfiguration)` (mirrors `AddAzurePrintQueueSink`'s signature).
- (b) `AddAzureBlobStorageService(this IServiceCollection, IHostEnvironment)`.
- (c) Resolve `IHostEnvironment` from the provider inside the factory.

**Chosen approach:** (b).

**Rationale:** The `BlobServiceClient` factory must preserve its exact current behavior,
including the Development-only warning message that interpolates `environment.EnvironmentName`
("...is empty in {Environment}; falling back to UseDevelopmentStorage=true."). The options are
already bound and validated by `FileStorageModule`, so the extension needs **no** `IConfiguration`
— it only reads `IOptions<FileStorageOptions>`. It does need the environment name for the log
message, so it must receive `IHostEnvironment`. This matches how `AddFileStorageModule` itself
already takes `IHostEnvironment`. Option (c) (resolve from provider) works in the API host but
forces the unit test to register a mock `IHostEnvironment`; passing it explicitly keeps the
closure-capture pattern byte-for-byte identical to today's factory and is the smallest behavioral
delta. This is a deliberate amendment to the spec's stated `(IServiceCollection, IConfiguration)`
signature (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure

**Move (git mv to preserve history):**
```
FROM: backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
TO:   backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs
```
- Change namespace to `Anela.Heblo.Adapters.Azure.Features.FileStorage`.
- Add `using Anela.Heblo.Application.Features.FileStorage;` so the reference to
  `FileStorageModule.FileDownloadClientName` still resolves (do **not** duplicate the `"FileDownload"`
  literal). Keep `using Anela.Heblo.Domain.Features.FileStorage;` for `IBlobStorageService` /
  `BlobItemInfo`. Keep the `Azure.Storage.Blobs` / `Azure.Storage.Blobs.Models` usings.
- All method bodies, the private `BlobDownloadStream`, the `_containerExists` cache, and the two
  content-type helper methods move **verbatim**. Do not touch logic.

**Edit `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`:**
- Add a second public static extension `AddAzureBlobStorageService`.
- Add `using Anela.Heblo.Adapters.Azure.Features.FileStorage;`,
  `using Anela.Heblo.Application.Features.FileStorage;`,
  `using Anela.Heblo.Domain.Features.FileStorage;`,
  `using Microsoft.Extensions.Hosting;`, `using Microsoft.Extensions.Logging;`.

**Edit `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`:**
- Remove the `BlobServiceClient` factory (lines 42–57), the
  `AddSingleton<IBlobStorageService, AzureBlobStorageService>()` binding (line 84), the
  `using Azure.Storage.Blobs;` (line 5), and the now-unused
  `using ...Features.FileStorage.Services;` (line 3, unless still referenced — it will no longer be).
- **Keep** everything else: the `FileStorageOptions` options binding, the non-Development
  `.Validate(...).ValidateOnStart()`, the `FileDownload` named `HttpClient` with its
  `SocketsHttpHandler`/infinite-timeout config, `IDownloadResilienceService`, and the
  `FileDownloadOptions` binding. The `FileDownloadClientName` const stays here.

**Edit `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`:**
- Remove line 12: `<PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />`.

**Edit `backend/src/Anela.Heblo.API/Program.cs`:**
- After the `AddApplicationServices` call (line 103), add an unconditional
  `builder.Services.AddAzureBlobStorageService(builder.Environment);`. `using Anela.Heblo.Adapters.Azure;`
  already exists (line 5).

### Interfaces and Contracts

New extension method (target shape):

```csharp
// AzureAdapterModule.cs
public static IServiceCollection AddAzureBlobStorageService(
    this IServiceCollection services,
    IHostEnvironment environment)
{
    // BlobServiceClient factory — moved verbatim from FileStorageModule lines 42–57.
    // Reads the already-bound IOptions<FileStorageOptions> (binding + ValidateOnStart
    // stay in FileStorageModule, which always runs before first resolve).
    services.AddSingleton<BlobServiceClient>(provider =>
    {
        var opts = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
        if (string.IsNullOrWhiteSpace(opts.BlobConnectionString))
        {
            var logger = provider.GetRequiredService<ILogger<AzureBlobStorageService>>();
            logger.LogWarning(
                "FileStorage:BlobConnectionString is empty in {Environment}; falling back to UseDevelopmentStorage=true.",
                environment.EnvironmentName);
            return new BlobServiceClient("UseDevelopmentStorage=true");
        }
        return new BlobServiceClient(opts.BlobConnectionString);
    });

    services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
    return services;
}
```

**Contracts that must NOT change:** `IBlobStorageService` (all eight method signatures),
`BlobItemInfo`, `FileStorageOptions` (incl. `BlobConnectionString` and `SectionName`),
`FileDownloadOptions`, and `FileStorageModule.FileDownloadClientName` (= `"FileDownload"`).

### Data Flow

Runtime path is identical before and after:

1. Startup: `FileStorageModule` binds + validates `FileStorageOptions`, registers the
   `FileDownload` `HttpClient` and `IDownloadResilienceService`.
   `AddAzureBlobStorageService` registers the `BlobServiceClient` singleton factory and binds
   `IBlobStorageService → AzureBlobStorageService`.
2. First resolve of `IBlobStorageService` triggers the factory, which reads the validated
   options (fail-fast already guaranteed by `ValidateOnStart` in non-Development).
3. `DownloadFromUrlHandler` / `ExpeditionListArchive` handlers inject `IBlobStorageService` and
   call it exactly as today; the service uses the named `FileDownload` client and the
   `BlobServiceClient` as before. Singleton lifetimes (client, service, resilience) preserve the
   `_containerExists` cache and socket pooling.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Wiring placed in `ApplicationModule`, creating an Application→Adapter reference cycle | High | Decision 2: wire from `Program.cs` (composition root) only. Application must never reference the adapter. |
| Blob binding made conditional (e.g. added inside `AddPrintQueueSink`'s `switch`), regressing `FileSystem`/`Cups` environments | High | Decision 2: the `AddAzureBlobStorageService` call is unconditional in `Program.cs`, matching today's always-on registration by `FileStorageModule`. |
| `FileStorageModuleTests` still assert blob/`BlobServiceClient` registration on `FileStorageModule` → compile/assert failures | Medium | Move the three affected tests to a new `AzureAdapterModule` test (see Amendment 4). The three: `RegistersBlobStorageService_AsSingleton`, `ResolvingBlobStorageServiceTwice_ReturnsSameInstance`, `DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning`. |
| New adapter test resolves `BlobServiceClient`/`IBlobStorageService` but options binding now lives elsewhere → missing `IOptions<FileStorageOptions>` | Medium | The adapter-module test must call **both** `AddFileStorageModule(config, env)` (for the options binding + validation) **and** `AddAzureBlobStorageService(env)`. Document this coupling in the test. |
| Warning-message text drift (`{Environment}` interpolation) breaks the dev-fallback assertion | Low | Decision 3: pass `IHostEnvironment` to the extension and keep the factory body verbatim; the log message string is unchanged. |
| `AzureBlobStorageServiceTests` fails to compile after the type moves | Low | Only the `using`/namespace reference to the service changes (test constructs the type directly, no DI). Update `using Anela.Heblo.Application.Features.FileStorage.Services;` → `using Anela.Heblo.Adapters.Azure.Features.FileStorage;`. The test project already references the adapter (`.csproj` line 50). |
| Stray `Azure.Storage` references left in Application after the move | Low | FR-4 acceptance: repo-wide grep for `Azure.Storage` / `BlobServiceClient` / `BlobContainerClient` under `Anela.Heblo.Application` must return nothing; `dotnet build` then proves the package removal is safe. |

## Specification Amendments

1. **Amend FR-2 / API-Interface-Design signature.** The new extension should be
   `AddAzureBlobStorageService(this IServiceCollection services, IHostEnvironment environment)` —
   **not** `(IServiceCollection, IConfiguration)`. The factory needs `environment.EnvironmentName`
   for the fallback warning; it does **not** need `IConfiguration` because the `FileStorageOptions`
   binding/validation stays in `FileStorageModule` and the factory reads `IOptions<FileStorageOptions>`.
   (Rationale in Decision 3.)

2. **Amend FR-3 / the `brief.md` "suggested fix" step 5.** The registration must be invoked from
   the **API composition root (`Program.cs`)**, not from `ApplicationModule`. `ApplicationModule`
   is in the Application ring and cannot reference the adapter without inverting the dependency —
   the very coupling this refactor removes. Concretely: add
   `builder.Services.AddAzureBlobStorageService(builder.Environment);` in `Program.cs` immediately
   after the existing `AddApplicationServices(...)` call (~line 103). The spec's Open-Questions
   already lands on "the API composition root"; this amendment makes it binding and rejects the
   `ApplicationModule` option outright.

3. **Clarify FR-3: the call is unconditional and independent of `AddPrintQueueSink`.** Do not add
   it inside `AddPrintQueueSink`'s `switch` (that path is conditional on
   `ExpeditionList:PrintSink`). Blob storage is consumed regardless of the print sink and must
   always be registered.

4. **Amend FR-5 with the precise test deltas discovered in the source:**
   - `AzureBlobStorageServiceTests.cs`: update the two `using` lines (drop
     `...Application.Features.FileStorage.Services`, add `...Adapters.Azure.Features.FileStorage`);
     keep the reference to `FileStorageModule.FileDownloadClientName` (needs
     `using Anela.Heblo.Application.Features.FileStorage;`, already present). No logic changes.
   - `FileStorageModuleTests.cs`: **three** methods target the moved registrations and must be
     relocated to a new `AzureAdapterModuleTests` (or equivalent adapter-module test):
     `AddFileStorageModule_RegistersBlobStorageService_AsSingleton`,
     `AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance`, and
     `AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning` (this last
     one resolves `BlobServiceClient` and verifies the warning). The relocated tests must call
     **both** `AddFileStorageModule(config, env)` (for the options binding) **and**
     `AddAzureBlobStorageService(env)`. The remaining `FileStorageModuleTests` (named `HttpClient`,
     resilience service, options validation, constant export) stay as-is and must still pass —
     they assert only the non-Azure registrations that remain in `FileStorageModule`.

5. **Non-scope confirmation retained.** `AzureBlobConflictTelemetryFilter` references
   `AzureBlobStorageService` only in a doc comment; per the spec no code change is required. If the
   comment's implied namespace becomes stale, an optional one-line comment touch-up is acceptable
   but out of scope.

## Prerequisites

None beyond the existing codebase. Specifically verified:

- `Anela.Heblo.Adapters.Azure.csproj` **already** references `Azure.Storage.Blobs` 12.25.0 and
  `Anela.Heblo.Application` — no new package or project references needed in the adapter.
- `Anela.Heblo.API.csproj` (line 68) and `Program.cs` (`using`, line 5) **already** wire in the
  Azure adapter — the new `Program.cs` call compiles without additional references.
- `Anela.Heblo.Tests.csproj` (line 50) **already** references the Azure adapter — the relocated
  tests need no new project reference.
- No migrations, config keys, or Key Vault secrets are introduced. `FileStorage:BlobConnectionString`
  already exists in all non-Development environments (documented in `environments.md`); its sourcing,
  validation timing, and the Development fallback are all preserved unchanged.

Validation gate before completion (per CLAUDE.md / NFR-3): `dotnet build` succeeds,
`dotnet format` clean, and the full backend test suite (including the relocated FileStorage tests)
passes.
