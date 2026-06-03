I have enough context. Writing the architecture review now.

# Architecture Review: Move ProductExportOptions to FileStorage Module

## Skip Design: true

This is a pure backend refactor — relocating a single options POCO between projects and folders. No UI, no visual changes, no new components.

## Architectural Fit Assessment

The refactor aligns cleanly with the project's **Vertical Slice + Clean Architecture** layout. The current placement of `ProductExportOptions` is the anomaly, not the target:

- The Configuration module's Domain folder contains genuinely cross-cutting application configuration (`ApplicationConfiguration`, `ConfigurationConstants`) — environment, auth scheme, health-check tags. `ProductExportOptions` (download URL, retry policy, container name) has nothing in common with those concerns.
- Every other feature with an options POCO already places it inside its own `Application/Features/{Feature}/` slice. Confirmed by grep: `ArticleOptions`, `LeafletOptions`, `OrgChartOptions`, `KnowledgeBaseOptions`, `MeetingTasksOptions`, `ExpeditionListArchiveOptions`, `FinancialAnalysisOptions`, plus subfolder-style variants under `Photobank/Configuration/`, `Marketing/Configuration/`, `Manufacture/Configuration/`, `Catalog/Infrastructure/`.
- FileStorage is the only consumer (3 production files + 5 test files); no Domain code or other module touches the type. Moving it creates **zero new cross-module references**.
- The integration points are: `using` directives in 3 production + 5 test files, the file location, and the DI registration line in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:363`.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application/Features/FileStorage/
├── FileStorageModule.cs               (DI registration — extend here)
├── ProductExportOptions.cs            ← NEW HOME (flat, sibling to module)
├── Infrastructure/
│   ├── DownloadResilienceService.cs   (consumer: IOptions<ProductExportOptions>)
│   └── Jobs/
│       └── ProductExportDownloadJob.cs (consumer: IOptions<ProductExportOptions>)
├── Services/
└── UseCases/
    └── DownloadFromUrl/
        └── DownloadFromUrlHandler.cs   (consumer: IOptions<ProductExportOptions>)

Anela.Heblo.Domain/Features/Configuration/
├── ApplicationConfiguration.cs        (unchanged)
├── ConfigurationConstants.cs          (unchanged)
└── ProductExportOptions.cs            ← DELETE
```

### Key Design Decisions

#### Decision 1: Flat placement vs `Configuration/` subfolder

**Options considered:**
- A. Flat: `Application/Features/FileStorage/ProductExportOptions.cs`
- B. Subfolder: `Application/Features/FileStorage/Configuration/ProductExportOptions.cs`
- C. Inside `Infrastructure/`: next to the consumers

**Chosen approach:** A (flat).

**Rationale:** FileStorage has exactly one options class and no `Configuration/` subfolder today. Other modules with a single options class (Article, Leaflet, OrgChart, MeetingTasks, KnowledgeBase) all use flat placement. Creating a `Configuration/` subfolder for one file is premature structure and contradicts the spec's "no new conventions" stance (Out of Scope item). Placing under `Infrastructure/` would couple the contract to one implementation directory; options are consumed by both `Infrastructure/` and `UseCases/`, so the module root is the right shared location.

#### Decision 2: Namespace

**Chosen approach:** `Anela.Heblo.Application.Features.FileStorage` (folder-default).

**Rationale:** Matches every other top-level options class in the Application project (e.g. `Anela.Heblo.Application.Features.Leaflet.LeafletOptions`). Implicit/file-scoped namespace inferred from folder path — no surprises for `dotnet format` or analyzers.

#### Decision 3: Where the `Configure<ProductExportOptions>(...)` call lives

**Options considered:**
- A. Leave it on `ServiceCollectionExtensions.cs:363` (alongside `HangfireOptions` registration), update only the `using` directive.
- B. Move it into `FileStorageModule.AddFileStorageModule(this IServiceCollection, IConfiguration)`, which already accepts `IConfiguration` and is the module's documented composition entry point.

**Chosen approach:** A — strictly follow FR-3 as written (only `using` change, no binding relocation).

**Rationale:** FR-3 explicitly states "Only the `using` directive changes; binding semantics are unchanged." Moving the registration call into the module would be a behavioural-neutral improvement but violates the spec's NFR-4 (minimal surgical change) and Out of Scope ("No adjacent refactoring … bundled in"). The registration relocation is a legitimate follow-up but **should not be done in this PR**. See Specification Amendments.

#### Decision 4: Section key constant

**Chosen approach:** Do **not** introduce `public const string SectionName = "ProductExportOptions"` on the moved class.

**Rationale:** The DI registration currently uses the literal `"ProductExportOptions"`. Adding a constant is an unrelated improvement and would expand the diff. Out of scope per NFR-4. Track separately if desired.

## Implementation Guidance

### Directory / Module Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`
  - Namespace: `Anela.Heblo.Application.Features.FileStorage`
  - Class body byte-identical to the original (members, modifiers, XML docs).

