# Specification: Remove IHostEnvironment from GetConfigurationHandler

## Summary

`GetConfigurationHandler` in the Application layer currently injects `IHostEnvironment` (a Generic Host / ASP.NET Core abstraction) solely to read `EnvironmentName`. Replacing this with a direct `IConfiguration` lookup eliminates an upward architectural dependency from the Application layer into hosting infrastructure, and removes the only reason the unit-test suite must mock `IHostEnvironment`.

## Background

The project follows Clean Architecture: the Application layer must not depend on hosting or infrastructure concerns. `IHostEnvironment` lives in `Microsoft.Extensions.Hosting`, which is a host-layer contract. Its presence in `GetConfigurationHandler` creates a coupling that:

1. Violates the Application-layer boundary (the layer should only depend on domain abstractions and `Microsoft.Extensions.Configuration.Abstractions`).
2. Forces `GetConfigurationHandlerTests` to set up an `IHostEnvironment` NSubstitute mock (`environment.EnvironmentName.Returns("Test")`) purely as scaffolding — not to test behaviour — increasing test friction.

The fix is already fully prescribed: read the environment name from `IConfiguration` using the `ASPNETCORE_ENVIRONMENT` key, with a fallback to `ConfigurationConstants.DEFAULT_ENVIRONMENT` ("Production"). `IConfiguration` is already injected and is an accepted Application-layer dependency throughout this codebase.

## Functional Requirements

### FR-1: Replace IHostEnvironment with IConfiguration read in GetConfigurationHandler

Remove the `IHostEnvironment _environment` field and its constructor parameter from `GetConfigurationHandler`. Inside `BuildApplicationConfiguration()`, replace the line:

```csharp
var environment = _environment.EnvironmentName;
```

with:

```csharp
var environment = _configuration["ASPNETCORE_ENVIRONMENT"]
    ?? ConfigurationConstants.DEFAULT_ENVIRONMENT;
```

The `using Microsoft.Extensions.Hosting;` directive must also be removed (it will have no remaining references).

**Acceptance criteria:**
- `GetConfigurationHandler` constructor signature accepts exactly two parameters: `IConfiguration configuration` and `ILogger<GetConfigurationHandler> logger`.
- No `using Microsoft.Extensions.Hosting` directive remains in `GetConfigurationHandler.cs`.
- `BuildApplicationConfiguration()` reads the environment string from `_configuration["ASPNETCORE_ENVIRONMENT"]` with a `ConfigurationConstants.DEFAULT_ENVIRONMENT` fallback.
- The handler still correctly passes the environment value to `ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth)`.
- `dotnet build` produces zero errors and zero new warnings.

### FR-2: Remove IHostEnvironment mock from GetConfigurationHandlerTests

Remove the NSubstitute mock of `IHostEnvironment` from the `CreateHandler` factory method in `GetConfigurationHandlerTests`. The environment value under test ("Test") must instead be supplied via the `configData` dictionary using the key `"ASPNETCORE_ENVIRONMENT"`.

**Acceptance criteria:**
- `CreateHandler` no longer references `IHostEnvironment`, `Substitute.For<IHostEnvironment>()`, or `environment.EnvironmentName.Returns(...)`.
- The `using Microsoft.Extensions.Hosting` directive is removed from `GetConfigurationHandlerTests.cs`.
- All four existing tests (`Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet`, `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty`, `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent`, `Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet`) continue to pass without modification to their assertion logic.
- No test is deleted; the set of test cases is unchanged.
- `dotnet test` on the `Anela.Heblo.Tests` project passes.

### FR-3: Verify environment value is populated correctly in all runtime environments

Confirm (by inspection, not by new code) that `ASPNETCORE_ENVIRONMENT` is set in all deployment targets (Development, Staging, Production) so the fallback path to `ConfigurationConstants.DEFAULT_ENVIRONMENT` ("Production") is a safe safety net, not a silent misconfiguration.

**Acceptance criteria:**
- A code comment in `BuildApplicationConfiguration()` adjacent to the new line documents the fallback value and its source constant, e.g.:
  ```csharp
  // Falls back to ConfigurationConstants.DEFAULT_ENVIRONMENT ("Production") if not set
  var environment = _configuration["ASPNETCORE_ENVIRONMENT"]
      ?? ConfigurationConstants.DEFAULT_ENVIRONMENT;
  ```

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. This is a substitution of one string read (`IHostEnvironment.EnvironmentName`) for another (`IConfiguration["ASPNETCORE_ENVIRONMENT"]`); both are O(1) in-memory dictionary lookups. The handler is called infrequently (application startup / config endpoint).

### NFR-2: Security

No security impact. `ASPNETCORE_ENVIRONMENT` is a non-sensitive runtime value. It was already being read and returned to callers; the source of the value changes, not its exposure.

### NFR-3: Testability

After this change the handler's only injected dependencies are `IConfiguration` (backed by `ConfigurationBuilder().AddInMemoryCollection()` in tests — already in use) and `ILogger` (already using `NullLogger`). No mocking framework is needed for environment setup.

### NFR-4: Architectural compliance

The change must leave the Application layer with zero references to `Microsoft.Extensions.Hosting`. Verify with `dotnet build` and, optionally, a dependency audit (`dotnet list package` on the Application project).

## Data Model

No data model changes. `ApplicationConfiguration` (domain entity), `GetConfigurationRequest`, and `GetConfigurationResponse` are unchanged.

## API / Interface Design

No API surface changes. `GetConfigurationResponse.Environment` continues to carry the environment string; its value at runtime will be identical because both `IHostEnvironment.EnvironmentName` and `IConfiguration["ASPNETCORE_ENVIRONMENT"]` read from the same underlying environment variable in ASP.NET Core.

### Files changed

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` | Remove `IHostEnvironment` field, constructor param, `using` directive; replace `_environment.EnvironmentName` with config read |
| `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` | Remove `IHostEnvironment` mock from `CreateHandler`; add `"ASPNETCORE_ENVIRONMENT"` to config dict where environment identity matters |

## Dependencies

- `IConfiguration` / `Microsoft.Extensions.Configuration.Abstractions` — already a direct dependency of `GetConfigurationHandler`.
- `ConfigurationConstants.DEFAULT_ENVIRONMENT` — already defined in `Anela.Heblo.Domain.Features.Configuration.ConfigurationConstants` as `"Production"`.
- No new packages or services required.

## Out of Scope

- Changes to `ApplicationConfiguration`, `GetConfigurationRequest`, or `GetConfigurationResponse`.
- Changes to how `ASPNETCORE_ENVIRONMENT` is set in Docker, Azure App Service, or `launchSettings.json`.
- Adding new test cases beyond updating the existing four.
- Auditing other handlers for similar `IHostEnvironment` usage (may be filed as a follow-up if found).
- Any changes to the public `GET /configuration` endpoint behaviour.

## Open Questions

None.

## Status: COMPLETE
