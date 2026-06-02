All tasks complete. Here is the output summary:

---

# Implementation: Relocate Picking List Operation DTOs from Domain to Application

## What was implemented

Pure namespace relocation of `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking`. All 11 consumer files had their `using` directives flipped. The empty Domain `Picking/` folder was deleted.

One non-obvious fix beyond the plan's prescription: `PrintPickingListRequest.cs` has a property named `Carriers`, which caused C# to resolve `Carriers.Zasilkovna` etc. in the static `DefaultCarriers` initializer as the instance property rather than the enum type. Fixed by using fully qualified `Anela.Heblo.Domain.Features.Logistics.Carriers.X` names in the initializer.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` — new location (renamed from Domain)
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — new location; added `using Anela.Heblo.Domain.Features.Logistics;`; enum values in `DefaultCarriers` use fully qualified names to avoid shadowing by the `Carriers` property
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs` — new location (renamed from Domain)
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` — deleted (all 3 files + empty folder removed)
- 11 consumer files — one-line `using` directive flip each (4 Application, 2 Adapter, 5 test)

## Tests

No new tests added (per FR-6). Existing tests in:
- `backend/test/Anela.Heblo.Tests/` — 4214 passing (32 pre-existing infra failures, unchanged)
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/` — 82 passing

Only `using` directives updated in test files.

## How to verify

```bash
# No Domain references remain in source
grep -rln "Anela.Heblo.Domain.Features.Logistics.Picking" backend/src backend/test
# → no output

# Domain has no Application dependency
grep -rn "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/ || echo "CLEAN"
# → CLEAN

# Build and tests
dotnet build Anela.Heblo.sln --nologo  # 0 errors
dotnet test Anela.Heblo.sln --nologo   # matches baseline
```

## Notes

The plan prescribed bare `Carriers.X` enum values after adding `using Anela.Heblo.Domain.Features.Logistics;`. This fails at compile time because the class member `Carriers` (the property) takes precedence over the imported type name in the static initializer context (CS0236). The fix uses fully qualified enum values — the only deviation from the plan, and the correct resolution for the name collision.

## PR Summary

Restores Clean Architecture's dependency rule for the Logistics picking subsystem by moving `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` from the Domain layer to the Application layer. The three types carry use-case workflow concerns (`SendToPrinter`, `ChangeOrderState`, `ExportedFiles`) that have no business being in the most stable layer. Eleven consumer files across Application, Adapters.ShoptetApi, and two test projects received a one-line `using` directive flip each. The now-empty Domain `Picking/` folder was removed.

The only non-trivial detail: `PrintPickingListRequest` has a property named `Carriers` that shadows the `Carriers` enum type from the Domain namespace in the static `DefaultCarriers` initializer — resolved by using fully qualified `Anela.Heblo.Domain.Features.Logistics.Carriers.X` enum values there.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — created (moved from Domain); added `using` for Domain.Logistics; fully qualified enum values in `DefaultCarriers`
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` — created (moved from Domain); namespace flip only
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs` — created (moved from Domain); namespace flip only
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` — deleted (3 files + empty folder)
- 11 consumer files — one-line `using` directive flip each

## Status
DONE