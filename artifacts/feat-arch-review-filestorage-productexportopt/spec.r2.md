# Specification: Verify and Document ProductExportOptions Module Ownership

## Summary
The brief reports that `ProductExportOptions` is configured in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` outside its owning module, violating ADR-004 ("one vertical slice = one module wires its own DI"). Verification against the current worktree shows the finding is **stale**: the binding now lives at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114` and `ServiceCollectionExtensions.cs` contains no reference to `ProductExportOptions`. This spec confirms the resolution, locks in the correct ownership (Catalog, not FileStorage), and adds a guard test to prevent regression.

## Background
The brief was filed by the daily arch-review routine on 2026-06-05 against the FileStorage module. Between then and now, two related plans were authored:

- `docs/superpowers/plans/2026-06-02-relocate-productexportoptions-to-filestorage.md` — earlier proposal to move the options into FileStorage.
- `docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md` — superseding decision to place both the options and the consuming job inside the Catalog module.

The current code reflects the 2026-06-12 decision:
- `ProductExportOptions` lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure`.
- `ProductExportDownloadJob` (the sole consumer) lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs`.
- The DI binding lives in `CatalogModule.cs` (the module that owns both types).

`CatalogModule.cs:114`:
```csharp
services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

`ServiceCollectionExtensions.cs:364` (the line the brief cited) is unrelated — it currently binds `HangfireOptions`, not `ProductExportOptions`. A repository-wide grep finds zero references to `ProductExportOptions` outside `Anela.Heblo.Application.Features.Catalog.*` and the matching test/plan files.

Conclusion: the arch-review violation no longer exists. The brief's suggested fix ("move binding into `FileStorageModule`") would actually **reintroduce** an ADR-004 violation, because the option type and its consumer both belong to Catalog. The remaining value of this work is to (a) confirm the resolution, (b) add a regression guard, and (c) close the loop with the arch-review routine.

## Functional Requirements

### FR-1: Confirm current binding location satisfies ADR-004
The `ProductExportOptions` DI binding must remain inside `CatalogModule.AddCatalogModule` and must not appear in any API-layer extension method.

**Acceptance criteria:**
- `rg "Configure<ProductExportOptions>"` returns exactly one source-tree hit: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` contains no reference to `ProductExportOptions`.
- `CatalogModule.AddCatalogModule` is invoked from the composition root with `IConfiguration` already wired (confirmed by reading the API startup path).

### FR-2: Regression guard test
Add an xUnit test in `backend/test/Anela.Heblo.Tests/Features/Catalog/` that builds a minimal `ServiceProvider` from `CatalogModule.AddCatalogModule` plus the necessary configuration and asserts that `IOptions<ProductExportOptions>` resolves with values bound from the `"ProductExportOptions"` configuration section.

Scope decision: a **pure unit test scoped to `CatalogModule.AddCatalogModule`**, not a broader composition-root assertion that enumerates every `*Module`. Rationale: the cheap, local test directly defends the documented contract for this binding without taking on the larger investment of a cross-module convention test; a generic guard can be considered later as a separate housekeeping task.

**Acceptance criteria:**
- Test resolves `IOptions<ProductExportOptions>` from a provider built only by `CatalogModule.AddCatalogModule(configuration, environment)` (no API-layer wiring).
- Test sets `ProductExportOptions:Url` and `ProductExportOptions:ContainerName` via an in-memory `IConfiguration` and asserts both round-trip to the resolved options.
- Test fails (clearly) if the `services.Configure<ProductExportOptions>(...)` line is removed from `CatalogModule`.
- Test runs as part of `dotnet test` and is independent of any external configuration source (`appsettings.json` ignored).
- Test name/class follows the existing Catalog test conventions; uses FluentAssertions if already adopted in the project.

### FR-3: Document module ownership decision
Capture the decision that `ProductExportOptions` belongs to Catalog (not FileStorage) in `memory/decisions/` so future arch-review iterations do not re-file the same stale finding.

