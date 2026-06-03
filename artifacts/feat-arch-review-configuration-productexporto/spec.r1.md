# Specification: Move ProductExportOptions to FileStorage Module

## Summary
Relocate the `ProductExportOptions` configuration class from the Configuration module's Domain layer to the FileStorage module's Application layer, where it is exclusively consumed. This is a pure refactor with no behavioural change, correcting a Vertical Slice boundary violation.

## Background
The Anela.Heblo backend follows Clean Architecture with Vertical Slice organization, where each module owns its own contracts, types, and configuration. `ProductExportOptions` currently lives in `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` but is consumed only by FileStorage components:

- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs:18`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:22`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs:24`

The class models retry policy, timeouts, and download URL for the product export download job — concepts internal to FileStorage with no relationship to the Configuration module. The current placement forces FileStorage developers to traverse module boundaries to inspect their own configuration contract, and creates a cross-module Domain-level dependency that would block independent deployability of modules.

This finding was filed by the daily arch-review routine on 2026-05-29.

## Functional Requirements

### FR-1: Relocate ProductExportOptions class
Move the `ProductExportOptions` class from its current Domain-layer location in the Configuration module to the FileStorage module's Application layer.

**Source path:**
`backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`

**Target path:**
`backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`

**Acceptance criteria:**
- File exists at the target path with identical class definition (members, modifiers, attributes, XML docs) to the original.
- The original file at the source path is deleted.
- The namespace of the moved class matches the conventions used for other types within `backend/src/Anela.Heblo.Application/Features/FileStorage/` (i.e. `Anela.Heblo.Application.Features.FileStorage` or a sibling matching surrounding files).
- No other types are moved as part of this change.

### FR-2: Update consumer `using` statements
All consumers of `ProductExportOptions` must reference the new namespace.

**Consumers to update:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`

**Acceptance criteria:**
- Each consumer's `using` directive for the old Configuration namespace is removed (if the moved class was its only use of that namespace).
- Each consumer compiles against the new namespace.
- No other code in these files is modified.

### FR-3: Update DI registration
The dependency injection registration of `ProductExportOptions` must reference the new namespace.

**File:**
`ServiceCollectionExtensions.cs:356` (per the brief — likely under FileStorage module's DI registration path; the exact file should be located via grep on `ProductExportOptions` in `ServiceCollectionExtensions.cs` files).

**Acceptance criteria:**
- The DI registration (e.g. `Configure<ProductExportOptions>(...)`) continues to bind to the same configuration section key as before the refactor.
- Only the `using` directive changes; binding semantics are unchanged.

### FR-4: Verify no other references remain
Confirm no other code, test, or documentation references the old namespace path for `ProductExportOptions`.

**Acceptance criteria:**
- Repo-wide search for `Anela.Heblo.Domain.Features.Configuration.ProductExportOptions` returns zero matches.
- Repo-wide search for `ProductExportOptions` returns only the three known consumer files, the DI registration, the new class location, and any pre-existing tests.

### FR-5: Preserve runtime behaviour
The product export download job, retry policy, and timeout behaviour must be identical before and after the refactor.

**Acceptance criteria:**
- The bound configuration section name (e.g. `ProductExport`) is unchanged.
- All property names, types, and default values on `ProductExportOptions` are unchanged.
- Any existing unit or integration tests covering the download job continue to pass without modification (other than namespace `using` updates if a test references the class directly).

## Non-Functional Requirements

### NFR-1: Build & Format Validation
The refactor must pass the project's standard pre-completion gates:
- `dotnet build` succeeds with zero errors and no new warnings introduced by this change.
- `dotnet format` produces no diff against the committed result.

### NFR-2: Test Stability
- All existing backend tests touching FileStorage or Configuration modules continue to pass.
- No new tests are required — this is a pure relocation with no behavioural surface.

### NFR-3: No Configuration / Deployment Change
- `appsettings*.json` files are not modified.
- Azure Key Vault secrets are not touched.
- Docker image build is unaffected.
- No database migration is involved.

### NFR-4: Minimal Surgical Change
- Only the four file categories named in FR-1 through FR-3 are modified.
- No adjacent refactoring, formatting cleanup, or unrelated improvements are bundled in.

## Data Model
No data model change. `ProductExportOptions` is an options/configuration POCO bound from `IConfiguration` via the standard ASP.NET Core options pattern. Its shape (properties, types, attributes) is preserved verbatim.

## API / Interface Design
No public API, HTTP endpoint, MediatR contract, or UI surface is affected. The class is consumed internally via `IOptions<ProductExportOptions>` / `IOptionsMonitor<ProductExportOptions>` injection inside FileStorage components only.

## Dependencies
- `Anela.Heblo.Application` project must continue to be able to host options classes (it already does — the move is into an existing project structure).
- `Anela.Heblo.Domain` project must remain buildable after the file removal (no other code in Domain depends on `ProductExportOptions` per the brief).
- No new NuGet packages or external services.

## Out of Scope
- Moving any other configuration class out of `Anela.Heblo.Domain/Features/Configuration/`. A broader audit of misplaced options types is a separate finding.
- Introducing a `Contracts/` subfolder convention for FileStorage if one does not already exist. If FileStorage already uses `Contracts/`, the target path should follow that convention; if not, the flat target path in FR-1 stands.
- Renaming `ProductExportOptions` or any of its members.
- Changing the configuration section key in `appsettings*.json`.
- Adding new tests beyond what is required to keep the build green.
- Restructuring the Configuration module's remaining contents.

## Open Questions
None.

## Status: COMPLETE