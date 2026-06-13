I have full context now. The spec underestimates the coupling — `DownloadFromUrlHandler`, `AzureBlobStorageService`, and a named HttpClient registration all reference `ProductExportOptions` / `ProductExportDownloadClientName` from inside FileStorage. A naive move breaks compilation. Writing the architecture review now.

# Architecture Review: Relocate ProductExportDownloadJob from FileStorage to Catalog Module

## Skip Design: true

Backend-only structural refactor. No UI, no visual components, no design surface.

## Architectural Fit Assessment

The spec's stated goal — restore SRP for `FileStorage` so it owns only generic blob transport — is correct, and the chosen direction (relocate to `Catalog`) matches existing precedent: `Features/Catalog/Infrastructure/Jobs/ProductWeightRecalculationJob.cs` already exists with the same Hangfire `IRecurringJob` pattern. The destination convention is therefore unambiguous.

However, **the spec materially underestimates the FileStorage→ProductExport coupling.** Beyond the two files named in the brief, three additional integration points reference product-export concerns from inside FileStorage:

1. **`DownloadFromUrlHandler` (FileStorage Use Case)** injects `IOptions<ProductExportOptions>` and reads `HeadTimeout` for its HEAD probe (line 21, 141). Moving `ProductExportOptions` to Catalog while keeping the handler in FileStorage either:
   - breaks compilation, or
   - forces Catalog to be a dependency of FileStorage (wrong direction — `FileStorage` must stay leaf-generic).
2. **`AzureBlobStorageService` (FileStorage Service)** resolves the named HttpClient via `FileStorageModule.ProductExportDownloadClientName` constant (line 38). This is product-export naming bleeding into a generic blob service.
3. **`FileStorageModule.ProductExportDownloadClientName = "ProductExportDownload"`** is a public constant on the FileStorage module surface, consumed by `DownloadFromUrlHandler`, `AzureBlobStorageService`, and three test files.

The spec's FR-1/FR-2/FR-3 alone are insufficient. Implementing them as written will not compile, or will leave FileStorage even more polluted than before because it will still own a `ProductExport*` constant referenced from Catalog.

The fix requires a small extension of scope: split `ProductExportOptions` into a Catalog-owned domain options class and a FileStorage-owned generic download options class, and rename the named HttpClient to neutral terminology. With that, the relocation becomes clean and the module boundary holds.

Additionally:

- **Configuration section is already neutral** (`ProductExportOptions:` at root, not under `FileStorage:`). FR-4's rename concern is largely moot — the section name does not need to change.
- **Key Vault secret names use the root path** (`ProductExportOptions--Url` style, not `FileStorage--ProductExport--Url`). No KV migration is required.
- **Tests already exist** for the job (`ProductExportDownloadJobTests.cs`) — FR-7 is non-trivial and needs an explicit move.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Features/Catalog (DOMAIN owner of product export)           │
│  ├─ Infrastructure/Jobs/ProductExportDownloadJob.cs   [NEW] │
│  │     - reads ProductExportOptions (Url, ContainerName)    │
│  │     - sends DownloadFromUrlRequest via IMediator         │
│  └─ Infrastructure/ProductExportOptions.cs            [NEW] │
│        - Url, ContainerName  (domain-specific only)         │
│                                                             │
│  CatalogModule.AddCatalogModule(cfg):                       │
│   • services.Configure<ProductExportOptions>(...)           │
│   • job auto-registered via IRecurringJob scan              │
└─────────────────────────────────────────────────────────────┘
                       │
                       │ depends on  IMediator + DownloadFromUrlRequest
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Features/FileStorage (GENERIC blob transport)               │
│  ├─ FileDownloadOptions.cs                            [NEW] │
│  │     - HeadTimeout, DownloadTimeout,                      │
│  │       MaxRetryAttempts, RetryBaseDelay                   │
│  ├─ UseCases/DownloadFromUrl/                               │
│  │     DownloadFromUrlHandler.cs                  [MODIFY]  │
│  │     - now depends on IOptions<FileDownloadOptions>       │
│  ├─ Services/AzureBlobStorageService.cs            [MODIFY] │
│  │     - uses neutral client name                           │
│  └─ FileStorageModule.cs                            [MODIFY]│
│        - constant renamed: FileDownloadClientName           │
│          = "FileDownload"                                   │
│        - services.Configure<FileDownloadOptions>(...)       │
│        - no product-export references anywhere              │
└─────────────────────────────────────────────────────────────┘
                       │
                       │ Domain abstraction (unchanged)
                       ▼
              IBlobStorageService (Domain.Features.FileStorage)
