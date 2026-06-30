# Implementation: fix-tests

## What was implemented

Removed the `IHostEnvironment` mock (NSubstitute) from `GetConfigurationHandlerTests` and replaced it with an in-memory configuration entry (`ASPNETCORE_ENVIRONMENT = "Test"`). The `CreateHandler` helper now:

1. Calls `configData.TryAdd("ASPNETCORE_ENVIRONMENT", "Test")` so every test has an environment value without forcing callers to supply it.
2. Builds an `IConfiguration` from the supplied dictionary via `ConfigurationBuilder.AddInMemoryCollection`.
3. Constructs `GetConfigurationHandler(configuration, NullLogger<GetConfigurationHandler>.Instance)` — no `IHostEnvironment` parameter.

The four test cases are unchanged in intent.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — removed `using Microsoft.Extensions.Hosting`, `using NSubstitute`, dropped `IHostEnvironment` substitute, added `TryAdd` for `ASPNETCORE_ENVIRONMENT`.

## Tests

All 4 GetConfigurationHandlerTests passed.

```
Passed Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet
Passed Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty
Passed Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent
Passed Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet
Total tests: 4 | Passed: 4
```

## How to verify

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
```

## Notes

- The access matrix generator tool threw a pre-existing `JsonException` during the wider solution build — unrelated to this change.
- NSubstitute is no longer referenced by the test file; if it was only used here, the package reference may be a candidate for removal later (out of scope for this task).

## PR Summary

Removes the `IHostEnvironment` NSubstitute mock from `GetConfigurationHandlerTests`. Environment is now supplied via the in-memory `IConfiguration` provider (`ASPNETCORE_ENVIRONMENT = "Test"`), consistent with how the refactored `GetConfigurationHandler` reads environment name.

## Status

DONE
