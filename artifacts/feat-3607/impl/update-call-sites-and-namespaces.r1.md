# Implementation: update-call-sites-and-namespaces

## What was implemented
Updated the `using`/namespace references in the 5 remaining call sites that pointed at the old
`Anela.Heblo.Application.Features.GiftSettings` namespace, following the relocation of the
Application layer to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings` done in
the previous task. No route, DI registration name, or test logic changed — namespace/using lines
only.

## Files created/modified
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — updated the two `using` directives for `GetGiftSetting`/`SetGiftSetting` use cases to the new namespace
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — updated the `using Anela.Heblo.Application.Features.GiftSettings;` directive; the `services.AddGiftSettingsModule();` call site is unchanged
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs` — updated `using` directives for `Dto` and `GetGiftSetting`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs` — updated `using` directive for `SetGiftSetting`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs` — updated `using` directive for `SetGiftSetting`

## Tests
Ran `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.GiftSettings"`:
```
Test Run Successful.
Total tests: 13
     Passed: 13
 Total time: 1.7676 Seconds
```
All 13 GiftSettings tests (5 validator, 2 GetGiftSetting handler, 6 SetGiftSetting handler) pass — matching the pre-move test count exactly.

## How to verify
1. `dotnet build Anela.Heblo.sln` from the repo root — `Build succeeded.`, `0 Error(s)`.
2. `grep -rn "Anela\.Heblo\.Application\.Features\.GiftSettings" --include="*.cs" backend/` — no output (old namespace fully removed).
3. `grep -rn "Anela\.Heblo\.Domain\.Features\.Logistics\.GiftSettings\|Anela\.Heblo\.Persistence\.Logistics\.GiftSettings" --include="*.cs" backend/ | wc -l` — non-zero (Domain/Persistence references untouched, as required).
4. `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.GiftSettings"` — 13/13 pass.

## Notes
`dotnet build` produces one pre-existing, unrelated warning (`MSB3073`, exit code 134) from the `Anela.Heblo.AccessMatrixGen` post-build tool failing to parse `access-matrix.generated.json`. This is an environment/tooling issue orthogonal to this change — verified independently that it is not caused by the GiftSettings namespace move (the tool crashes trying to read a JSON file unrelated to GiftSettings, before any GiftSettings-specific compilation step). Build still reports `0 Error(s)` and succeeds overall. No new C# compiler warnings were introduced by this task's edits.

## Status
DONE
