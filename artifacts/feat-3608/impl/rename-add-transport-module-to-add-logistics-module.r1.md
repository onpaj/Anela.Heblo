# Implementation: rename-add-transport-module-to-add-logistics-module

## What was implemented
Renamed the DI-registration extension method `LogisticsModule.AddTransportModule()` to `AddLogisticsModule()`, aligning it with the codebase-wide `{Feature}Module.Add{Feature}Module()` naming convention. Updated the single call site and two stale documentation examples. Pure identifier rename — no change to method body, signature parameters, return type, or registered services.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — renamed method declaration on line 17 from `AddTransportModule` to `AddLogisticsModule` (body unchanged).
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — updated call site on line 92 from `services.AddTransportModule();` to `services.AddLogisticsModule();` (position in registration sequence unchanged, still between `AddManufactureModule(configuration)` and `AddGiftPackageManufactureModule()`).
- `docs/architecture/development_guidelines.md` — updated the "API Composition (Program.cs)" example code block, line 158, from `.AddTransportModule()` to `.AddLogisticsModule()` (position in fluent chain unchanged).
- `docs/architecture/infrastructure.md` — updated the "Feature modules" example code block, line 143, from `.AddTransportModule()` to `.AddLogisticsModule()` (position in fluent chain unchanged).

## Tests
No behavior change occurs, so no new unit tests were added. Verification performed:
- `dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj` completed with 0 errors (156 pre-existing warnings unrelated to this change, plus one pre-existing MSB3073 warning from an unrelated post-build code-gen tool exiting with code 134 — not caused by this change and not a build error).
- `dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include <touched files> --verify-no-changes` reported no formatting violations.
- Repo-wide `grep -rn "AddTransportModule"` confirmed zero remaining references outside the explicitly out-of-scope historical documents (`docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md` and prior arch-review artifacts under `artifacts/`, which are historical/pipeline-generated records and out of scope per the task context).

## How to verify
1. `grep -rn "AddTransportModule" backend docs` — should return no hits.
2. `grep -n "AddLogisticsModule" backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs backend/src/Anela.Heblo.Application/ApplicationModule.cs docs/architecture/development_guidelines.md docs/architecture/infrastructure.md` — should show the renamed method/call/examples in their original positions.
3. `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj` — should succeed with 0 errors.

## Notes
No deviations from the task context. The four in-scope locations matched exactly what was described (method declaration, call site, and two doc examples). Out-of-scope historical/artifact references (dated plan doc and prior arch-review reports under `artifacts/`) were intentionally left untouched, as instructed. Not pushed; no PR opened; per instructions this is committed to the current branch only.

## PR Summary
This change renames `LogisticsModule`'s DI-registration extension method from `AddTransportModule()` to `AddLogisticsModule()`, resolving the sole exception to the codebase-wide `{Feature}Module.Add{Feature}Module()` naming convention (e.g. `CatalogModule.AddCatalogModule()`, `PurchaseModule.AddPurchaseModule()`). The inconsistency was a leftover from when the module was renamed from "Transport" to "Logistics" and was flagged by the daily arch-review routine on 2026-07-12. This is a pure identifier rename with no behavior change: the method body, registered services, and call-site position are all preserved.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — method rename
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call-site update
- `docs/architecture/development_guidelines.md` — doc example update
- `docs/architecture/infrastructure.md` — doc example update

## Status
DONE
