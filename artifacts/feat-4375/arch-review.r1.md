# Architecture Review: Remove ApplicationConfiguration entity and ConfigurationConstants

## Skip Design: true
Backend-only pure refactor; no UI.

## Architectural Fit Assessment
Fits the project's vertical-slice / pragmatic Clean Architecture guidance (`docs/architecture/development_guidelines.md`): the Domain layer should hold real domain concepts. `ApplicationConfiguration` is a transport-shaped holder with no behaviour, and the configuration module is a read-only projection of `IConfiguration` plus assembly metadata. Verified in code: the only references to both types are the two Domain files, `GetConfigurationHandler`, and two test files (plus a process doc). No other project (API, Persistence, Infrastructure, frontend) references them.

## Proposed Architecture

### Component Overview
Before: `Handler -> ApplicationConfiguration.CreateWithDefaults (Domain, ConfigurationConstants) -> copy -> Response`.
After: `Handler -> Response` (defaults as private consts in the handler).

### Key Design Decisions

#### Decision 1: Where defaults live
**Options considered:** private consts in handler; keep a constants class in Application; keep in Domain.
**Chosen approach:** `private const string DefaultVersion = "1.0.0"; private const string DefaultEnvironment = "Production";` inside `GetConfigurationHandler`.
**Rationale:** single consumer; matches the issue's suggestion; no cross-layer dependency.

#### Decision 2: Null handling
**Chosen approach:** `GetVersionFromSources() ?? DefaultVersion` and `_configuration["ASPNETCORE_ENVIRONMENT"] ?? DefaultEnvironment`, exactly preserving current semantics (empty-string environment is NOT replaced; do not "improve" this).

#### Decision 3: Handler structure
Keep `BuildApplicationConfiguration` out; either inline in `Handle` or keep a small private method returning `GetConfigurationResponse`. Prefer inline in `Handle` (a prior plan removed speculative async here; keep the method synchronous returning `Task.FromResult`).

## Implementation Guidance

### Directory / Module Structure
- Delete `backend/src/Anela.Heblo.Domain/Features/Configuration/` (both files).
- Edit `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`.
- Delete `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs`; extend `GetConfigurationHandlerTests.cs`.
- Edit `docs/processes/module-configuration.md`.

### Interfaces and Contracts
`GetConfigurationRequest` / `GetConfigurationResponse` unchanged (DTO classes, not records).

### Data Flow
Request -> `GetConfigurationHandler.Handle` -> read `IConfiguration` / assembly attributes -> build response -> return.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Behaviour drift in defaults | Low | Handler tests for environment default, mock-auth values; existing endpoint integration tests |
| Dangling reference in tests/docs | Low | grep for both type names after change; `dotnet build` of test project |
| Process-doc CI gate fails | Low | Update doc and bump `verified_at` in same PR; run `check.py index` |
| Environment-default test is flaky if env var `ASPNETCORE_ENVIRONMENT` leaks | Low | `IConfiguration` is built from in-memory collection only; do not use `AddEnvironmentVariables` |

## Specification Amendments
Note: `CreateHandler` in `GetConfigurationHandlerTests` always adds `ASPNETCORE_ENVIRONMENT=Test` via `TryAdd`; the default-environment test needs a handler built without it (build the configuration directly in that test or add an optional parameter).

## Prerequisites
None.
