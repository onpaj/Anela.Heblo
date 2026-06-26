# Specification: Relocate ProductExportDownloadJob from FileStorage to Catalog Module

## Summary
Move `ProductExportDownloadJob` and `ProductExportOptions` out of the `FileStorage` module and into the `Catalog` module, which owns the product-export domain. This restores Single Responsibility for the `FileStorage` module (generic blob transport) and consolidates Shoptet/Catalog product-export business logic in the module that owns it. No behavior changes — pure relocation and DI re-wiring.

## Background
A daily architecture review (2026-06-05) flagged that `ProductExportDownloadJob` and its `ProductExportOptions` live under `backend/src/Anela.Heblo.Application/Features/FileStorage/`. The job's logic is entirely product-export domain:

- It knows the Shoptet product-export URL (`ProductExportOptions.Url`).
- It targets a specific product-data blob container (`ProductExportOptions.ContainerName`).
- It enforces a product-export filename convention (`products_{timestamp}.csv`).

None of this is generic file-storage infrastructure. The job uses `IBlobStorageService` only as a transport. Hosting the job in `FileStorage` means:

1. **SRP violation** — the `FileStorage` module now has two unrelated reasons to change: blob storage mechanics, and product-export scheduling/naming/URL rules.
2. **Namespace pollution** — `ProductExportOptions` sits in the root namespace of what should be a generic infrastructure module, leaking domain configuration into infrastructure.
3. **Future drift risk** — if more domain-specific jobs accumulate here, `FileStorage` becomes a junk drawer rather than a stable, reusable infrastructure module.

The fix is structural: relocate the two files to the `Catalog` module (or its product-export sub-feature), update DI registration, and leave logic untouched. `IBlobStorageService` continues to be consumed cross-module via its Domain interface — the existing and correct cross-module pattern.

## Functional Requirements

### FR-1: Relocate ProductExportDownloadJob to the Catalog module
Move `ProductExportDownloadJob.cs` from `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/` to an appropriate location inside `backend/src/Anela.Heblo.Application/Features/Catalog/` that mirrors the Catalog module's existing layout for background jobs / infrastructure (e.g. `Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`).

**Acceptance criteria:**
- The file no longer exists under `Features/FileStorage/Infrastructure/Jobs/`.
- The file exists under `Features/Catalog/` in the location that matches Catalog's existing folder convention for jobs/infrastructure.
- The class namespace is updated to match its new folder (e.g. `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs`).
- The class body, including dependencies, scheduling, blob writes, and filename format `products_{timestamp}.csv`, is byte-for-byte unchanged apart from `using` directives and the namespace declaration.

### FR-2: Relocate ProductExportOptions to the Catalog module
Move `ProductExportOptions.cs` from `backend/src/Anela.Heblo.Application/Features/FileStorage/` to the Catalog module, alongside or near `ProductExportDownloadJob`. It must no longer sit in the FileStorage root namespace.

**Acceptance criteria:**
- The file no longer exists under `Features/FileStorage/`.
- The file exists in the Catalog module at a location consistent with how other option/config classes are placed there (e.g. `Features/Catalog/ProductExportOptions.cs` or `Features/Catalog/Infrastructure/ProductExportOptions.cs`).
- Namespace updated to a `Catalog`-rooted namespace.
- The class definition (properties `Url`, `ContainerName`, and any others currently on it) is unchanged.

### FR-3: Update dependency injection registration
The job and its options are currently registered in `FileStorageModule` (or equivalent `Module.cs` for FileStorage). Registration must move to the Catalog module's `Module.cs`.

**Acceptance criteria:**
- `FileStorageModule` (or its `Module.cs`) no longer references `ProductExportDownloadJob` or `ProductExportOptions`.
- The Catalog module's `Module.cs` registers:
  - `ProductExportDownloadJob` with the same lifetime/registration it previously had in `FileStorageModule`.
  - `ProductExportOptions` bound from configuration with the same configuration section/path it previously used.
- If `FileStorageModule` previously registered any scheduling/hosted-service wiring for this job, that wiring moves to the Catalog module as well.
- DI container resolves the job successfully at application startup (no missing-service exceptions).

### FR-4: Update configuration section ownership (if scoped to FileStorage)
If `ProductExportOptions` was bound from a configuration section named under `FileStorage:` (e.g. `FileStorage:ProductExport`), the section path should move to a Catalog-owned key (e.g. `Catalog:ProductExport` or `ProductExport`) to avoid leaving Catalog config under a FileStorage namespace.

