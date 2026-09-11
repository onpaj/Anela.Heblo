# Implementation: wire-shared-instance-di-registration

## What was implemented
Changed the DI registration in `GiftPackageManufactureModule` so that `IGiftPackageManufactureService` and `IGiftPackageQueryService` are both aliased to a single shared scoped instance of the concrete `GiftPackageManufactureService`, instead of two independent `AddScoped<TInterface, TImpl>()` registrations that would each construct their own instance. This satisfies FR-4's requirement that both interfaces resolve to the same object within one DI scope, per architecture review Decision 2.

Added a focused DI-resolution test (`GiftPackageManufactureModuleTests`) that builds a real `ServiceProvider` from `AddGiftPackageManufactureModule()` (with mocked dependencies) and verifies:
1. Resolving both interfaces from the same scope returns the same instance.
2. Resolving from different scopes returns different instances (guards against over-correcting to a singleton).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs` — replaced the single `AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>()` line with three registrations: `AddScoped<GiftPackageManufactureService>()` as the shared scoped root, plus factory-delegate registrations for `IGiftPackageManufactureService` and `IGiftPackageQueryService` that both resolve `sp.GetRequiredService<GiftPackageManufactureService>()`.
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GiftPackageManufactureModuleTests.cs` — new test file (created exactly as specified in the task context) with two `[Fact]` tests covering same-scope shared-instance and different-scope distinct-instance behavior.

## Tests

**RED run** (before the registration change), `dotnet build Anela.Heblo.sln` succeeded first (0 errors), then:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufactureModuleTests"
```
Result: both tests failed with
```
System.InvalidOperationException : No service for type 'Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services.IGiftPackageQueryService' has been registered.
```
— the expected DI-registration failure reason (not a compile error), confirming the test fails for the right cause before the fix.

**GREEN run** (after the registration change), same command:
```
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 1 s - Anela.Heblo.Tests.dll (net8.0)
```
Both `Resolving_BothInterfaces_FromSameScope_ReturnsSameInstance` and `Resolving_BothInterfaces_FromDifferentScopes_ReturnsDifferentInstances` passed.

**Full solution build** (Step 5), `dotnet build Anela.Heblo.sln`:
```
0 Warning-relevant errors; 246 Warning(s), 0 Error(s)
```
Build succeeded cleanly with the registration change in place — `IGiftPackageManufactureService` still exposes all four methods at this point, so existing handlers (`GetAvailableGiftPackagesHandler`, `GetGiftPackageDetailHandler`) kept compiling unchanged.

## How to verify
```bash
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4115-Arch-Review-Logistics-Igiftpackagemanufactureservi
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufactureModuleTests"
```
Expect a successful build and both tests passing.

## Notes
No deviations from the task context. `AccessMatrixGen` non-fatal noise was not observed in this run's output. The `artifacts/feat-4115/state.json` modification visible in `git status` predates this task (was already modified before I started) and was not touched by this implementation.

## Status
DONE
