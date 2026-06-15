# Specification: Atomic GridLayout Upsert

## Summary
Replace the read-then-write pattern in `GridLayoutRepository.UpsertAsync` with an atomic PostgreSQL `INSERT ... ON CONFLICT ... DO UPDATE` statement, eliminating a race condition where rapid concurrent saves from the same user on the same grid silently fail with a unique-constraint violation. The change makes layout persistence idempotent (last-write-wins) without altering the repository's public contract.

## Background
`GridLayoutRepository.UpsertAsync` (`backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:40-59`) currently:
1. Queries for an existing row by `(UserId, GridKey)`.
2. Either updates the found row or adds a new one.
3. Calls `SaveChangesAsync`.

The unique index on `(UserId, GridKey)` exists to prevent duplicates, but the application treats the natural race window between step 1 and step 3 as an unrecoverable error. The end-to-end frontend behaviour amplifies this: column resizes are debounced and posted to the API; when a user drags a column repeatedly, two debounced requests can arrive nearly simultaneously, both observe an empty table, both attempt `INSERT`, and the second fails. `PostgresExceptionTranslator.TryTranslateGridLayout` translates the duplicate-key exception into `GridLayoutPersistenceException`, which surfaces to the caller as `ErrorCodes.DatabaseError` and is logged at `Error` level. The user silently loses the layout change, and the log fills with false-positive errors for what is a normal interaction pattern.

The Postgres `INSERT ... ON CONFLICT ... DO UPDATE` (UPSERT) statement makes the operation atomic at the database level and is the canonical idiomatic fix.

## Functional Requirements

### FR-1: Atomic upsert at the database layer
`GridLayoutRepository.UpsertAsync` MUST persist the `(UserId, GridKey)` layout in a single round-trip using PostgreSQL's `INSERT ... ON CONFLICT ("UserId", "GridKey") DO UPDATE` syntax. The read-then-write pattern with `FirstOrDefaultAsync` followed by entity tracking MUST be removed.

**Acceptance criteria:**
- The new implementation issues exactly one SQL statement per call (verified by EF Core query logs in an integration test).
- The statement is parameterised — `userId`, `gridKey`, `layoutJson`, and the timestamp value are passed as parameters, never interpolated into the SQL string.
- When no row exists for `(UserId, GridKey)`, the row is created with the supplied values.
- When a row already exists, `LayoutJson` and `LastModified` are overwritten with the new values.
- `Id` of an existing row is not changed by the upsert.

### FR-2: Idempotency under concurrent calls
Two concurrent `UpsertAsync` calls for the same `(UserId, GridKey)` MUST both succeed. Whichever commits last determines the persisted `LayoutJson` value; neither caller observes a `GridLayoutPersistenceException` translated from a unique-violation.

**Acceptance criteria:**
- An integration test launches N (≥ 5) parallel `UpsertAsync` calls for the same `(UserId, GridKey)` with distinct payloads and asserts:
  - All tasks complete without throwing.
  - Exactly one row exists in `GridLayouts` for that `(UserId, GridKey)` after all tasks complete.
  - The persisted `LayoutJson` matches one of the supplied payloads.
- The same test launches N parallel calls with the same payload and asserts no error is thrown and a single row is persisted.

### FR-3: Timestamp semantics preserved
The new implementation MUST continue to stamp `LastModified` with `_timeProvider.GetUtcNow().DateTime`. The timestamp is computed in application code (not via SQL `NOW()`) so existing `TimeProvider`-based tests continue to control the clock.

**Acceptance criteria:**
- An integration test using a fake `TimeProvider` confirms `LastModified` on the persisted row equals the provider's current value at call time.
- The persisted `LastModified` is updated on every successful upsert, whether insert or update path.

### FR-4: Public repository contract unchanged
`IGridLayoutRepository.UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken)` MUST keep its current signature, return type, and exception semantics for non-race failures.

**Acceptance criteria:**
- Method signature is byte-identical to the current one.
- Calling code in `GridLayouts.Application` (handlers and any callers) is not modified.
- Database failures unrelated to the unique constraint (e.g. connection loss) still surface via the existing exception path, translated to `GridLayoutPersistenceException` and `ErrorCodes.DatabaseError`.
- `CancellationToken` is honoured — passing a cancelled token causes the call to throw `OperationCanceledException` (or the EF Core equivalent) before issuing SQL.

### FR-5: `DeleteAsync` left unchanged in this scope
`DeleteAsync` (lines 72-93) has the same read-then-write structure but the race is benign (delete-then-delete is idempotent). It MUST NOT be refactored as part of this work to keep the change surgical.

**Acceptance criteria:**
- `DeleteAsync` source is byte-identical before and after the change.
- A short note added to the repository (or PR description) records the rationale: race is benign, no behavioural defect to fix.

### FR-6: Exception translator path retained
`PostgresExceptionTranslator.TryTranslateGridLayout` and the wrapping `GridLayoutPersistenceException` MUST remain in place to handle non-race failures (e.g. constraint violations introduced by future schema changes, deadlocks, connection drops).

