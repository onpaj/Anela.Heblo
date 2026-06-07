## Module
Configuration

## Finding
`GetConfigurationHandler` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`) injects `IConfiguration` and uses it correctly for `UseMockAuth` (line 66). However, the version resolution at line 76 calls `System.Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION)` directly, bypassing `IConfiguration` entirely:

```csharp
// line 66 — correct: uses IConfiguration abstraction
var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);

// line 76 — inconsistent: bypasses IConfiguration, calls OS API directly
var version = Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION);
```

`IConfiguration` already aggregates environment variables (via `AddEnvironmentVariables()` in the host builder), so `_configuration[ConfigurationConstants.APP_VERSION]` would return the same value through the established abstraction layer.

## Why it matters
1. **Testability gap.** Unit tests cannot mock `System.Environment.GetEnvironmentVariable()`. Testing the three-level version fallback chain (env var → informational version → assembly version) requires setting real process-level environment variables, which is fragile in parallel test runs.
2. **Inconsistency.** The handler mixes two different mechanisms for reading configuration. A future developer will not expect `IConfiguration`-injected but APP_VERSION read via OS API.
3. **DI contract violation.** The handler constructor advertises `IConfiguration` as its configuration source; the direct `System.Environment` call is an undeclared hidden dependency.

## Suggested fix
Replace the direct OS call with the injected `IConfiguration`:

```csharp
// In GetVersionFromSources():
var version = _configuration[ConfigurationConstants.APP_VERSION];
```

`IConfiguration` resolves environment variables in the same priority order (env vars override appsettings), so behaviour is identical in all environments. Remove the `Environment.GetEnvironmentVariable` call and its `using` is no longer needed for that purpose.

---
_Filed by daily arch-review routine on 2026-06-03._