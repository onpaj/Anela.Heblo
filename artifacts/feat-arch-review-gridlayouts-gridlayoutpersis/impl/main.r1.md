---

# Implementation: Remove PostgreSQL-Specific `SqlState` from Domain Exception

## What was implemented

Refactored `GridLayoutPersistenceException` to eliminate upward leakage of PostgreSQL infrastructure concepts into the Domain layer. The `SqlState` property was removed from the domain exception; structured `SqlState` logging was moved to `PostgresExceptionTranslator` in the Persistence layer, where the Postgres vocabulary belongs.

Two commits landed on the branch:
1. `refactor(persistence): PostgresExceptionTranslator becomes DI instance + logs SqlState/Operation at Warning`
2. `refactor(domain): drop SqlState from GridLayoutPersistenceException — log moves to Persistence translator`

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` — simplified to `(string message, Exception inner)` ctor, no using directives, no SqlState property (NFR-1 satisfied)
- `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — converted from `static class` to instance class with `ILogger<PostgresExceptionTranslator>` DI injection; emits `LogWarning(Operation, SqlState, Message)` before returning
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — added translator as ctor DI dependency; replaced 3 static calls with instance method calls
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — added `services.AddScoped<PostgresExceptionTranslator>()`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — removed `SqlState={SqlState}` and `ex.SqlState` from catch-block log
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — same
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` — same
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` — updated to construct translator with `Mock<ILogger>` / `NullLogger`; added 2 new tests verifying Warning-level log emission with `SqlState` and `Operation`; removed `result.SqlState.Should().BeNull()` assertion
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` — updated all 4 `new GridLayoutRepository(...)` calls to pass a translator instance
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — 2-arg exception ctor
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` — 2-arg exception ctor
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs` — 2-arg exception ctor
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` — 2-arg exception ctor

## Tests

- `PostgresExceptionTranslatorTests.cs` — 7 tests: direct NpgsqlException, DbUpdateException wrapping Npgsql, OperationCanceledException, DbUpdateConcurrencyException, plain exception, **new**: Warning log emitted with SqlState+Operation, **new**: no log for non-Postgres exceptions
- `GridLayoutRepositoryTranslationTests.cs` — 4 integration tests covering UpsertAsync and DeleteAsync throw/rethrow behavior
- Handler tests — all 4 handler test files updated; catch-block behavior tested with 2-arg exception ctor

**Result: 34/34 GridLayout tests pass.** Full solution: 4691 pass, 38 pre-existing failures in unrelated DB integration suites (KnowledgeBase, Article, etc.) that require live DB setup and are not affected by this change.

## How to verify

```bash
# Confirm no Npgsql in Domain
grep -rn "Npgsql" backend/src/Anela.Heblo.Domain/ || echo "OK"

# Confirm no ex.SqlState in handlers
grep -rn "SqlState" backend/src/Anela.Heblo.Application/Features/GridLayouts/ || echo "OK"

# Run GridLayouts test suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"
```

## Notes

- `PostgresExceptionLoggingInterceptor` was intentionally NOT modified — it already logs `SqlState` at Error level for `SaveChangesAsync` failures. On write paths there will now be a deliberate duplicate: the interceptor entry (Error, full PG context, no operation name) and the translator entry (Warning, operation correlation, covers read paths). Both are useful for incident triage and were documented as intentional per Architecture Review §Decision 3.
- The translator retains the `TryTranslateGridLayout(Exception, string) → GridLayoutPersistenceException?` nullable-return shape (not narrowed to `PostgresException`), per Architecture Review §Decision 2.

## PR Summary

Removes the PostgreSQL-specific `SqlState` property from `GridLayoutPersistenceException` in the Domain layer, restoring Clean Architecture compliance. The `SqlState` structured log field is preserved at the Persistence boundary — `PostgresExceptionTranslator` (now a DI instance class) emits a `Warning`-level log with `SqlState`, `Operation`, and message before throwing the translated exception, covering both read and write paths. Application handlers continue to catch the domain exception and log business context (userId, gridKey) without referencing any infrastructure-specific field.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` — simplified to 2-arg ctor, no Npgsql
- `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — instance class + ILogger DI + Warning log
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — translator injected via DI
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — translator registered as scoped service
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/*/` — removed `ex.SqlState` from all 3 handler log statements
- `backend/test/...` — translator tests updated + new log-emission tests; handler test fixtures use 2-arg ctor

## Status
DONE