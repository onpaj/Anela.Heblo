## Module
GridLayouts

## Finding
All three MediatR handlers in the GridLayouts module directly import and catch `NpgsqlException` and `PostgresException` from the Npgsql package — which is a concrete infrastructure (PostgreSQL driver) dependency:

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — `using Npgsql;` (line 7), catch block lines 45–51
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — `using Npgsql;` (line 9), catch block lines 44–50
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` — `using Npgsql;` (line 5), catch block lines 34–40

Clean Architecture requires that the Application layer depends only on domain abstractions. `IGridLayoutRepository` is the right abstraction; `NpgsqlException` is a leaking infrastructure detail that belongs exclusively in the Persistence layer.

## Why it matters
- Gives the Application layer a compile-time dependency on the Npgsql assembly, violating the Application → Domain dependency direction.
- Makes the repository contract non-substitutable: an in-memory or SQLite test double cannot produce `NpgsqlException`, so the catch block is dead in unit tests and the handlers behave differently under mocking than in production.
- The existing unit tests themselves import `NpgsqlException` (`GetGridLayoutHandlerTests.cs` line 8, `SaveGridLayoutHandlerTests.cs` line 9, `ResetGridLayoutHandlerTests.cs` line 5), coupling the test suite to the driver.

## Suggested fix
Introduce a domain exception in `Domain/Features/GridLayouts/`:

```csharp
public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner) { }
}
```

Have `GridLayoutRepository` (Persistence layer) catch `NpgsqlException`/`PostgresException` and rethrow as `GridLayoutPersistenceException`. The three handlers then catch only the domain exception — zero Npgsql dependency in Application or its tests.

---
_Filed by daily arch-review routine on 2026-05-29._