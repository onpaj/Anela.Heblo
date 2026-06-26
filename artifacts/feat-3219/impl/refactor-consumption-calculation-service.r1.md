# refactor-consumption-calculation-service — Implementation Summary

## What was done

Refactored `ConsumptionCalculationService.ProcessDailyConsumptionAsync` in
`backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`
to consume the new `bool` return value from `IPackingMaterialRepository.AddDailyRunAsync`.

### Changes

1. **Reordered persistence calls** — daily run is now inserted (and its `SaveChangesAsync` committed) _before_ consumption rows are staged. Previously the order was reversed.

2. **Removed the EF Core / Npgsql catch block** — the `try/catch (Microsoft.EntityFrameworkCore.DbUpdateException)` around `SaveChangesAsync` is gone. Duplicate detection is now handled inside the repository and surfaced via the returned `bool`.

3. **Added bool-check in the service** — when `AddDailyRunAsync` returns `false` (duplicate detected), a `LogWarning` with event name `PackingMaterialsDailyRunDuplicateDetected` is emitted (text says "skipping") and the method returns `ProcessDailyConsumptionResult(false, 0)` early.

4. **Removed `IsDuplicateDailyRunViolation` private method** — no longer needed; the EF Core / Npgsql type references it held are fully eliminated from the file.

5. **Added comment** explaining the partial-success window introduced by splitting into two `SaveChangesAsync` calls, and how it compares to the prior behaviour.

### Verification

- `grep -n "EntityFrameworkCore|Npgsql|IsDuplicateDailyRunViolation"` on the file returned no output.
- `dotnet build backend/src/Anela.Heblo.Application/` completed with 0 errors (139 pre-existing warnings, unchanged).

## Commit

`aaf2693` — `@claude refactor-consumption-calculation-service: remove EF Core catch, consume bool from AddDailyRunAsync`
