## Module
GridLayouts

## Finding
`GridLayoutPersistenceException` lives in the Domain layer but carries a `SqlState` property that is a PostgreSQL-specific concept:

```csharp
// backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs:6
public class GridLayoutPersistenceException : Exception
{
    public string? SqlState { get; }   // ← PostgreSQL-specific

    public GridLayoutPersistenceException(string message, string? sqlState, Exception inner)
        : base(message, inner)
    {
        SqlState = sqlState;
    }
}
```

`SqlState` is populated by `PostgresExceptionTranslator` in the Persistence layer from `PostgresException.SqlState` (e.g. `"42P01"`, `"23505"`). Application-layer handlers then log it directly:

```csharp
// GetGridLayoutHandler.cs:63-65
_logger.LogError(ex,
    "Database error reading GridLayout ... SqlState={SqlState}",
    userId, request.GridKey, ex.SqlState);
```

The Domain layer is meant to be completely agnostic of the persistence technology. A `SqlState` string is a PostgreSQL wire-protocol detail — it has no meaning outside of Postgres. If the persistence layer were ever swapped (e.g. to SQL Server or a different EF Core provider), the Domain exception constructor signature would still require a `sqlState` parameter with no sensible value to supply.

## Why it matters
Clean Architecture: the Domain layer must not depend on or expose infrastructure-specific vocabulary. `SqlState` is owned by the Npgsql/PostgreSQL driver, not by the domain. Its presence on a Domain exception creates an upward leakage from Infrastructure into Domain, even though no code in the Persistence layer directly imports Domain types for this reason.

Additionally, the `sqlState` parameter is required by the constructor, so any code that creates `GridLayoutPersistenceException` (tests included) is forced to know about PostgreSQL error codes even when they are irrelevant.

## Suggested fix
Remove `SqlState` from `GridLayoutPersistenceException`. The exception message already contains enough context for diagnosis. If structured logging of the SQL state is still desired, log it from within `PostgresExceptionTranslator` before throwing (inside the Persistence layer, where Postgres types are appropriate), rather than propagating it through the domain type:

```csharp
// Domain: clean exception — no infrastructure fields
public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner) { }
}

// Persistence translator: log the detail where it belongs
_logger.LogDebug("GridLayout persistence error SqlState={SqlState}", sqlState);
throw new GridLayoutPersistenceException($"GridLayout persistence error during {operation}: {exception.Message}", exception);
```

Handlers catch the domain exception and log its message; the SQL state is preserved in the structured log emitted at the Persistence boundary.

---
_Filed by daily arch-review routine on 2026-06-07._