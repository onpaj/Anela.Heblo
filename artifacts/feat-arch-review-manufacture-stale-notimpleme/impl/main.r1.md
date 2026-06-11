Both reviews passed. The implementation is complete and committed.

---

# Implementation: Remove Dead `NotImplementedManufactureProtocolRenderer` DI Placeholder

## What was implemented

Removed the dead `NotImplementedManufactureProtocolRenderer` DI placeholder from the Application layer. The placeholder (which threw `NotImplementedException`) was a Phase 6 relic that was silently overridden at runtime by the real `QuestPdfManufactureProtocolRenderer` registration in the API composition root. Removing it makes missing-binding bugs surface at startup (via `ValidateOnBuild`) instead of as deferred 500s at request time.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — removed 4 lines: the Phase 6 comment, the `AddScoped` placeholder registration, and the now-unused `using` directive
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs` — **deleted** (13 lines, entire file)

## Tests
No new tests were added. Per arch-review Decision 2 and Spec Amendment 1, the existing test covers FR-4:
- `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` (1/1 passed) — runs with `ValidateOnBuild=true + ValidateScopes=true`, guards the production composition root
- Manufacture feature tests: 525/525 passed
- Module boundary tests: 17/17 passed
- Full suite: 0 failures

## How to verify
```bash
dotnet build backend/backend.sln -c Debug
dotnet test backend/backend.sln --no-build -c Debug
grep -rn "NotImplementedManufactureProtocolRenderer" backend   # expect: empty
```

## Notes
None. Change is exactly 2 files, net deletions only. `QuestPdfManufactureProtocolRenderer` in `ServiceCollectionExtensions.cs:152` is now the sole `IManufactureProtocolRenderer` binding. Commit: `212904c1`.

## PR Summary

Removes the `NotImplementedManufactureProtocolRenderer` placeholder that was registered in the Application module as a Phase 6 TODO stub. The real `QuestPdfManufactureProtocolRenderer` shipped in Phase 6 and has been the active binding in the API composition root ever since. The placeholder's registration was silently overridden at runtime (last-registration-wins), so it had no functional effect — but it masked the hard DI dependency, meaning any future host calling `AddManufactureModule()` without the API composition root would pass startup validation and fail at request time with `NotImplementedException` instead of an `InvalidOperationException` from DI.

Deleting the two-line registration and the now-orphaned class file ensures missing-binding bugs surface at container build via `ValidateOnBuild=true`, which `CompositionRootTests` already enforces for the production host.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — removed Phase 6 comment, `AddScoped` placeholder registration, and orphaned `using` directive (4 lines deleted)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs` — deleted entirely (13 lines)

## Status
DONE