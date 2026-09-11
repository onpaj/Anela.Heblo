# Implementation: remove-duplicate-registrations-from-application-module

## What was implemented
Removed the duplicate `GiftPackageManufacture` and `GiftSettings` module registrations (and their now-unused `using` directives) from `ApplicationModule.cs`. These sub-features are already registered by `LogisticsModule.AddLogisticsModule()` (added in the prior task `add-subfeature-registrations-to-logistics-module`), so registering them a second time directly in `ApplicationModule.AddApplicationServices()` was redundant.

## Files created/modified
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — removed `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;`, removed `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;`, removed the `services.AddGiftPackageManufactureModule();` call, and removed the `services.AddGiftSettingsModule();` call.

## Tests
No new tests required — this is a pure DI-registration cleanup task with no new behavior. Existing test suite (including any tests that resolve Logistics/GiftPackageManufacture/GiftSettings services via the composition root) verifies the services are still resolvable through `AddLogisticsModule()`.

## How to verify
1. `grep -n "GiftPackageManufacture\|GiftSettings" backend/src/Anela.Heblo.Application/ApplicationModule.cs` — returns no matches (previously 4: 2 `using` + 2 call lines).
2. `grep -n "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — confirms both calls remain present there, so the services are still registered exactly once, via the Logistics module.
3. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — `Build succeeded.`, 0 errors, no new warnings introduced by this change.

## Notes
No deviations from the task context. The two removed registration calls were confirmed to be true duplicates (identical calls already present in `LogisticsModule.cs`) before removal, per Step 1's grep check in the task spec.

## PR Summary
Removed duplicate `AddGiftPackageManufactureModule()` and `AddGiftSettingsModule()` registrations (and their now-unused `using` directives) from `ApplicationModule.cs`, since `LogisticsModule.AddLogisticsModule()` already registers both sub-features. Pure cleanup, no behavior change.

### Changes
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — removed 2 unused `using` directives and 2 duplicate service-registration calls

## Status
DONE