```

### Key Design Decisions

#### Decision 1: Split `ProductExportOptions` into two options classes

**Options considered:**
- (a) Move `ProductExportOptions` entirely to Catalog and let `DownloadFromUrlHandler` keep depending on it → forces FileStorage → Catalog reference (cycle).
- (b) Leave `ProductExportOptions` in FileStorage and only move the job → leaves the SRP violation in place; PR misses its goal.
- (c) Split into `ProductExportOptions` (Catalog: `Url`, `ContainerName`) and `FileDownloadOptions` (FileStorage: `HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`).

**Chosen approach:** (c).

**Rationale:** The four timeout/retry properties are not product-export concerns — they configure the FileStorage `DownloadFromUrlHandler` and the FileStorage `DownloadResilienceService`. They belong in FileStorage. The two domain properties (`Url`, `ContainerName`) belong in Catalog with the job. This is the only split that lets each module own only its own configuration without introducing a cycle.

#### Decision 2: Rename `ProductExportDownloadClientName` to a neutral `FileDownloadClientName`

**Options considered:**
- (a) Keep the constant name in FileStorage → product-export terminology stays in generic code.
- (b) Move the HttpClient registration to Catalog and pass the client name through `DownloadFromUrlRequest` → expands the request contract for one consumer; couples FileStorage handler to a Catalog-owned named client at runtime.
- (c) Rename the constant and the client to neutral terminology; keep HttpClient registration in FileStorage as a generic resource.

**Chosen approach:** (c). New constant: `FileStorageModule.FileDownloadClientName = "FileDownload"`.

**Rationale:** The named HttpClient configures `SocketsHttpHandler` + `PooledConnectionLifetime` + `AutomaticDecompression` — all generic. The "ProductExport" name was historical. Renaming aligns the constant with what it actually is and removes the last domain leak from FileStorage. Test fixtures using the old constant migrate trivially.

#### Decision 3: Place new files at `Features/Catalog/Infrastructure/Jobs/` and `Features/Catalog/Infrastructure/`

**Options considered:**
- (a) New `Features/Catalog/ProductExport/` sub-feature.
- (b) Mirror existing convention: `Features/Catalog/Infrastructure/Jobs/`.

**Chosen approach:** (b).

**Rationale:** `ProductWeightRecalculationJob.cs` already lives at `Features/Catalog/Infrastructure/Jobs/`. Creating a `ProductExport/` sub-feature for two files violates the principle of matching existing convention and creates inconsistency. If/when product-export grows handlers, contracts, services, then promote to a sub-feature. YAGNI now.

#### Decision 4: Configuration section stays at root `ProductExportOptions:`

**Options considered:**
- (a) Rename to `Catalog:ProductExport:`.
- (b) Keep as-is.

**Chosen approach:** (b).

**Rationale:** The section is already neutral — it is not under `FileStorage:`. FR-4's premise (config rooted under FileStorage namespace) does not apply here. Renaming buys nothing operationally and would require Key Vault migration. Zero-risk path: keep `ProductExportOptions:` at the root. The `FileDownloadOptions` will get its own root section `FileStorage:Download:` (new).

#### Decision 5: Job auto-registers via existing `IRecurringJob` discovery

**Verify:** Confirm Catalog or the host registers recurring jobs via assembly scan of `IRecurringJob`. If `ProductExportDownloadJob` is currently explicitly registered in FileStorage, replicate that explicit registration in `CatalogModule`. If it is scan-discovered (more likely, given `ProductWeightRecalculationJob` has no explicit registration in `CatalogModule.cs`), no `CatalogModule` change is needed for the job itself — only the options binding moves.

**Rationale:** Match existing pattern; do not introduce explicit registration where convention is scan-based.

## Implementation Guidance

### Directory / Module Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`
  - Namespace: `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs`
  - Namespace: `Anela.Heblo.Application.Features.Catalog.Infrastructure`
  - Properties: **only** `Url` (string), `ContainerName` (string)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileDownloadOptions.cs`
  - Namespace: `Anela.Heblo.Application.Features.FileStorage`
  - Properties: `HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay` (carried over verbatim from current `ProductExportOptions`)

**Delete:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`