**Acceptance criteria:**
- The configuration section path used to bind `ProductExportOptions` is no longer rooted under `FileStorage:`.
- `appsettings.json`, `appsettings.Development.json`, `appsettings.Staging.json`, `appsettings.Production.json`, and any other settings files in the repo are updated to the new section path.
- Any Azure Key Vault secret names that referenced the old path (using `--` separators, e.g. `FileStorage--ProductExport--Url`) are migrated or the binding remains backward-compatible. The chosen strategy is documented in the PR description.
- Assumption (note in Open Questions): if the existing section path is already neutral or already domain-named, no rename is required — only the binding registration moves.

### FR-5: Update all references and using directives
Any other file in the solution that references `ProductExportDownloadJob` or `ProductExportOptions` must be updated to the new namespace.

**Acceptance criteria:**
- A solution-wide search for `ProductExportDownloadJob` and `ProductExportOptions` returns only references that point to the new Catalog namespace.
- `Anela.Heblo.Application.Features.FileStorage` is no longer imported via `using` anywhere on behalf of these two types.
- Solution builds cleanly: `dotnet build` succeeds with zero new warnings or errors.

### FR-6: Preserve cross-module access to `IBlobStorageService`
`ProductExportDownloadJob` must continue to depend on `IBlobStorageService` via its Domain interface — the existing correct cross-module pattern. No copy of the interface or implementation moves with the job.

