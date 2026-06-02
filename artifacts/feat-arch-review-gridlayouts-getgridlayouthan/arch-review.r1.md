# Architecture Review: Graceful Handling of Malformed `LayoutJson` in `GetGridLayoutHandler`

## Skip Design: true

This is a backend-only defensive-coding change inside an existing MediatR handler. No new UI, no new visual components, no schema changes, no public contract changes. The frontend already handles `{ layout: null }` for the "no saved layout" case, so no design work is implied.

## Architectural Fit Assessment

The change fits cleanly into existing patterns:

- **Vertical Slice Architecture** — All work stays inside `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/` and its mirror test folder. No cross-module impact, no contract changes (`GridLayoutDto`, `GetGridLayoutResponse`, `IGridLayoutRepository` untouched).
- **Existing error handling style** — The handler already uses an outer `try { ... } catch (Exception ex) when (ex is PostgresException or NpgsqlException)` block that logs and returns `{ Layout = null }`. The new path is a *narrower inner* catch around just the deserialize call, returning the same fallback shape. This is structurally consistent with the established failure-mode handling.
- **Established `JsonException` handling convention** — The codebase already follows a `catch (JsonException ex)` + structured log pattern in several places (`OrgChartService.cs:65`, `MeetingUserDirectory.cs:75`, `ClaudeMeetingTaskExtractor.cs:72`, several Smartsupp handlers, `JsonResponseParser.cs`). The proposed handler matches that convention: typed catch, structured log, no payload echoed.
- **Integration point with the frontend** — The response shape is preserved exactly, so the existing FE fallback to default column order works without any FE-side change.

The single subtle deviation from current behavior is the spec's choice to treat `JsonSerializer.Deserialize(...)` returning `null` (legal JSON value `"null"`) as "no usable layout" rather than the current `?? new GridLayoutDto()` fallback. This is a deliberate, documented spec amendment (see Specification Amendments below) and the right call: returning an empty `GridLayoutDto` from valid-but-null JSON would surface a `GridKey = ""` payload, which is worse than `Layout = null`.

## Proposed Architecture

### Component Overview

No new components. The change is internal to one handler.

```
GetGridLayoutHandler.Handle
├── resolve userId  (unchanged — throws InvalidOperationException if missing)
└── try (outer — DB failure path, unchanged)
    ├── entity = repository.GetAsync(...)
    ├── if entity is null → return { Layout = null }                    [unchanged]
    ├── try (inner — NEW)
    │   ├── dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)
    │   └── if dto is null → return { Layout = null }                   [spec amendment]
    │   catch (JsonException ex)
    │   ├── _logger.LogWarning(ex, "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout", userId, request.GridKey)
    │   └── return { Layout = null }
    ├── dto.GridKey = entity.GridKey                                    [unchanged]
    ├── dto.LastModified = entity.LastModified                          [unchanged]
    └── return { Layout = dto }                                         [unchanged]
    catch (PostgresException or NpgsqlException) → logError, return { Layout = null }  [unchanged]
```

### Key Design Decisions

#### Decision 1: Inner try around deserialize vs. extending the outer catch

**Options considered:**
- A. Add `or JsonException` to the existing outer `catch (Exception ex) when (...)` filter.
- B. Wrap only `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` in a dedicated inner `try`/`catch (JsonException ex)`.

**Chosen approach:** B (matches the spec).

**Rationale:**
- The outer catch logs at `Error` severity with DB-specific context (`SqlState`). Deserialization failures are not DB faults — conflating them would either emit misleading "Database error" log messages, or force a branch inside the catch on exception type. Either way, log fidelity drops.
- Severity is semantically different: DB exception = `Error` (system fault), `JsonException` = `Warning` (corrupt-but-recoverable user-preference data). A single outer catch cannot cleanly express both severities.
- An inner catch makes the "deserialize is a distinct, fallible step" explicit at the call site, which matches the existing convention in `OrgChartService.cs:65–69` and several Smartsupp handlers.