**Delete:**
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`

### Interfaces and Contracts

No public API contract changes. The class continues to be consumed via:
- `IOptions<ProductExportOptions>` (in `DownloadResilienceService`, `DownloadFromUrlHandler`, `ProductExportDownloadJob`)
- Configuration binding from the `"ProductExportOptions"` section in `appsettings*.json`.

All property names, types, defaults, and XML docs remain identical.

### Data Flow

Unchanged. ASP.NET Core `IOptions<T>` pipeline: `appsettings.json` → `services.Configure<ProductExportOptions>(...)` at composition root → `IOptions<ProductExportOptions>` injected into consumers.

### Files Requiring `using` Updates

**Production code (3 files — from spec FR-2):**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs:1`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs:8`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:9`

**DI registration (1 file — from spec FR-3, corrected line number):**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:10` (the file-level `using Anela.Heblo.Domain.Features.Configuration;`) — **keep** because other types from that namespace (`ConfigurationConstants`, `ApplicationConfiguration`) are still referenced in the same file. Verify by re-grepping after the move.
- Line where binding happens: `ServiceCollectionExtensions.cs:363` (spec says `:356` — that's stale; actual is `:363`).

**Test code (5 files — MISSING FROM SPEC, see amendments):**
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs:1`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs:3`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs:2`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs:11`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs:11`

For each consumer file: **remove** `using Anela.Heblo.Domain.Features.Configuration;` only if no other type from that namespace is used in the file; otherwise leave it and **add** `using Anela.Heblo.Application.Features.FileStorage;` (or rely on same-namespace resolution if the file already lives under `Anela.Heblo.Application.Features.FileStorage.*`). The three production consumers all live under `Anela.Heblo.Application.Features.FileStorage.*` namespaces, so they will pick up `ProductExportOptions` **without any explicit `using`** once the move is done — the old `using Anela.Heblo.Domain.Features.Configuration;` should simply be deleted from those three files. Tests live in `Anela.Heblo.Tests.Features.FileStorage.*` and **must** add `using Anela.Heblo.Application.Features.FileStorage;`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Tests reference the old namespace; build breaks if missed | Medium | Spec FR-2 lists only production consumers. Amend to list 5 test files (see amendments). Final-step grep on `Anela.Heblo.Domain.Features.Configuration` over the whole solution must return zero `ProductExportOptions` hits. |
| DI section key drift breaks runtime binding | High (if it happened) | FR-5 already nails this down. Verify: `configuration.GetSection("ProductExportOptions")` literal is unchanged. Add a unit assertion only if a regression seems plausible (NFR-2 says no new tests). |
| `ProductExportOptionsTests.cs` lives in `Tests/Features/FileStorage/Configuration/` — confusing subfolder named "Configuration" | Low | Out of scope to rename the test folder. Note it as a follow-up; the folder name no longer reflects the class location. |
| Stale `using Anela.Heblo.Domain.Features.Configuration;` left in consumer files where no other Configuration-namespace type is used | Low | `dotnet format` with default analyzer (IDE0005) will surface it. NFR-1 gate catches this. |
| Domain project becomes empty in `Features/Configuration/`? | None | It does not — `ApplicationConfiguration.cs` and `ConfigurationConstants.cs` remain. Verified by `ls`. |
| Stale line number `:356` in brief/spec for DI registration | Low | Actual line is `:363`. Implementer must locate by grep on `Configure<ProductExportOptions>` — spec FR-3 already advises this. |

## Specification Amendments

The following changes to `spec.r1.md` are required for the refactor to be correctly executed:

1. **FR-2: Add the 5 test files to the consumer list.** Currently FR-2 lists only the 3 production consumers. The 5 test files also import `Anela.Heblo.Domain.Features.Configuration` and reference `ProductExportOptions`; their `using` directives must change too. Without this, the build fails.

   Files to add:
   - `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`
   - `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
   - `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`
   - `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`
   - `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

2. **FR-3: Correct the line number.** Spec says `ServiceCollectionExtensions.cs:356`; actual is `:363`. The spec already advises locating it via grep, which is correct guidance — just update the example.

3. **FR-3: Clarify scope of file-level `using` change.** `ServiceCollectionExtensions.cs` references multiple types from `Anela.Heblo.Domain.Features.Configuration` (`ConfigurationConstants`, etc.). The file-level `using` directive **must NOT** be removed — only `Anela.Heblo.Application.Features.FileStorage` must be added (or, if the file already qualifies, ensure type resolution still works). The spec's current wording ("Each consumer's `using` directive for the old Configuration namespace is removed") could mislead an implementer into deleting a still-needed import.

4. **Add a follow-up note (not in scope, but worth tracking):** Once this refactor lands, move the `services.Configure<ProductExportOptions>(...)` call from `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` into `FileStorageModule.AddFileStorageModule(...)` so the FileStorage module fully owns its option binding. That mirrors how the brief frames module ownership and would complete the vertical slice. **Do not bundle this into the current PR** (violates NFR-4).

## Prerequisites

None. The refactor is self-contained:

- No new migrations.
- No `appsettings*.json` changes (FR-5 explicitly preserves the `ProductExportOptions` section key).
- No Azure Key Vault changes.
- No new NuGet packages.
- No project reference changes (`Anela.Heblo.Application` already references `Anela.Heblo.Domain`; the removal cannot create a forbidden Domain→Application dependency because `ProductExportOptions` had no Domain consumers).
- Validation gates (per `CLAUDE.md`): `dotnet build` + `dotnet format` + all tests in `Anela.Heblo.Tests/Features/FileStorage/` must pass with no warnings introduced.