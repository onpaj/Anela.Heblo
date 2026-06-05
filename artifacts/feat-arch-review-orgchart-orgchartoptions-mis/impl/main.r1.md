All 3 feature commits are on the branch. Here is the output summary:

---

# Implementation: OrgChartOptions Startup Validation

## What was implemented

Added startup-time validation to `OrgChartOptions.DataSourceUrl` so that a missing or empty URL causes the application host to fail fast at boot with a clear `OptionsValidationException` instead of producing an opaque `HttpRequestException`/`UriFormatException` on the first user request.

The change adopts the project's existing Options validation pattern (already used in 9 sibling modules) with one key correction from the arch-review: `[Required(AllowEmptyStrings = false)]` rather than plain `[Required]`, since `[Required]` alone does not reject empty strings on `string` properties under `ValidateDataAnnotations`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` — Added `using System.ComponentModel.DataAnnotations;` and `[Required(AllowEmptyStrings = false)]` attribute on `DataSourceUrl`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — Replaced `services.Configure<OrgChartOptions>(...)` with `AddOptions<OrgChartOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` chain
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` — **New file**: 3 xUnit tests using `HostBuilder` to verify `OptionsValidationException` is thrown for missing key, empty string, and that startup succeeds for a valid URL

## Tests

- `OrgChartModuleValidationTests.cs` — covers:
  - `StartAsync_throws_when_OrgChart_DataSourceUrl_is_missing` — validates missing config key throws
  - `StartAsync_throws_when_OrgChart_DataSourceUrl_is_empty_string` — validates empty string (most likely real-world misconfiguration) throws
  - `StartAsync_succeeds_when_OrgChart_DataSourceUrl_is_configured` — validates valid URL allows normal startup
- All 3 tests pass; regression check on the empty-string guard confirmed it specifically depends on `AllowEmptyStrings = false`
- Full test suite run: 4,376+ tests pass; 40 pre-existing Docker/Testcontainers failures are unrelated to this change

## How to verify

```bash
# Run just the new tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OrgChartModuleValidationTests"

# Build the full solution
dotnet build backend/Anela.Heblo.sln
```

## Notes

- `appsettings.json` already had a placeholder `OrgChart:DataSourceUrl` value, so no existing `WebApplicationFactory`-based tests needed changes.
- Final reviewer noted `OrgChartOptions` lacks `sealed` — this is consistent with other Options classes in the codebase and is non-blocking.
- The `System.Collections.Generic` using in the test file is technically redundant under .NET 8 global usings but harmless.

## PR Summary

Added startup-time validation to `OrgChartOptions.DataSourceUrl` so that a missing or blank Azure Key Vault secret (`OrgChart--DataSourceUrl`) causes the container to fail fast at boot with a clear `OptionsValidationException` rather than surfacing as an opaque `HttpRequestException` on the first user request.

The implementation adopts the project's standard pattern already used in nine sibling modules (`Article`, `Leaflet`, `KnowledgeBase`, etc.) — `OrgChartModule` was the only outlier still on the legacy `services.Configure<T>()` call. One spec correction was applied: `[Required(AllowEmptyStrings = false)]` is used instead of plain `[Required]`, because `[Required]` alone does not reject empty strings on `string` properties under `ValidateDataAnnotations`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` — Added `[Required(AllowEmptyStrings = false)]` to `DataSourceUrl`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — Swapped `services.Configure<T>()` for `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()`
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` — New: three xUnit cases covering missing key, empty string, and valid URL via `HostBuilder`

## Status
DONE