#### Decision 2: Treat `Deserialize(...)` returning `null` the same as `JsonException`

**Options considered:**
- A. Keep the current `?? new GridLayoutDto()` fallback — a literal JSON `"null"` payload yields an empty DTO with `GridKey` set from the entity.
- B. Treat `dto is null` as "no usable saved layout" and return `{ Layout = null }`.

**Chosen approach:** B (per spec FR-1 / API design section).

**Rationale:** A returns a synthesized DTO with zero columns and an entity-derived `GridKey`, which the FE would interpret as "the user has an explicit layout with no columns" — that's a worse failure mode than "no saved layout exists". B unifies both pathological cases ("malformed" and "literal null") under one well-defined fallback that the FE already handles correctly.

#### Decision 3: Log level `Warning` with no payload echo

**Options considered:**
- A. Log at `Error` with the raw `LayoutJson` content.
- B. Log at `Warning` with only `{UserId}` and `{GridKey}` structured properties plus the exception.

**Chosen approach:** B.

**Rationale:** Corruption events are recoverable per-user-per-grid and do not indicate a system fault — `Warning` is the correct severity. Echoing `LayoutJson` into the log would offer marginal debugging value (the `JsonException.Message` already includes the offending character/position) while broadening the surface area for accidental information leakage. Operators can locate the corrupt row by `UserId + GridKey` and inspect it directly.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits confined to:

```
backend/
├── src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/
│   └── GetGridLayoutHandler.cs                              ← MODIFY
└── test/Anela.Heblo.Tests/Features/GridLayouts/
    └── GetGridLayoutHandlerTests.cs                         ← MODIFY (add 2 tests)
```

No changes to `Contracts/`, repository, entity, module registration, or the `SaveGridLayoutHandler` / `ResetGridLayoutHandler` siblings.

### Interfaces and Contracts

No interface changes. All public surfaces are stable:

- `IRequestHandler<GetGridLayoutRequest, GetGridLayoutResponse>` — unchanged.
- `GetGridLayoutResponse { GridLayoutDto? Layout }` — unchanged.
- `IGridLayoutRepository.GetAsync` — unchanged.
- `GridLayoutDto`, `GridColumnStateDto` — unchanged.

### Implementation sketch (handler)

```csharp
public async Task<GetGridLayoutResponse> Handle(GetGridLayoutRequest request, CancellationToken cancellationToken)
{
    var user = _currentUserService.GetCurrentUser();
    var userId = user.Id ?? user.Email
        ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

    try
    {
        var entity = await _repository.GetAsync(userId, request.GridKey, cancellationToken);
        if (entity is null)
        {
            return new GetGridLayoutResponse { Layout = null };
        }

        GridLayoutDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
                userId, request.GridKey);
            return new GetGridLayoutResponse { Layout = null };
        }

        if (dto is null)
        {
            return new GetGridLayoutResponse { Layout = null };
        }

        dto.GridKey = entity.GridKey;
        dto.LastModified = entity.LastModified;
        return new GetGridLayoutResponse { Layout = dto };
    }
    catch (Exception ex) when (ex is PostgresException or NpgsqlException)
    {
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        _logger.LogError(ex,
            "Database error reading GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
            userId, request.GridKey, pgEx?.SqlState);
        return new GetGridLayoutResponse { Layout = null };
    }
}
```

### Data Flow

Read path for a corrupt row (the new case):

```
FE GET /api/grid-layouts/{gridKey}
  → MediatR → GetGridLayoutHandler.Handle
    → ICurrentUserService.GetCurrentUser → userId
    → IGridLayoutRepository.GetAsync(userId, gridKey) → GridLayout entity (LayoutJson = "{not json")
    → JsonSerializer.Deserialize<GridLayoutDto>(...) throws JsonException
      → ILogger.LogWarning(ex, "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout", userId, gridKey)
      → return { Layout = null }
  → Controller → 200 OK { layout: null }
  → FE falls back to default column order (existing behavior)
```

