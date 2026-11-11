# Implementation: add-orgchart-wiring-test

## What was implemented
Added `OrgChartModuleWiringTests`, which calls `AddApplicationServices` on a fresh `ServiceCollection` (empty in-memory config, Development `IHostEnvironment` stub) and asserts an `IValidateOptions<OrgChartOptions>` descriptor is registered.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleWiringTests.cs` - new wiring test

## Tests
All 16 OrgChart tests pass. Mutation check: with `services.AddOrgChartServices(configuration);` commented out in `ApplicationModule.cs`, the new test fails; line restored afterward.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~OrgChart"`

## Notes
Issue premise was stale: wiring already exists at ApplicationModule.cs:112, so Program.cs was intentionally not changed. dotnet format verify and build clean.

## PR Summary
Adds a regression test guarding that `ApplicationModule.AddApplicationServices` registers the OrgChart module (and thus its startup options validation). The issue premise was stale: the wiring already exists, so no production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleWiringTests.cs` - descriptor-inspection wiring test

## Status
DONE