Location decision: use `memory/decisions/` (per `CLAUDE.md`'s "architectural and library choices with reasoning" mandate). The repository does not currently host a numbered ADR directory at `docs/architecture/decisions/`, so introducing one is out of scope; if/when an ADR ledger is established, this memory note can be promoted.

**Acceptance criteria:**
- New file `memory/decisions/product-export-options-ownership.md` exists.
- File references both prior plans (`2026-06-02-relocate-productexportoptions-to-filestorage.md`, `2026-06-12-relocate-productexportdownloadjob-to-catalog.md`) and states the conclusion: Catalog owns both `ProductExportOptions` and `ProductExportDownloadJob`; FileStorage exposes only generic download/upload primitives.
- File records the file paths the decision affects (`CatalogModule.cs`, `ServiceCollectionExtensions.cs`) so future greps land on it.
- File is added to any existing `memory/decisions/` index file if one is present; otherwise the file itself is sufficient.

### FR-4: Close arch-review finding
Record the resolution of the FileStorage `ProductExportOptions` finding so the daily arch-review routine and any human reviewer can see the finding has been addressed.

Location decision: write the resolution note to `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md` (the per-branch artifacts directory the routine already uses for `brief.md`, `spec.r{N}.md`, `answers.r{N}.md`). Also surface the conclusion in the PR description so a human reviewer sees it without opening the artifacts folder.

**Acceptance criteria:**
- `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md` exists and records: finding source (daily arch-review routine, 2026-06-05, FileStorage module), current state ("`ProductExportOptions` bound in `CatalogModule.cs:114`; no API-layer binding"), conclusion ("not applicable — already resolved by 2026-06-12 plan"), and links to the regression guard test (FR-2) and the decision memory (FR-3).
- The PR description includes a one-line summary linking to `resolution.md` and the decision memory.

## Non-Functional Requirements

### NFR-1: Performance
N/A. This is a configuration-wiring audit; runtime behavior is unchanged.

### NFR-2: Security
N/A. No secret handling, no auth surface, no data path changes.

### NFR-3: Backward compatibility
The `"ProductExportOptions"` configuration section name and shape must not change. Existing `appsettings.json`, Key Vault entries, and the `ProductExportDownloadJob` consumer must continue to work without redeploy.

### NFR-4: Build & lint gates
`dotnet build`, `dotnet format`, and `dotnet test` must pass on the touched projects.

## Data Model
No data model changes. Existing types:

- `Anela.Heblo.Application.Features.Catalog.Infrastructure.ProductExportOptions` — POCO with `Url` and `ContainerName` (mutable setters, matches the project's options-class convention).
- Configuration section name: `"ProductExportOptions"` (top-level).

## API / Interface Design
No public API changes.

Module surface unchanged:
- `CatalogModule.AddCatalogModule(IServiceCollection, IConfiguration, ...)` — already binds `ProductExportOptions`.
- `FileStorageModule.AddFileStorageModule(IServiceCollection, IConfiguration, IHostEnvironment)` — owns generic download primitives only.

## Dependencies
- `Microsoft.Extensions.Configuration` (already referenced).
- `Microsoft.Extensions.Options` (already referenced).
- xUnit + FluentAssertions (already used by the Catalog test project).

No new packages.

## Out of Scope
- Renaming or restructuring `ProductExportOptions` (shape, section name, casing).
- Moving `ProductExportDownloadJob` or `ProductExportOptions` between modules — the 2026-06-12 plan already settled this.
- Touching any other module's options bindings (e.g., `HangfireOptions` at `ServiceCollectionExtensions.cs:364`); those are out of scope even if similar audits apply.
- Adding `ValidateOnStart()` or `Validate(...)` to `ProductExportOptions` — separate hardening task.
- A cross-module convention test that enumerates every `*Module` and asserts each binds its declared options — see FR-2 scope decision.
- Establishing or migrating to a numbered ADR directory at `docs/architecture/decisions/` — see FR-3 location decision.
- Changing how the arch-review routine itself runs or filters resolved findings.

## Open Questions
None.

## Status: COMPLETE