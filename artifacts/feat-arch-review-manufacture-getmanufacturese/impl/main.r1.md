All 5 commits are on the branch. Here is the final output summary:

---

# Implementation: Migrate `ManufactureGroupId` to Typed Options Pattern

## What was implemented

Replaced `IConfiguration`-based access to `ManufactureGroupId` in `GetManufactureSettingsHandler` with strongly-typed `IOptions<ManufactureErpOptions>`. Added the property to `ManufactureErpOptions`, rewrote handler tests with `Options.Create(...)` (including a new whitespace test case), refactored the handler to use `IsNullOrWhiteSpace`, deleted the now-unused `ManufactureConfigurationKeys` constants file, and moved the `appsettings.json` placeholder from root level into the nested `ManufactureErp` section.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs` — added `public string? ManufactureGroupId { get; set; }` with XML doc
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` — replaced `IConfiguration` with `IOptions<ManufactureErpOptions>`, `IsNullOrWhiteSpace`, stored `options.Value` at construction
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` — **deleted**
- `backend/src/Anela.Heblo.API/appsettings.json` — removed top-level `ManufactureGroupId`, added `ManufactureGroupId` inside `ManufactureErp` section
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — removed top-level `ManufactureGroupId` documentation placeholder
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` — rewrote with `Options.Create(...)`, added whitespace test, added two constructor-null tests

## Tests

`GetManufactureSettingsHandlerTests.cs` — 6 tests all passing:
1. `Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse`
2. `Handle_WhenManufactureGroupIdMissing_ReturnsNull`
3. `Handle_WhenManufactureGroupIdEmpty_ReturnsNull`
4. `Handle_WhenManufactureGroupIdWhitespace_ReturnsNull` *(new — per Spec Amendment 3)*
5. `Constructor_WhenOptionsNull_ThrowsArgumentNullException`
6. `Constructor_WhenLoggerNull_ThrowsArgumentNullException`

`GetManufactureSettingsEndpointTests.cs` — 3 tests pass, file **untouched** (per Spec Amendment 1).

Full solution: `dotnet build` clean, `dotnet format --verify-no-changes` clean, 4595 tests pass (38 Docker-dependent integration failures are pre-existing and unrelated).

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetManufactureSettings"
grep -rn "ManufactureConfigurationKeys" .   # expect: no matches
grep -rn '"ManufactureGroupId"' src/        # expect: exactly 1 match (inside ManufactureErp section)
```

## Notes

**⚠️ Required deployment coordination before merge (NFR-3 + Spec Amendment 2):**

The Azure App Service environment variable key has changed:

| | Before | After |
|---|---|---|
| Env var | `ManufactureGroupId` | `ManufactureErp__ManufactureGroupId` |
| Config path | `ManufactureGroupId` (root) | `ManufactureErp:ManufactureGroupId` (nested) |

Required actions in Azure Portal **before** the new image is promoted:
1. **Production Web App:** Add `ManufactureErp__ManufactureGroupId` with the current value of `ManufactureGroupId`. Keep both vars set until the new image is healthy (rollback safety), then remove the legacy var.
2. **Staging Web App (if applicable):** Check whether it also sets the legacy `ManufactureGroupId` env var; if yes, apply the same rename.
3. **Any other environment** that overrides this value: same treatment.

Without step 1, the endpoint silently returns `null` post-deploy and frontend "responsible person" gating degrades.

## PR Summary

Migrates `ManufactureGroupId` consumption in `GetManufactureSettingsHandler` from raw `IConfiguration` to the typed `IOptions<ManufactureErpOptions>` pattern. Adds the property to `ManufactureErpOptions`, deletes the now-unused `ManufactureConfigurationKeys`, moves the `appsettings.json` placeholder into the nested `ManufactureErp` section, and updates unit tests with full whitespace-handling coverage. Behavioral change: null/empty/whitespace now all collapse to `null` (previously only null/empty). HTTP contract is unchanged.

**⚠️ Required deployment coordination — DO NOT MERGE before this is done:** The production binding key changes from `ManufactureGroupId` (top-level) to `ManufactureErp__ManufactureGroupId` (nested, double-underscore). Before the new image is promoted, add `ManufactureErp__ManufactureGroupId` in the Production (and Staging, if applicable) Azure Web App settings with the current value, and keep the old var until the new image is healthy. Without this, the endpoint silently returns `null` and frontend gating degrades.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs` — added `ManufactureGroupId` nullable string property
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` — replaced `IConfiguration` with `IOptions<ManufactureErpOptions>`, `IsNullOrWhiteSpace`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` — deleted (unused)
- `backend/src/Anela.Heblo.API/appsettings.json` — moved `ManufactureGroupId` from root into `ManufactureErp` section
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — removed stale top-level placeholder
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` — rewrote with typed options, added whitespace + constructor-null tests

## Status
DONE