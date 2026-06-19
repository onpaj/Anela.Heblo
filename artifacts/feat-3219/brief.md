## Module
PackingMaterials

## Finding
`ConsumptionCalculationService` in the Application layer directly imports and inspects infrastructure-specific exception types from EF Core and Npgsql:

**`backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs:79–84`**
```csharp
catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsDuplicateDailyRunViolation(ex))
{
    _logger.LogWarning("...");
    return new ProcessDailyConsumptionResult(false, 0);
}
```

**Lines 123–126**
```csharp
private static bool IsDuplicateDailyRunViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex) =>
    ex.InnerException is Npgsql.PostgresException pg
    && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
    && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);
```

The Application layer is responsible for orchestrating business logic; it must not know about the persistence technology or database engine in use.

## Why it matters
This violates Clean Architecture's dependency rule: the Application layer must not depend on Infrastructure types. By catching `DbUpdateException` and pattern-matching on `Npgsql.PostgresException` with a hardcoded constraint name, the Application service is:
- Coupled to EF Core (if persistence changes, this breaks)
- Coupled to PostgreSQL specifically (the constraint name and `SqlState` check are database-engine specific)
- Mixing exception-handling infrastructure concerns into business logic

## Suggested fix
Move the duplicate-run detection into the Persistence layer where it belongs. Two options, in order of preference:

1. **Preferred**: In `PackingMaterialRepository.AddDailyRunAsync` (or a dedicated `TryAddDailyRunAsync`), catch the EF Core/Npgsql exception and return a domain-meaningful boolean or throw a domain exception (e.g., `DailyRunAlreadyProcessedException`). The Application service then reacts to the domain signal only.

2. **Simpler**: Add `HasDailyProcessingBeenRunAsync` as a pre-check inside `AddDailyRunAsync` using a database-level idempotent insert (`INSERT ... ON CONFLICT DO NOTHING` via raw SQL or EF Core's `ExecuteSqlRaw`), returning a boolean that tells the caller whether the row was actually inserted. The Application service never sees the database exception.

---
_Filed by daily arch-review routine on 2026-06-18._
