## Module
Configuration

## Finding
`ConfigurationConstants.ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT"` is declared in `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:9` but is never referenced anywhere in the codebase. Every caller that reads the environment name uses one of:
- `IHostEnvironment.EnvironmentName` (in `GetConfigurationHandler.cs:63`) — the correct approach
- The raw string `"ASPNETCORE_ENVIRONMENT"` passed to `Environment.GetEnvironmentVariable()` (in `DiagnosticsController.cs:31,44,86,108`, `E2ETestController.cs:51`, `CostOptimizedTelemetryProcessor.cs:95`, `DesignTimeDbContextFactory.cs:17`)

No site uses `ConfigurationConstants.ASPNETCORE_ENVIRONMENT`.

## Why it matters
A constant that nobody uses provides false reassurance that a pattern is in place. It adds noise when reading `ConfigurationConstants.cs`, and the raw-string callers still exist unguarded — so the constant doesn't even serve its intended safety purpose.

## Suggested fix
Delete line 9 (`public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";`) from `ConfigurationConstants.cs`. The sites that use the raw string in `DiagnosticsController` and `CostOptimizedTelemetryProcessor` should switch to `IHostEnvironment.EnvironmentName` (already injected or available via DI) if a single source of truth is desired — but that is a separate, lower-priority cleanup.

---
_Filed by daily arch-review routine on 2026-06-03._