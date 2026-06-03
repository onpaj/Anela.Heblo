# Specification: Graceful Handling of Malformed `LayoutJson` in `GetGridLayoutHandler`

## Summary
`GetGridLayoutHandler.Handle` currently lets `JsonException` escape when deserializing a corrupted `entity.LayoutJson`, surfacing a 500 to the client and breaking the affected grid. This feature wraps deserialization in a guarded block that logs a warning and returns a `null` layout, aligning the behavior with the existing "no saved layout" / "database error" fallback paths.

## Background
`GetGridLayoutHandler` (`backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`) returns a user's saved grid configuration (column order, widths, visibility) for a given `GridKey`. Two failure modes are already handled gracefully:

1. **Missing row** — returns `GetGridLayoutResponse { Layout = null }` (handler line 34–37).
2. **Database error** (`PostgresException`/`NpgsqlException`) — logs an error and returns `GetGridLayoutResponse { Layout = null }` (handler line 45–52).

A third failure mode is **unhandled**: malformed JSON in the `LayoutJson` column. `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` at line 39 throws `JsonException` on:

- Invalid JSON syntax (truncated, partially written data).
- Empty string (`JsonException: "The input does not contain any JSON tokens"`).
- Type mismatch caused by a `GridLayoutDto` schema change incompatible with previously stored payloads.

Under normal operation `SaveGridLayoutHandler` writes well-formed payloads, but corruption can occur via:

- An incorrect DB migration touching `LayoutJson`.
- Old rows predating a breaking change to `GridLayoutDto`.
- Manual DB edits during debugging or incident recovery.

A corrupt user-preference row should degrade to "no saved layout" so the grid falls back to its default column configuration, not produce a 500 that blocks the user from viewing the page.

## Functional Requirements

### FR-1: Catch `JsonException` during `LayoutJson` deserialization
`GetGridLayoutHandler.Handle` must catch `JsonException` thrown by `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` and treat it as "no saved layout exists".

**Acceptance criteria:**
- When `entity.LayoutJson` is malformed (invalid JSON, empty string, or schema-incompatible payload), `Handle` returns `GetGridLayoutResponse { Layout = null }`.
- No `JsonException` propagates out of `Handle`.
- The existing DB error path (`PostgresException`/`NpgsqlException`) continues to behave as before.
- The "no saved layout" path (entity is `null`) continues to behave as before.
- The happy path (well-formed JSON) returns the populated `GridLayoutDto` with `GridKey` and `LastModified` set from the entity, identical to current behavior.

### FR-2: Log a warning on deserialization failure
The handler must emit a structured warning log when deserialization fails, with enough context to investigate.

**Acceptance criteria:**
- Log level is `Warning` (not `Error`) — corrupt user-preference data is recoverable and does not indicate a system fault.
- Log message contains the phrase `Malformed LayoutJson` (used for log search and for asserting in tests).
- Structured log properties include `UserId` (the resolved `user.Id ?? user.Email`) and `GridKey` (from the request).
- The original `JsonException` is passed as the exception argument to `ILogger.LogWarning` so its message and stack trace are captured.
- Logging follows the same `ILogger<GetGridLayoutHandler>` instance already injected into the handler.

### FR-3: Preserve current behavior for the other three code paths
The change must be additive — no behavioral change to:

- Returning `null` when no row exists.
- Returning a populated `GridLayoutDto` when JSON is valid (with `GridKey` and `LastModified` copied from the entity, and a non-null result even when the JSON itself deserializes to `null` — current code uses `?? new GridLayoutDto()` as a fallback).
- Logging and returning `null` on Postgres/Npgsql exceptions.
- The `InvalidOperationException` thrown when the authenticated user has neither `Id` nor `Email`.

**Acceptance criteria:**
- All three existing tests in `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` continue to pass without modification:
  - `Handle_WhenNoSavedLayout_ReturnsNull`
  - `Handle_WhenSavedLayoutExists_ReturnsDeserializedDto`
  - `Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError`

### FR-4: Test coverage for the new path
Add xUnit tests that pin the new behavior.

**Acceptance criteria:**
- A test `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning`:
  - Arranges the repository to return a `GridLayout` whose `LayoutJson` is invalid (e.g. `"{not json"`).
  - Asserts the response's `Layout` is `null`.
  - Asserts `ILogger.Log` was invoked once at `LogLevel.Warning` with a message containing `"Malformed LayoutJson"` and the underlying `JsonException`.
- A test `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning` covers the empty-string variant.
- Tests use the existing `Mock<IGridLayoutRepository>`, `Mock<ICurrentUserService>`, `Mock<ILogger<GetGridLayoutHandler>>` setup pattern already in the file.

