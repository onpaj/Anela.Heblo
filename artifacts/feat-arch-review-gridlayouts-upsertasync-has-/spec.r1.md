# Specification: Atomic Upsert for GridLayoutRepository

## Summary
Replace the non-atomic select-then-insert/update pattern in `GridLayoutRepository.UpsertAsync` with a PostgreSQL `INSERT ... ON CONFLICT DO UPDATE` statement so concurrent layout saves from the same user become idempotent at the database level. This eliminates spurious unique-constraint failures that currently surface as `ErrorCodes.DatabaseError` to the user and cause silent layout-save losses.

## Background
`GridLayoutRepository.UpsertAsync` (`backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:36-70`) loads any existing `GridLayout` row for the `(UserId, GridKey)` tuple, then either updates it or inserts a new one and calls `SaveChangesAsync`. The `(UserId, GridKey)` unique index defined in `GridLayoutConfiguration` is the only thing keeping duplicates out, but the code does not exploit it. When two requests for the same `(UserId, GridKey)` interleave — typical for rapid grid column resize events whose debounce fires twice before the first save completes — both can read `null`, both attempt an `INSERT`, and the loser hits the unique index. The Postgres exception is translated by `PostgresExceptionTranslator.TryTranslateGridLayout` into a `GridLayoutPersistenceException`, then `SaveGridLayoutHandler` logs an error and returns `ErrorCodes.DatabaseError`. From the user's perspective the layout change vanishes despite identical intent ("last write wins"). The database already supports atomic upserts via `INSERT ... ON CONFLICT`; switching to it removes both the race and the read round-trip.

## Functional Requirements

### FR-1: Atomic upsert at the database level
`UpsertAsync` MUST persist the supplied `(userId, gridKey, layoutJson, lastModified)` in a single SQL statement that is atomic with respect to concurrent invocations for the same `(userId, gridKey)`. When a row for `(UserId, GridKey)` already exists, the statement MUST overwrite `LayoutJson` and `LastModified` with the supplied values. When no row exists, the statement MUST insert a new one. The implementation MUST use `INSERT INTO public."GridLayouts" ... ON CONFLICT ("UserId", "GridKey") DO UPDATE SET "LayoutJson" = EXCLUDED."LayoutJson", "LastModified" = EXCLUDED."LastModified"` executed through `DbContext.Database.ExecuteSqlInterpolatedAsync` (or `ExecuteSqlAsync`) so all values are passed as parameters, not concatenated into the SQL string.

**Acceptance criteria:**
- Two `UpsertAsync` calls in flight in parallel for the same `(userId, gridKey)` MUST both complete without throwing `GridLayoutPersistenceException`.
- After such a parallel pair, exactly one row exists in `GridLayouts` for that `(userId, gridKey)`, and its `LayoutJson` and `LastModified` equal the values supplied by the call that committed last (last-write-wins).
- A single `UpsertAsync` for a new `(userId, gridKey)` inserts exactly one row with the supplied values.
- A single `UpsertAsync` for an existing `(userId, gridKey)` updates the row in place — `Id` is unchanged.
- The SQL statement issues exactly one round-trip to PostgreSQL (no preceding `SELECT`).
- Parameters are sent via Npgsql parameterization, not string interpolation into the SQL text.

### FR-2: Preserve repository exception contract
`UpsertAsync` MUST continue to honour the `IGridLayoutRepository` xml-doc contract: PostgreSQL/Npgsql failures surface as `GridLayoutPersistenceException` (via `PostgresExceptionTranslator.TryTranslateGridLayout`); non-Npgsql exceptions are rethrown unchanged. Cancellation via `CancellationToken` MUST be propagated to the SQL execution call.

**Acceptance criteria:**
- A connection-level `NpgsqlException` thrown by the SQL execution surfaces as `GridLayoutPersistenceException` wrapping the original exception, with `SqlState` populated when the inner exception is a `PostgresException`.
- A non-Npgsql exception thrown by the SQL execution is rethrown unchanged.
- A cancelled `CancellationToken` results in an `OperationCanceledException` from the underlying call; the catch block does not translate it into `GridLayoutPersistenceException`.
- `SaveGridLayoutHandler` continues to map `GridLayoutPersistenceException` to `ErrorCodes.DatabaseError` with no code changes required there.

### FR-3: LastModified uses injected TimeProvider
`UpsertAsync` MUST compute `LastModified` from `TimeProvider.GetUtcNow().DateTime` (the existing injected `TimeProvider`), not from `DateTime.UtcNow`, so deterministic-time tests continue to work.

**Acceptance criteria:**
- A unit test that injects a fake `TimeProvider` with a fixed instant observes that fixed instant written to `LastModified` on both insert and update paths.

### FR-4: DeleteAsync left unchanged
`DeleteAsync` is explicitly out of scope (see Out of Scope). Its current behaviour and tests remain.

**Acceptance criteria:**
- `DeleteAsync` code and tests in `GridLayoutRepositoryTranslationTests` are not modified by this change.

### FR-5: Test coverage for concurrent upsert
A new integration test MUST exercise the concurrent-upsert path against a real PostgreSQL backend (Testcontainers) and assert that two parallel `UpsertAsync` calls for the same `(userId, gridKey)` both succeed and leave the table with a single row.

**Acceptance criteria:**
- Test runs against a real Postgres instance (Testcontainers or equivalent), not `UseInMemoryDatabase`.
- Test issues at least two `UpsertAsync` calls in parallel via `Task.WhenAll` for an identical `(userId, gridKey)`.
- Test asserts both tasks completed without exception.
- Test asserts the resulting row count for that `(userId, gridKey)` is exactly 1.
- Test asserts the persisted `LayoutJson` matches the payload of one of the two callers (whichever committed last).

