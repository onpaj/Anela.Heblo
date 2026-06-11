# Specification: Remove PostgreSQL-Specific `SqlState` from Domain Exception

## Summary
Remove the `SqlState` property from `GridLayoutPersistenceException` in the Domain layer to eliminate upward leakage of PostgreSQL/Npgsql infrastructure concepts into Domain. The SQL state will instead be logged from within `PostgresExceptionTranslator` in the Persistence layer, where the Postgres vocabulary is appropriate. Application-layer handlers will continue to catch the domain exception and log its message; the structured `SqlState` log field is preserved at the Persistence boundary.

## Background
The Domain layer in this Clean Architecture monorepo (`backend/src/Anela.Heblo.Domain`) must remain agnostic of persistence technology. `GridLayoutPersistenceException` currently exposes a `SqlState` property — a PostgreSQL wire-protocol error code (e.g. `42P01`, `23505`) populated by Npgsql's `PostgresException.SqlState`. This violates the Domain boundary:

- The Domain type carries an infrastructure-specific field with no meaning outside PostgreSQL.
- The constructor forces every caller (handlers, tests, future translators for other providers) to supply a `sqlState` argument even when the underlying provider has no equivalent concept.
- Swapping the EF Core provider (e.g. to SQL Server) would leave the parameter semantically empty.

The `SqlState` is currently consumed in two places:
1. `PostgresExceptionTranslator` (Persistence) — sets it when wrapping a `PostgresException`.
2. `GetGridLayoutHandler` and any sibling handlers (Application) — log it via structured logging.

Logging the SQL state is operationally valuable for diagnosing database errors, so it must be preserved — but emitted from the layer that owns the concept.

## Functional Requirements

### FR-1: Remove `SqlState` from the Domain exception
The `GridLayoutPersistenceException` class in `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` must no longer declare or expose a `SqlState` property, and its constructor must no longer accept a `sqlState` parameter.

**Acceptance criteria:**
- `GridLayoutPersistenceException` has a single public constructor: `(string message, Exception inner)`.
- The class has no public, protected, or internal property named `SqlState`.
- The class has no `using` directives or references to Npgsql, PostgreSQL, or other persistence-layer types.
- The Domain project compiles without referencing Npgsql.

### FR-2: Log `SqlState` from the Persistence translator
`PostgresExceptionTranslator` (in the Persistence layer) must log the PostgreSQL `SqlState` via structured logging *before* throwing the translated `GridLayoutPersistenceException`. The log entry must include enough context (operation name, original message) to correlate with the thrown exception.

**Acceptance criteria:**
- `PostgresExceptionTranslator` accepts an `ILogger<PostgresExceptionTranslator>` (or equivalent typed logger) via constructor injection. If it currently lacks DI, it is registered as a singleton or scoped service consistent with sibling translators.
- Before throwing, the translator emits a single structured log entry at an appropriate level (debug or warning — see NFR-3) with at minimum the fields: `SqlState`, `Operation`, and the original Postgres exception message.
- The thrown `GridLayoutPersistenceException` is constructed with the new two-argument form (message, inner).

### FR-3: Update all call sites of the exception constructor
Every construction site of `GridLayoutPersistenceException` — production code, fakes, and tests — must be updated to the new two-argument constructor.

**Acceptance criteria:**
- A repository-wide search for `new GridLayoutPersistenceException(` returns only the two-argument form.
- The solution builds with no warnings or errors related to this change.

### FR-4: Update handler logging
Application-layer handlers (e.g. `GetGridLayoutHandler` and any other handler catching `GridLayoutPersistenceException`) must no longer reference `ex.SqlState`. The handler log statement should retain the contextual fields (user id, grid key, operation) and log the exception message, but drop the `SqlState` token from the message template and arguments.

**Acceptance criteria:**
- A repository-wide search for `ex.SqlState` and `\.SqlState` inside `backend/src/Anela.Heblo.Application/Features/GridLayouts` returns no results.
- Handler log statements still include user id, grid key, and the operation context.
- The exception is still passed as the first argument to `_logger.LogError(ex, …)` so the inner exception chain is preserved.