**Modify:**
- `Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
  - Change `IOptions<ProductExportOptions>` → `IOptions<FileDownloadOptions>`.
  - Update `_options.Value.HeadTimeout` reference (line 141) — same property name, new options type.
  - Replace `FileStorageModule.ProductExportDownloadClientName` references (lines 95, 144) with `FileStorageModule.FileDownloadClientName`.
- `Features/FileStorage/Services/AzureBlobStorageService.cs`
  - Replace `FileStorageModule.ProductExportDownloadClientName` (line 38) with `FileStorageModule.FileDownloadClientName`.
- `Features/FileStorage/FileStorageModule.cs`
  - Rename constant `ProductExportDownloadClientName` → `FileDownloadClientName`; value `"ProductExportDownload"` → `"FileDownload"`.
  - Replace the line `services.AddHttpClient(ProductExportDownloadClientName)` with the new name.
  - **Add:** `services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));` — bind new section (or default-bind if section missing).
- `Features/Catalog/CatalogModule.cs`
  - Add: `services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));` — moved from `ServiceCollectionExtensions.cs`.
- `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
  - Remove line 365: `services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));`.
  - Remove line 28: `using Anela.Heblo.Application.Features.FileStorage;` (only if no other use; verify).
- `Anela.Heblo.API/appsettings.json`
  - Keep existing `"ProductExportOptions": { "Url": "" }` block as-is.
  - **Add** (optional, only if non-default values needed) a new `"FileStorage": { "Download": { ... } }` block. If defaults are fine, omit — `FileDownloadOptions` defaults are baked in.

**Tests (FR-7):**
- Move `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` → `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`. Update namespace.
- Move `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs` → `backend/test/Anela.Heblo.Tests/Features/Catalog/Configuration/ProductExportOptionsTests.cs`. Update namespace and delete any test assertions that reference moved-away timeout/retry properties (now on `FileDownloadOptions`).
- **Split** `ProductExportOptionsTests.cs`: assertions about `HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay` move to a new `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/FileDownloadOptionsTests.cs`.
- Update `FileStorageModuleTests.cs`, `AzureBlobStorageServiceTests.cs`, `DownloadFromUrlHandlerTests.cs` to use `FileDownloadClientName` constant and `FileDownloadOptions` type.

### Interfaces and Contracts

No public contract changes. Internal contract changes:

| Type | Before | After |
|------|--------|-------|
| `ProductExportOptions` | `Anela.Heblo.Application.Features.FileStorage.ProductExportOptions` with 6 properties | `Anela.Heblo.Application.Features.Catalog.Infrastructure.ProductExportOptions` with 2 properties (`Url`, `ContainerName`) |
| `FileDownloadOptions` *(new)* | n/a | `Anela.Heblo.Application.Features.FileStorage.FileDownloadOptions` with 4 properties (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`) |
| `FileStorageModule.ProductExportDownloadClientName` | `"ProductExportDownload"` | renamed to `FileDownloadClientName` = `"FileDownload"` |
| `ProductExportDownloadJob` | `Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs` | `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs` |

`IBlobStorageService` (Domain) — unchanged.
`DownloadFromUrlRequest` / `DownloadFromUrlResponse` (FileStorage MediatR contracts) — unchanged.
`IRecurringJob` (Domain.Features.BackgroundJobs) — unchanged.

### Data Flow

For the daily product-export run:

```
Hangfire scheduler (cron "0 2 * * *")
    │
    ▼
ProductExportDownloadJob.ExecuteAsync           [Catalog.Infrastructure.Jobs]
    │  reads IOptions<ProductExportOptions>     [Catalog.Infrastructure]
    │  → Url, ContainerName
    │  builds fileName = "products_{timestamp}.csv"
    │
    ▼
IMediator.Send(DownloadFromUrlRequest)          [FileStorage MediatR contract]
    │
    ▼
DownloadFromUrlHandler.Handle                   [FileStorage.UseCases.DownloadFromUrl]
    │  reads IOptions<FileDownloadOptions>      [FileStorage]
    │  → HeadTimeout, DownloadTimeout, retry policy
    │  HEAD probe via "FileDownload" HttpClient
    │  → IBlobStorageService.DownloadFromUrlAsync
    │
    ▼
AzureBlobStorageService                          [FileStorage.Services]
    │  resolves "FileDownload" HttpClient
    │  streams body to Azure Blob container
    │
    ▼
Blob: <container>/products_{timestamp}.csv
```

Module-direction invariant: **Catalog → FileStorage only (via MediatR + Domain abstraction). FileStorage knows nothing about Catalog.** After the refactor, this holds cleanly.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Naive move per spec FR-1/FR-2 alone breaks compilation because `DownloadFromUrlHandler` references `ProductExportOptions` | **High** | Implement Decision 1 (split options) and Decision 2 (rename HttpClient constant) as part of this PR. Spec amendments below codify these. |
| Renaming the named HttpClient breaks any external code path or in-flight request that resolves it by string | Low | Single repo, all references already inventoried (4 files in src, 3 test files). Solution-wide replace is safe; `dotnet build` will catch any miss. |
| Hangfire recurring-job state under the old job name persists in storage if the `JobName` metadata changes | Low | **Do not change** `Metadata.JobName = "product-export-download"` — it's the Hangfire identity. Namespace change does not affect Hangfire's job key. Verify in test. |
| `ILogger<T>` category name changes from `FileStorage.Infrastructure.Jobs.ProductExportDownloadJob` to `Catalog.Infrastructure.Jobs.ProductExportDownloadJob` may break log queries | Low | Confirmed in spec NFR-4 / open question 3 as accepted. Note in PR description for ops awareness. |
| `ProductExportOptions` split could miss a downstream consumer that reads the old combined shape | Medium | Solution-wide grep confirms only `DownloadFromUrlHandler.HeadTimeout` consumes the timeout properties outside the job itself. Job consumes only `Url` + `ContainerName`. Mitigation is explicit: grep `_options.Value\.` against the original class to enumerate all property reads. |
| `IRecurringJob` registration assumption (scan vs explicit) may be wrong, causing the job to not run after move | Medium | Before deleting the old file, verify how `ProductWeightRecalculationJob` is wired (likely scan via `IRecurringJob` discovery in the host). If explicit, replicate explicit registration in `CatalogModule`. Smoke test: start API in Development, confirm the job appears in Hangfire dashboard. |
| Tests that previously asserted timeout properties on `ProductExportOptions` will fail when properties move to `FileDownloadOptions` | Low | Split `ProductExportOptionsTests.cs` accordingly (see implementation guidance). |
| `appsettings.json` has `ProductExportOptions` listed but no top-level `FileStorage:Download` section — defaults must be safe | Low | The four timeout/retry defaults in current `ProductExportOptions` are already correct production values; preserve them as defaults on `FileDownloadOptions`. No appsettings change required for `FileDownloadOptions` unless tuning. |

## Specification Amendments

The spec must be updated to reflect the broader coupling. Recommended amendments:

**Amend FR-2 (relocate `ProductExportOptions`):** Split the class. Move only `Url` and `ContainerName` to Catalog as the new `ProductExportOptions`. Move `HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay` into a new FileStorage-owned `FileDownloadOptions` class. Update `DownloadFromUrlHandler` constructor and all references accordingly. Update split tests.

**Add FR-2a (rename FileStorage named HttpClient constant):** Rename `FileStorageModule.ProductExportDownloadClientName` to `FileStorageModule.FileDownloadClientName`, value from `"ProductExportDownload"` to `"FileDownload"`. Update `DownloadFromUrlHandler`, `AzureBlobStorageService`, and three test files. This completes the SRP cleanup of FileStorage.

**Amend FR-3 (DI registration move):** The current registration of `ProductExportOptions` lives in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` line 365 (not in `FileStorageModule.cs`). Move that line into `CatalogModule.AddCatalogModule`. `FileStorageModule` gains a new `services.Configure<FileDownloadOptions>(...)` line. Confirm whether `ProductExportDownloadJob` is currently explicitly registered or scan-discovered; replicate the same pattern in Catalog.

**Resolve FR-4 (config section rename):** Not required. The current section path `ProductExportOptions:` is at the config root, not under `FileStorage:`. No appsettings rename, no Key Vault migration needed. Optionally add a new `FileStorage:Download` section for `FileDownloadOptions` if defaults need to be tuned per environment; otherwise omit and let class defaults apply.

**Confirm Open Question 3 (log category):** Accepted as-is. Category name changes from `Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs.ProductExportDownloadJob` to `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs.ProductExportDownloadJob`. PR description must note this for ops; no log query is known to depend on the old category in this solo-dev project.

**Resolve Open Question 4 (tests):** Tests exist at `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` and `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`. Both move to corresponding Catalog test paths; `ProductExportOptionsTests` is split (timeout/retry assertions migrate to a new `FileDownloadOptionsTests.cs` in FileStorage tests).

**Resolve Open Question 5 (other domain leaks in FileStorage):** Inventory complete. After this PR, FileStorage will contain: `IBlobStorageService` impl, `DownloadFromUrl` use case, `DownloadResilienceService`, `FileDownloadOptions`, named HttpClient `"FileDownload"`. All generic. No further moves recommended in this PR.

## Prerequisites

None. All changes are within a single solution, no infrastructure, no migration, no external coordination required.

- No DB migrations.
- No Key Vault secret renames (section name unchanged).
- No external API contract changes.
- No Azure resource changes (blob container name preserved via `ProductExportOptions.ContainerName` value).
- No CI/CD pipeline changes.

Smoke check before merge: start the API locally in Development, open the Hangfire dashboard, confirm `product-export-download` recurring job is registered with the same `JobName` and cron expression. Run `dotnet build` and `dotnet test` — both must pass clean.