All other paths (no row, valid JSON, DB error, missing Id+Email) are byte-for-byte identical to today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Behavior change for stored literal JSON `"null"` — today returns an empty `GridLayoutDto` with the entity's `GridKey` and `LastModified`; under the spec it returns `Layout = null`. | Low | Documented as Decision 2 above. In practice no caller writes literal `"null"` — `SaveGridLayoutHandler.cs:37` always serializes a populated `GridLayoutDto`. FE treats both responses identically (falls back to defaults). |
| Silent loss of layout for a user whose row genuinely corrupted, with no upstream signal to operators. | Low | `Warning`-level structured log with `UserId` + `GridKey` is sufficient signal at expected event volume. Spec explicitly defers metrics/alerting to out-of-scope. |
| Hiding a real upstream bug (e.g., a future incompatible `GridLayoutDto` change causing widespread deserialization failure). | Low | A widespread regression would manifest as a spike in the `Malformed LayoutJson` warning, which is searchable by message phrase. The spec pins the message phrase in tests (FR-2), so the search target is stable. |
| Test brittleness from asserting on log message contents. | Low | Existing DB-error test (`Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError`) already uses the same `Moq.Verify` shape (`v.ToString()!.Contains(...)`). New tests should mirror that exact pattern. |
| Inner `try` accidentally swallows non-deserialize exceptions. | Negligible | Catch is typed (`JsonException`) — `NpgsqlException`, `OperationCanceledException`, etc., still propagate to the outer scope as intended. |
| Future maintainer extending the catch to `catch (Exception)`. | Low | Add a one-line comment on the `catch (JsonException ex)` to flag the intentional narrowness, or rely on the typed catch + tests to enforce the contract. Spec does not require a comment; recommend only if a maintainer finds it surprising. |

## Specification Amendments

1. **Make the "deserialized dto is null" branch explicit in the spec acceptance criteria.** The spec's API/Interface Design section captures this in the target control flow, but FR-1's acceptance criteria phrase the outcome only in terms of `JsonException`. Add an acceptance criterion to FR-1:

   > When `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` returns `null` (e.g., the stored payload is the literal JSON value `"null"`), `Handle` returns `GetGridLayoutResponse { Layout = null }` and does **not** log a warning (this is not a corruption event — it's a degenerate-but-valid value).

   Rationale: the spec already chose this behavior (Open Questions notes the deliberate divergence from `?? new GridLayoutDto()`), but the test plan in FR-4 should reflect it. Recommend adding a third test `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog` to pin the no-warning behavior; otherwise a future refactor could quietly start logging on this path.

2. **Log message phrasing — keep the FR-2 "contains the phrase `Malformed LayoutJson`" assertion, but adopt the brief's exact template** (`"Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout"`) verbatim in the implementation. The spec's flow diagram and the brief use slightly different prose; aligning on the brief's exact string keeps the log search target unambiguous and matches the existing DB-error log's stylistic pattern (`"Database error reading GridLayout for user={UserId} gridKey={GridKey} ..."`).

3. **No other spec changes required.** FR-1 through FR-5 are correctly scoped, the NFRs are accurate, and the Out of Scope list correctly defers schema versioning, repair, telemetry counters, and sibling-handler edits.

## Prerequisites

None. The change requires no:

- Database migrations or schema changes.
- New configuration values, environment variables, or Key Vault secrets.
- New NuGet packages — `System.Text.Json` and `Microsoft.Extensions.Logging.Abstractions` are already referenced.
- New service registrations — `GridLayoutsModule.cs` relies on MediatR assembly scanning; the handler signature is unchanged.
- New infrastructure or external dependencies.
- Frontend coordination — response shape is identical.

Implementation can start immediately against the current `main`.