**Acceptance criteria:**
- The translator's call site in the repository remains.
- An integration test simulating a non-unique-violation Postgres error (e.g. via a too-long `GridKey` if a length constraint exists, or by injecting a broken connection) verifies the translator path still produces `GridLayoutPersistenceException`.

## Non-Functional Requirements

### NFR-1: Performance
- A successful upsert MUST complete in a single round-trip to PostgreSQL (no separate `SELECT` followed by `INSERT`/`UPDATE`).
- p95 latency for `UpsertAsync` measured in an integration benchmark MUST be no worse than the current implementation's update-path latency (the insert path becomes strictly faster by eliminating the `SELECT`).

### NFR-2: Security
- The SQL statement MUST use parameter placeholders (`{0}`, `{1}`, ... with `ExecuteSqlRawAsync` or `FormattableString` with `ExecuteSqlInterpolatedAsync`). No string concatenation, no interpolated user-controlled values. `userId` and `gridKey` are user-controlled inputs and MUST flow through parameters.
- The schema/table/column identifiers in the statement are hardcoded literals, not parameterised, matching existing EF Core conventions (`public."GridLayouts"`, `"UserId"`, `"GridKey"`, `"LayoutJson"`, `"LastModified"`).
- No additional logging of payload contents — `LayoutJson` may contain user preferences but is not classified as sensitive; existing log statements are retained as-is.

### NFR-3: Observability
- The spurious `LogError` previously emitted when the duplicate-key race fired MUST no longer occur during normal concurrent-save flows.
- Existing log statements for non-race errors remain unchanged.
- No new log statements are added for the happy path.

### NFR-4: Backwards compatibility
- No schema migration is required — the unique index `(UserId, GridKey)` already exists and is the target of `ON CONFLICT`.
- No API contract changes, no frontend changes.

### NFR-5: Test coverage
- Unit tests covering the existing select-then-write paths are updated or replaced; remove tests that asserted the obsolete `FirstOrDefaultAsync` behaviour.
- Integration tests against a real Postgres (Testcontainers or the existing integration-test harness) cover: insert path, update path, concurrent insert/insert race, concurrent update/update, and cancellation.
- Overall repository coverage stays at or above the project's 80% threshold.

## Data Model
No data-model changes.

Existing entity (unchanged):
```
GridLayouts
  Id           uuid (PK)
  UserId       text
  GridKey      text
  LayoutJson   text
  LastModified timestamp
  UNIQUE (UserId, GridKey)
```

The `ON CONFLICT ("UserId", "GridKey")` clause relies on the existing unique index. If the index is ever renamed or its columns reordered, this implementation must be updated in lockstep.

## API / Interface Design

### Repository method (unchanged signature)
```csharp
public Task UpsertAsync(
    string userId,
    string gridKey,
    string layoutJson,
    CancellationToken cancellationToken);
```

### SQL issued (new internal behaviour)
```sql
INSERT INTO public."GridLayouts" ("Id", "UserId", "GridKey", "LayoutJson", "LastModified")
VALUES (@id, @userId, @gridKey, @layoutJson, @lastModified)
ON CONFLICT ("UserId", "GridKey") DO UPDATE
   SET "LayoutJson"   = EXCLUDED."LayoutJson",
       "LastModified" = EXCLUDED."LastModified";
```

Implementation notes:
- Use `ExecuteSqlInterpolatedAsync` (preferred over `ExecuteSqlRawAsync` for compile-time parameter safety) on `_context.Database`.
- `Id` is supplied at call time using `Guid.NewGuid()` so the insert path produces a stable, application-generated identifier matching the current entity's `Id` initialisation. On conflict, the supplied `Id` is discarded and the existing row's `Id` is preserved.
- If the existing entity's `Id` is database-generated rather than application-generated, drop `Id` from the column list and let the default apply on insert. This must be verified by reading the entity configuration before implementation.

### No frontend or external API surface changes
The change is wholly internal to the persistence layer.

## Dependencies
- PostgreSQL ≥ 9.5 for `INSERT ... ON CONFLICT` syntax (already required by the project).
- EF Core's `RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync` (already available — `Microsoft.EntityFrameworkCore.Relational`).
- No new NuGet packages.
- `TimeProvider` already injected into the repository.

## Out of Scope
- Refactoring `DeleteAsync` (race is benign — see FR-5).
- Changing the unique index, schema, or migration history.
- Frontend changes to debouncing, request-coalescing, or optimistic UI on the layout-save flow.
- Replacing other read-then-write patterns elsewhere in the codebase. If similar races exist in other repositories, they are tracked separately.
- Introducing a generalised "upsert" helper or extension method — YAGNI; one call site, one SQL statement.
- Restructuring the `PostgresExceptionTranslator` — it is still used for non-race failures.

## Open Questions
None.

## Status: COMPLETE