**Acceptance criteria:**
- `IBlobStorageService` remains defined and implemented in its current location (FileStorage Domain/Infrastructure).
- The relocated job injects `IBlobStorageService` via constructor exactly as before.
- No new project references are introduced that would create a Catalog → FileStorage Infrastructure dependency. Catalog continues to depend only on the FileStorage Domain abstraction it already depends on (or via the shared Application/Domain assembly, matching the project's existing module-boundary pattern).

### FR-7: Test relocation and continued green status
Any existing tests for `ProductExportDownloadJob` (unit or integration) must be moved to the Catalog test project (if they live in a FileStorage test folder/project today) and must continue to pass.

**Acceptance criteria:**
- Test files that exclusively target `ProductExportDownloadJob` or `ProductExportOptions` are relocated to the Catalog test project/folder structure.
- Test namespaces are updated to match.
- `dotnet test` passes for the full backend test suite with zero new failures.
- If no tests exist for this job today, this requirement is satisfied trivially and noted in the PR description; no new tests are required by this change since logic is unchanged. (See Open Questions on whether to add minimum coverage opportunistically.)

## Non-Functional Requirements

### NFR-1: Performance
No performance change expected or permitted. The job's scheduling, execution path, and blob I/O remain identical. No new allocations, no added indirection, no changed lifetime.

### NFR-2: Security
No security surface change. The job continues to read its Shoptet export URL and storage credentials from configuration (which in this project means Azure Key Vault in staging/production). If the configuration section path changes (FR-4), the migration must not regress secret resolution in any environment.

**Acceptance criteria:**
- No secret values are committed to source.
- Key Vault secret names continue to resolve, either by retaining the old path or by adding the new path before removing the old one.
- Staging deployment of this branch successfully resolves `ProductExportOptions.Url` and `ProductExportOptions.ContainerName` at startup.

### NFR-3: Maintainability / Architectural cleanliness
This is the entire point of the change.

**Acceptance criteria:**
- After the change, `Features/FileStorage/` contains only generic blob-storage abstractions and implementations — no domain-specific jobs or options.
- `Features/Catalog/` now owns `ProductExportDownloadJob` and `ProductExportOptions` alongside other Catalog product-export concerns.
- Adding a new generic FileStorage capability no longer requires touching Catalog code; changing the product-export URL/filename no longer requires editing the FileStorage module.

### NFR-4: Backward compatibility at runtime
The change must be invisible to operators and downstream consumers of the exported blobs.

**Acceptance criteria:**
- Exported blob filename format remains `products_{timestamp}.csv`.
- Exported blobs land in the same container they did before (`ProductExportOptions.ContainerName` value unchanged).
- Scheduling cadence is unchanged.
- No log message format/scope changes beyond the unavoidable namespace change in default `ILogger<T>` category names. (See Open Questions if log category stability matters to existing log alerts.)

## Data Model
No data model changes. Configuration option shape is unchanged:

- `ProductExportOptions`
  - `Url` (string) — Shoptet product export URL
  - `ContainerName` (string) — target blob container for product-export CSV
  - (Any additional properties currently on the class — preserved as-is)

The only data-shape-adjacent change is the **configuration section path** that binds these options (see FR-4).

## API / Interface Design

### Public API
No public HTTP API or MediatR contract changes.

### Internal module surface
- **Before:** `FileStorage` module exposes/registers `ProductExportDownloadJob` and `ProductExportOptions`.
- **After:** `Catalog` module exposes/registers `ProductExportDownloadJob` and `ProductExportOptions`. `FileStorage` module exposes only generic blob storage abstractions.

### Module boundary
- `Catalog` depends on the `IBlobStorageService` Domain interface — already an established cross-module pattern in this codebase.
- `FileStorage` no longer depends on or knows about Catalog concerns.

### File moves (concrete)
| From | To |
| --- | --- |
| `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` | `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` (final path to match Catalog's existing convention) |
| `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` | `backend/src/Anela.Heblo.Application/Features/Catalog/ProductExportOptions.cs` (final path to match Catalog's existing convention) |
| FileStorage module's DI registration block for the job & options | Catalog module's `Module.cs` |
| Any existing tests for these types under a FileStorage test project | Corresponding Catalog test project |

## Dependencies
- `IBlobStorageService` (Domain abstraction) — remains in FileStorage, consumed by relocated job. No change.
- Configuration system (`IOptions<ProductExportOptions>`) — already in use; binding registration moves to Catalog module.
- Azure Key Vault (staging/production) — must continue to resolve the URL and container name secrets; affects the FR-4 migration strategy.
- Existing job-scheduling infrastructure (Quartz / Hangfire / `IHostedService` / whatever pattern the project uses today) — unchanged; only the registration call-site moves.
- No new NuGet packages.

## Out of Scope
- Refactoring `ProductExportDownloadJob` logic (URL handling, filename format, error handling, retry policy).
- Changing the scheduling cadence or trigger mechanism.
- Changing `IBlobStorageService` interface, implementation, or its module location.
- Renaming `ProductExportOptions` properties.
- Adding new tests beyond relocating existing ones (unless trivially required for the relocated registration to be verifiable — see Open Questions).
- Migrating other domain-specific jobs that may also live in `FileStorage`. If others are discovered, they are a separate change with their own brief.
- Any change to the Shoptet integration itself.
- Documentation updates beyond updating `CLAUDE.md` documentation map or architecture docs *only if* a doc explicitly references the old FileStorage location of these types. No broad doc rewrites.

## Open Questions
1. **Catalog sub-feature placement.** The `Catalog` module may have multiple sub-features (e.g. product catalog, product export, Shoptet integration). Should the relocated files live under a `Catalog/ProductExport/` sub-feature folder, or at the Catalog feature root mirroring the existing convention? Assumption: follow whatever pattern Catalog's other infrastructure/job files already use; if a `ProductExport` sub-feature exists, use it.
2. **Configuration section rename (FR-4).** Is the current `ProductExportOptions` bound from a section under `FileStorage:` today? If yes, do we rename the section (cleaner, but requires Key Vault secret rename and a coordinated deploy) or keep the old path (technically leaves a `FileStorage:` config remnant in Catalog code, but zero ops risk)? Default assumption: keep the old path if it's already neutral or domain-named; rename only if the existing path is explicitly under `FileStorage:`, and document the Key Vault migration steps in the PR.
3. **Log category continuity (NFR-4).** The default `ILogger<ProductExportDownloadJob>` category name will change because of the namespace move (`...FileStorage.Infrastructure.Jobs.ProductExportDownloadJob` → `...Catalog.Infrastructure.Jobs.ProductExportDownloadJob`). If any production log alerts or queries match on the old category, they need updating. Confirm whether this matters operationally; default assumption: not load-bearing, change accepted.
4. **Tests today.** Do unit/integration tests for `ProductExportDownloadJob` currently exist, and if so where? This affects the size of FR-7. If none exist, should a minimal smoke test be added for the relocated DI registration (verifying the job resolves from the container)? Default assumption: no new tests required; existing ones, if any, are moved.
5. **Other domain-specific items in FileStorage.** While moving these two files, are there other domain-specific options/jobs hiding in `Features/FileStorage/` that should be flagged in the PR description (without being fixed in this change)? Default: list any discovered, do not move them in this PR.

> **Note on `answers.r1.md` and `answers.r2.md`:** Both provided answer files address a different feature (a `PickingListBatchProcessor` / `ShoptetApiExpeditionListSource` cooling-marker PATCH logger choice) and do not correspond to any of the open questions above. They have therefore been ignored. The five open questions remain open and need clarification before implementation.

## Status: HAS_QUESTIONS