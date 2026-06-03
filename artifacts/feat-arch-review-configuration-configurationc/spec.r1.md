# Specification: Remove Unused ASPNETCORE_ENVIRONMENT Constant

## Summary
Delete the unused `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` constant from the Configuration domain. The constant is declared but referenced nowhere in the codebase, providing false reassurance of a centralization pattern that does not actually exist.

## Background
A daily architecture review identified that `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` at `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:9` is dead code. Every site that reads the ASP.NET Core environment name either:
- Uses `IHostEnvironment.EnvironmentName` directly (the preferred approach), or
- Calls `Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")` with the raw string literal.

Because no caller references the constant, it offers no safety value and adds noise to the configuration constants file. Removing it eliminates a misleading abstraction without changing runtime behavior.

## Functional Requirements

### FR-1: Remove the unused constant declaration
Delete the `ASPNETCORE_ENVIRONMENT` constant from `ConfigurationConstants.cs`.

**Acceptance criteria:**
- Line 9 (`public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";`) is removed from `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`.
- The surrounding file structure (class declaration, namespace, other constants) remains intact and properly formatted.
- No `using` statements become orphaned as a result.

### FR-2: Verify no references exist before deletion
Confirm that the constant truly has zero usages across the entire solution prior to removal.

**Acceptance criteria:**
- A repository-wide search for `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` returns zero matches outside the declaration site.
- A repository-wide search for `ConfigurationConstants\.ASPNETCORE_ENVIRONMENT` (regex) confirms no qualified accessors.
- Search is documented in the PR description (e.g., the `grep`/`ripgrep` command used and its output).

### FR-3: Solution must compile and tests must pass
The codebase remains buildable and all existing tests continue to pass after removal.

**Acceptance criteria:**
- `dotnet build` succeeds for the entire solution with no new warnings or errors.
- The full backend test suite passes (`dotnet test`).
- No CI checks regress on the PR.

### FR-4: Preserve raw-string callers as-is
Do not modify the raw-string call sites in this change.

**Acceptance criteria:**
- The following files are NOT modified by this PR:
  - `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs` (lines 31, 44, 86, 108)
  - `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs` (line 51)
  - `backend/src/Anela.Heblo.API/Telemetry/CostOptimizedTelemetryProcessor.cs` (line 95)
  - `backend/src/Anela.Heblo.Persistence/DesignTimeDbContextFactory.cs` (line 17)
  - `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` (line 63)
- A follow-up cleanup of these call sites is tracked separately (see Out of Scope).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a compile-time deletion of an unused symbol; the generated assembly will be marginally smaller but indistinguishable at runtime.

### NFR-2: Security
No security impact. The constant carries no secret material; it duplicates a well-known ASP.NET Core environment variable name that is public by convention.

### NFR-3: Maintainability
Reduces noise in `ConfigurationConstants.cs`, making it clearer which constants are actually load-bearing in the codebase. Eliminates a misleading signal that a centralized constant pattern exists for environment-name lookups when in fact it does not.

### NFR-4: Backwards compatibility
No external consumers depend on this constant because:
- `ConfigurationConstants` lives in the internal `Anela.Heblo.Domain` project, not a published library.
- The constant is unused even internally.
No backwards-compatibility shims (e.g., `[Obsolete]` attribute) are required.

## Data Model
Not applicable. No data model changes.

## API / Interface Design
Not applicable. No public API surface changes. The constant is internal-only and unused.

## Dependencies
- .NET SDK toolchain (already in use by the project).
- The existing build and test pipeline.

No new external services, libraries, or features are introduced.

## Out of Scope
- **Refactoring raw-string callers.** The five files that pass the literal `"ASPNETCORE_ENVIRONMENT"` to `Environment.GetEnvironmentVariable()` are explicitly left untouched in this change. A separate, lower-priority cleanup task should migrate those sites to use `IHostEnvironment.EnvironmentName` (already DI-available) where appropriate. That cleanup involves verifying DI availability at each call site and is non-trivial for static contexts like `DesignTimeDbContextFactory`.
- **Auditing other constants in `ConfigurationConstants.cs`.** This change addresses only the `ASPNETCORE_ENVIRONMENT` finding. A broader audit of dead constants is out of scope.
- **Introducing analyzer rules.** Adding a Roslyn analyzer or `.editorconfig` rule to flag future dead constants is not part of this work.

## Open Questions
None.

## Status: COMPLETE