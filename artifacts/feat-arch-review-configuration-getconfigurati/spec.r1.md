# Specification: Replace Direct Environment Variable Access with IConfiguration in GetConfigurationHandler

## Summary
Refactor `GetConfigurationHandler` to read the application version through the injected `IConfiguration` abstraction instead of calling `System.Environment.GetEnvironmentVariable()` directly. This restores consistency with the rest of the handler, eliminates a hidden DI dependency, and makes the version-resolution fallback chain unit-testable without manipulating process-level environment variables.

## Background
`GetConfigurationHandler` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`) injects `IConfiguration` and uses it correctly for the `UseMockAuth` setting (line 66). However, the version resolution helper at line 76 bypasses the injected abstraction and calls `System.Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION)` directly.

Because the .NET host already registers environment variables as a configuration source via `AddEnvironmentVariables()`, `_configuration[ConfigurationConstants.APP_VERSION]` returns the same value through the established abstraction. The current code therefore has no functional benefit but introduces three concrete problems:

1. **Testability gap** — `System.Environment.GetEnvironmentVariable()` cannot be mocked. Verifying the three-level fallback chain (env var → assembly informational version → assembly version) requires setting real process-level env vars, which is fragile in parallel test runs.
2. **Inconsistency** — Two different mechanisms read configuration inside the same handler.
3. **DI contract violation** — The constructor advertises `IConfiguration` as the configuration source; the direct OS call is an undeclared hidden dependency.

This finding was filed by the daily arch-review routine on 2026-06-03.

## Functional Requirements

### FR-1: Read APP_VERSION through IConfiguration
The `GetVersionFromSources()` helper inside `GetConfigurationHandler` MUST read the `APP_VERSION` value via the injected `IConfiguration` instance instead of `System.Environment.GetEnvironmentVariable()`.

**Acceptance criteria:**
- `GetConfigurationHandler.cs` no longer contains any call to `System.Environment.GetEnvironmentVariable()` or `Environment.GetEnvironmentVariable()`.
- The handler reads the version using `_configuration[ConfigurationConstants.APP_VERSION]` (or equivalent `IConfiguration` accessor).
- The `using System;` directive remains only if other parts of the file still need it; otherwise it is removed.
- The `ConfigurationConstants.APP_VERSION` constant remains unchanged.

### FR-2: Preserve existing version fallback semantics
The refactor MUST NOT change observable behavior. The three-level fallback chain (env-var/configuration value → assembly informational version → assembly version) MUST continue to resolve in the same order with the same precedence.

**Acceptance criteria:**
- When `APP_VERSION` is set in the environment or in any registered configuration source with a non-empty value, the handler returns that value.
- When `APP_VERSION` is missing or empty, the handler falls through to the assembly informational version.
- When the informational version is missing or empty, the handler falls through to the assembly version.
- An integration test or unit test demonstrates each branch of the fallback chain.

### FR-3: Unit-testable version resolution
The refactored handler MUST be testable using a mocked or in-memory `IConfiguration` instance — no process-level environment variable manipulation should be required for unit tests of the version-resolution logic.

**Acceptance criteria:**
- A new or updated unit test for `GetConfigurationHandler` exercises the version-resolution path by configuring an in-memory `IConfiguration` (e.g., `ConfigurationBuilder().AddInMemoryCollection(...)`).
- Tests cover at minimum: (a) version present in configuration, (b) version absent — falls back to informational version, (c) informational version absent — falls back to assembly version.
- Tests run cleanly under parallel xUnit execution without `[Collection]` serialization workarounds related to environment variables.

## Non-Functional Requirements

### NFR-1: Behavioral parity
No change in runtime behavior across Development, Staging, or Production environments. The `IConfiguration` provider chain registered by the host (appsettings → appsettings.{Environment} → environment variables → user secrets in Dev) MUST resolve `APP_VERSION` identically to the previous direct OS call in all deployed configurations.

### NFR-2: No new dependencies
The change MUST NOT introduce new NuGet packages, new DI registrations, or new constants. It is a pure in-file refactor.

### NFR-3: Validation gates
The following project-mandated checks MUST pass before merge:
- `dotnet build` — clean.
- `dotnet format` — no formatting violations introduced.
- All existing tests that touch `GetConfigurationHandler` continue to pass.
- New unit tests for the version fallback chain pass.

### NFR-4: Surgical change
Only `GetConfigurationHandler.cs` and its associated test file(s) should be modified. Do not touch adjacent code, comments, formatting, or unrelated handlers.

## Data Model
No data-model changes. The DTO returned by the handler (`GetConfigurationResponse` or equivalent) is unchanged. Its `Version` property continues to be populated by the same fallback chain.

## API / Interface Design
No public API changes. The MediatR request/response contract (`GetConfigurationQuery` → response DTO) is unchanged. The HTTP endpoint that exposes this query is unchanged. Only the internal implementation of `GetVersionFromSources()` (or equivalently named private helper) changes.

## Dependencies
- `Microsoft.Extensions.Configuration.Abstractions` — already referenced; provides `IConfiguration`.
- Host builder must continue to call `AddEnvironmentVariables()` (already the case in `Program.cs`). This is a precondition, not a new dependency.
- Test project must reference `Microsoft.Extensions.Configuration` (for `ConfigurationBuilder` / `AddInMemoryCollection`) — verify it does; if not, add it to the test project only.

## Out of Scope
- Refactoring other handlers that may also bypass `IConfiguration`. (Out of scope for this finding; track separately if discovered.)
- Changing the version-resolution fallback order or adding new sources (e.g., file-based version stamps).
- Modifying `ConfigurationConstants.APP_VERSION` or its consumers elsewhere.
- Touching the deployment pipeline or how `APP_VERSION` is injected into the Docker container / Azure Web App.
- Adding integration tests beyond what is required to cover the fallback chain.

## Open Questions
None.

## Status: COMPLETE