### FR-6: Update or replace existing UpsertAsync translation tests
The existing `GridLayoutRepositoryTranslationTests.UpsertAsync_*` cases in `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` rely on `UseInMemoryDatabase` and override `SaveChangesAsync` to throw. Since the new implementation no longer calls `SaveChangesAsync`, those tests MUST be updated to exercise the new code path while still proving the exception-translation contract from FR-2.

**Acceptance criteria:**
- After the change, the test suite contains tests proving each branch of FR-2 (Npgsql translated, DbUpdateException-wrapping-Npgsql translated, non-Npgsql rethrown) for `UpsertAsync`.
- All tests in `GridLayoutRepositoryTranslationTests` pass.

## Non-Functional Requirements

### NFR-1: Performance
The new `UpsertAsync` MUST complete in one database round-trip (was: two — `SELECT` followed by `INSERT`/`UPDATE` inside `SaveChanges`). No noticeable latency regression at p99 for a single call. No additional locks beyond those already taken by the unique index on `(UserId, GridKey)`.

### NFR-2: Security
The SQL statement MUST parameterize `userId`, `gridKey`, `layoutJson`, and `lastModified`. Direct string concatenation of any of these into the SQL is forbidden (SQL-injection class, per global C# security rules). `ExecuteSqlInterpolatedAsync` or `ExecuteSqlAsync` with `FormattableString` provides parameterization automatically; `ExecuteSqlRawAsync` is acceptable only with `{0}`-style positional parameters bound via the params array, never with `string.Format`.

### NFR-3: Backwards compatibility
The `IGridLayoutRepository` interface signature, the `GridLayout` domain entity, the `GridLayoutConfiguration`, and the `GridLayouts` table schema MUST remain unchanged. No database migration is required.

### NFR-4: Observability
On translated persistence failure, the existing `SaveGridLayoutHandler` log (`Database error saving GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}`) MUST continue to function and continue to carry the original `SqlState` when available. The change MUST NOT introduce any new log line for the previously-erroring (normal-path) concurrent-save scenario — the success path is silent, as today.

### NFR-5: Database portability
The fix is PostgreSQL-specific. The project targets PostgreSQL exclusively (per `ApplicationDbContext` and existing `PostgresExceptionTranslator` usage), so use of `ON CONFLICT` is acceptable and does not require an abstraction layer.

## Data Model
No schema changes. The existing entity stays:

- `GridLayout` (`backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayout.cs`): `Id` (int identity PK), `UserId` (string, max 255), `GridKey` (string, max 100), `LayoutJson` (string, required), `LastModified` (DateTime, UTC).
- Table: `public."GridLayouts"`.
- Constraints: PK on `Id`; **unique index** on `(UserId, GridKey)` (`GridLayoutConfiguration:31-32`). The conflict target in the `ON CONFLICT` clause MUST be this `(UserId, GridKey)` pair.
- `Id` is auto-generated by PostgreSQL on insert; it is not supplied by the `INSERT` clause and is not touched on update.

## API / Interface Design
No public-API surface change. The change is purely internal to `Anela.Heblo.Persistence.GridLayouts.GridLayoutRepository`.

**Method signature (unchanged):**
```csharp
Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);
```

**Internal implementation sketch:**
```csharp
public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
{
    try
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public."GridLayouts" ("UserId", "GridKey", "LayoutJson", "LastModified")
            VALUES ({userId}, {gridKey}, {layoutJson}, {now})
            ON CONFLICT ("UserId", "GridKey") DO UPDATE
                SET "LayoutJson" = EXCLUDED."LayoutJson",
                    "LastModified" = EXCLUDED."LastModified"
            """,
            cancellationToken);
    }
    catch (Exception ex)
    {
        var translated = PostgresExceptionTranslator.TryTranslateGridLayout(ex, nameof(UpsertAsync));
        if (translated is not null) throw translated;
        throw;
    }
}
```

`GetAsync` and `DeleteAsync` retain their current EF Core implementations.

**Caller impact:** `SaveGridLayoutHandler` (`backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`) is unchanged — same `try/catch` over `GridLayoutPersistenceException`, same `ErrorCodes.DatabaseError` return on failure.

## Dependencies
- **PostgreSQL** ≥ 9.5 for `ON CONFLICT` (project already requires modern PostgreSQL — no version bump).
- **EF Core 8 / Npgsql** — already in use; `ExecuteSqlInterpolatedAsync` is part of the existing `Microsoft.EntityFrameworkCore` package.
- **Testcontainers for .NET (PostgreSQL module)** — required to host the integration test from FR-5. If not already in the test projects, add `Testcontainers.PostgreSql` package to `backend/test/Anela.Heblo.Tests`.
- Existing `PostgresExceptionTranslator` (`backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs`).
- Existing injected `TimeProvider`.

## Out of Scope
- `DeleteAsync` race condition. The brief notes the race is benign (deleting an already-deleted row is a no-op) and leaving the read-then-delete avoids touching unrelated code. Not changed in this work.
- `GetAsync` — already a single-statement read; no change.
- Any change to the `(UserId, GridKey)` unique index, table layout, or migrations.
- Changes to debounce logic on the frontend that produces the concurrent writes — the fix is server-side and idempotent.
- Conflict-resolution strategies other than last-write-wins (e.g. merging column states). The brief explicitly endorses last-write-wins as matching intended semantics.
- Adding telemetry/metrics for upsert latency.
- Refactoring `PostgresExceptionTranslator` to be more generic across repositories.
- Any change to `SaveGridLayoutHandler`, `ResetGridLayoutHandler`, DTOs, or the OpenAPI surface.

## Open Questions
None.

## Status: COMPLETE