### FR-5: Preserve test coverage
Existing tests that asserted on `SqlState` propagation must be updated to assert on the equivalent observable behavior: the persistence translator emits a log entry with `SqlState`, and the thrown exception carries the expected message and inner exception.

**Acceptance criteria:**
- All previously passing tests in `backend/test` related to `GridLayoutPersistenceException` or `PostgresExceptionTranslator` continue to pass after refactor.
- At least one test verifies `PostgresExceptionTranslator` logs the `SqlState` via a captured `ILogger` (e.g. using a test logger or `Microsoft.Extensions.Logging` fakes).
- No test references the removed `SqlState` property.

## Non-Functional Requirements

### NFR-1: Clean Architecture compliance
The Domain layer must not depend on Npgsql, EF Core, or any persistence provider. After this change, `dotnet list backend/src/Anela.Heblo.Domain package` must not include any persistence-provider packages, and no Domain source file may `using Npgsql;` or reference `PostgresException`.

### NFR-2: Backwards-incompatible API change is acceptable
Because this is an internal application (single deployable, solo developer), the constructor signature change is allowed without a deprecation period. All consumers will be updated in the same commit.

### NFR-3: Logging level and verbosity
The `SqlState` log entry in `PostgresExceptionTranslator` should be emitted at **Warning** level when an exception is translated (the translator only runs on error paths, so Debug would be silently dropped in production). The entry must not duplicate the full stack trace — handlers will still emit the `LogError(ex, …)` with the full exception. Assumption: Warning is preferred over Error to avoid double-alerting in monitoring; documented in Open Questions.

### NFR-4: No behavioral regression
Externally observable behavior (HTTP status codes, error responses, retry semantics) must be unchanged. Only the internal exception shape and the layer at which `SqlState` is logged change.

## Data Model
No data model changes. This is a code-structure refactor only — no database schema, EF Core entity, or DTO is affected.

## API / Interface Design

### Affected types

**Domain (after change)**
```csharp
// backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs
public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner) { }
}
```

**Persistence (after change)**
```csharp
// backend/src/Anela.Heblo.Persistence/...PostgresExceptionTranslator.cs
public class PostgresExceptionTranslator
{
    private readonly ILogger<PostgresExceptionTranslator> _logger;

    public PostgresExceptionTranslator(ILogger<PostgresExceptionTranslator> logger)
        => _logger = logger;

    public GridLayoutPersistenceException Translate(string operation, PostgresException ex)
    {
        _logger.LogWarning(
            "GridLayout persistence error during {Operation}: SqlState={SqlState} Message={Message}",
            operation, ex.SqlState, ex.Message);

        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {ex.Message}", ex);
    }
}
```

**Application handlers (after change)**
```csharp
catch (GridLayoutPersistenceException ex)
{
    _logger.LogError(ex,
        "Database error reading GridLayout for user {UserId} key {GridKey}",
        userId, request.GridKey);
    // …existing error response…
}
```

No public HTTP API, no MediatR request/response, no React component contracts change.

## Dependencies
- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` — modify.
- `backend/src/Anela.Heblo.Persistence/...` — `PostgresExceptionTranslator` (exact path to be located during implementation) — modify to accept `ILogger` and emit the structured log.
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/**/*Handler.cs` — drop `SqlState` from log templates.
- Persistence DI registration (`AddPersistence` or equivalent in `Anela.Heblo.Persistence`) — verify the translator is registered with DI (or remains a `new`-ed helper that receives the logger via the calling repository, depending on current style).
- `backend/test/**` — update unit tests for translator and handler.

No new NuGet packages. No external service dependencies.

## Out of Scope
- Renaming or moving `GridLayoutPersistenceException` to a different namespace or layer.
- Introducing a generic `PersistenceException` base class or applying the same refactor to other modules' exceptions. (If other modules have the same anti-pattern, file separate findings.)
- Changing the HTTP response shape returned for persistence errors.
- Adding telemetry/metrics beyond the existing logging.
- Refactoring how `PostgresExceptionTranslator` is wired into repositories (DI vs. manual construction) — keep the existing wiring style unless the logger injection forces a change.
- Database migrations or schema changes.

## Open Questions
None.

## Status: COMPLETE