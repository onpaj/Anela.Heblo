Committed successfully. Here is the output artifact:

---

# Implementation: GetConfigurationHandler IConfiguration Refactor

## What was implemented

Replaced the direct `System.Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION)` call in `GetVersionFromSources()` with `_configuration[ConfigurationConstants.APP_VERSION]`, routing the version read through the already-injected `IConfiguration` abstraction. The adjacent log message was updated from "environment variable" to "configuration" to stay accurate. Four new unit tests verify the full version-resolution fallback chain using in-memory configuration.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — line 76: swapped `Environment.GetEnvironmentVariable(...)` for `_configuration[...]`; line 79: updated log message
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — new file with 4 unit tests using `ConfigurationBuilder().AddInMemoryCollection()` and `NSubstitute` for `IHostEnvironment`

## Tests

- `GetConfigurationHandlerTests.cs` (4 new unit tests):
  - `Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet` — verifies config-sourced version is returned verbatim
  - `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty` — verifies fallback fires; asserts non-null and not DEFAULT_VERSION
  - `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent` — same, for absent key
  - `Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet` — regression guard confirming `UseMockAuth` wiring is unbroken
- All 9 configuration tests pass (5 existing integration + 4 new unit)

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Configuration"
# Expected: Passed 9, Failed 0
```

## Notes

- The `async Task` without `await` in `Handle()` is pre-existing and out of scope per NFR-4 (surgical change). Not touched.
- The `System.Reflection` using directive is retained — still needed for the assembly fallback branches.
- No new NuGet packages or DI registrations introduced.

## PR Summary

Removes the one remaining direct `System.Environment.GetEnvironmentVariable()` call from `GetConfigurationHandler`, replacing it with the already-injected `IConfiguration` instance. The `.NET` host wires environment variables into `IConfiguration` by default, so runtime behavior is identical in all environments; the change eliminates a hidden OS-level dependency and makes the version-resolution fallback chain unit-testable without manipulating process-level state.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — swap env-var call for `_configuration` indexer; update one log message for accuracy
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — 4 new handler-level unit tests covering config version, fallback, and UseMockAuth regression guard

## Status

DONE