# Implementation: add-subfeature-registrations-to-logistics-module

## What was implemented
Added two `using` directives and two DI registration calls to `LogisticsModule.AddLogisticsModule()` so that the `GiftPackageManufacture` and `GiftSettings` sub-feature modules are registered directly from the Logistics module, in preparation for removing the duplicate registrations currently owned by `ApplicationModule.cs` (a later task).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — added `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;` and `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;` (alphabetically ordered with the existing `Anela.Heblo.Application.Features.*` group), and added `services.AddGiftPackageManufactureModule();` and `services.AddGiftSettingsModule();` calls (with a preceding comment) immediately before `return services;` in `AddLogisticsModule()`.

## Tests
None — mechanical DI registration change.

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — build succeeds with 0 errors (139 pre-existing warnings unrelated to this change, no new warnings/errors introduced).
2. Inspect the diff on `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` to confirm the two new `using` lines and the two new registration calls match the task spec exactly.

## Notes
As called out in the task description, `ApplicationModule.cs` still separately calls `AddGiftPackageManufactureModule()` / `AddGiftSettingsModule()` at this point, so both modules are now registered twice (once from `ApplicationModule`, once from `LogisticsModule`). This is a valid, non-failing no-op at build time — it only matters at runtime — and is expected to be resolved by a later task that removes the duplicate calls from `ApplicationModule.cs`. No deviations from the specified change.

## PR Summary
Registers the `GiftPackageManufacture` and `GiftSettings` sub-feature modules from `LogisticsModule.AddLogisticsModule()`, moving ownership of these registrations into the Logistics module ahead of a follow-up task that removes the now-duplicate registrations from `ApplicationModule.cs`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — added two `using` directives and two DI registration calls (`AddGiftPackageManufactureModule()`, `AddGiftSettingsModule()`) at the end of `AddLogisticsModule()`.

## Status
DONE