### FR-5: No changes outside the read path
`SaveGridLayoutHandler` and `ResetGridLayoutHandler` do not deserialize `LayoutJson` and must not be modified by this change.

**Acceptance criteria:**
- Diff is confined to `GetGridLayoutHandler.cs` and `GetGridLayoutHandlerTests.cs`.
- No changes to `GridLayoutDto`, `GridLayout` entity, the repository interface/implementation, or any contract DTOs.
- No changes to the public API surface (request/response shapes are unchanged).

## Non-Functional Requirements

### NFR-1: Performance
- The added `try`/`catch` is on the happy path's hot loop only when an exception is thrown; the normal-flow cost is a single Try-block entry which is negligible.
- No additional I/O, allocations, or async overhead introduced.

### NFR-2: Security
- The warning log must not include the raw `LayoutJson` content (could contain user-specific UI state — low sensitivity, but no reason to log payloads).
- Standard structured logging guarantees apply: no tokens, no PII beyond `UserId`/`GridKey` (already used in the existing error log).
- No new attack surface — the change only narrows an existing failure mode.

### NFR-3: Observability
- A warning log per malformed row is appropriate volume — these events should be rare (manual DB edits, migration errors, schema breaks). A spike would indicate a real incident worth investigating.
- Operators can search by `UserId`/`GridKey` to identify affected rows for cleanup or re-save.

### NFR-4: Compatibility
- No database schema changes.
- No API contract changes — callers see identical response shape.
- Frontend continues to receive `{ layout: null }` and falls back to default column order, which it already does for the "no saved layout" case.

## Data Model
No changes. Reference entities/DTOs:

- **`GridLayout`** (entity) — persisted row with `UserId`, `GridKey`, `LayoutJson` (string), `LastModified`.
- **`GridLayoutDto`** (`backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridLayoutDto.cs`) — wire format with `GridKey`, `Columns: List<GridColumnStateDto>`, `LastModified`.
- **`GetGridLayoutResponse`** — wraps `Layout: GridLayoutDto?`.

## API / Interface Design
No interface changes.

**Current control flow (relevant slice):**

```
Handle(request, ct)
  ├─ resolve userId (throws InvalidOperationException if missing)
  └─ try
       ├─ entity = repository.GetAsync(userId, gridKey, ct)
       ├─ if entity is null → return { Layout = null }
       ├─ dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new()
       ├─ dto.GridKey = entity.GridKey
       ├─ dto.LastModified = entity.LastModified
       └─ return { Layout = dto }
     catch (PostgresException or NpgsqlException) → log Error, return { Layout = null }
```

**Target control flow:**

```
Handle(request, ct)
  ├─ resolve userId (throws InvalidOperationException if missing)
  └─ try
       ├─ entity = repository.GetAsync(userId, gridKey, ct)
       ├─ if entity is null → return { Layout = null }
       ├─ GridLayoutDto? dto
       │   try
       │     dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)
       │   catch (JsonException ex)
       │     log Warning "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout"
       │     return { Layout = null }
       ├─ if dto is null → return { Layout = null }                  // preserves "?? new GridLayoutDto()" semantics below
       ├─ dto.GridKey = entity.GridKey
       ├─ dto.LastModified = entity.LastModified
       └─ return { Layout = dto }
     catch (PostgresException or NpgsqlException) → log Error, return { Layout = null }
```

**Assumption (noted in Open Questions):** When `JsonSerializer.Deserialize` legally returns `null` for the literal JSON value `"null"`, the brief's suggested fix returns `null` (no fallback `new GridLayoutDto()`). The current code uses `?? new GridLayoutDto()` for that case. This spec follows the brief and treats a `null` deserialization result the same as malformed JSON (returns `Layout = null`), since both indicate "no usable saved layout".

## Dependencies
- `System.Text.Json` (already referenced).
- `Microsoft.Extensions.Logging.Abstractions` (already injected as `ILogger<GetGridLayoutHandler>`).
- No new NuGet packages, services, or infrastructure.

## Out of Scope
- Detecting or rewriting corrupted rows (no automatic repair, no migration to clean up bad data).
- Surfacing the corruption to the client (response shape unchanged; frontend cannot distinguish "no saved layout" from "corrupt saved layout").
- Adding metrics/telemetry counters for corruption events — the warning log is sufficient signal.
- Changes to `SaveGridLayoutHandler` or `ResetGridLayoutHandler`.
- Schema-versioning `GridLayoutDto` or adding a `SchemaVersion` field to stored JSON.
- Auditing/alerting pipelines for the new warning log.

## Open Questions
None.

## Status: COMPLETE