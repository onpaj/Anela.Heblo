I have enough context. The translator is currently a static helper; the repository wraps each method in a `try/catch` that calls `TryTranslateGridLayout`. There are three handlers logging `ex.SqlState`. The persistence layer also has a separate `PostgresExceptionLoggingInterceptor` that already logs `SqlState` (plus Severity, Detail, TableName, ConstraintName) on `SaveChanges` failures — important context the spec doesn't mention. Writing the review.

# Architecture Review: Remove PostgreSQL-Specific `SqlState` from Domain Exception

## Skip Design: true

Backend-only Clean Architecture refactor. No new visual components, screens, or design decisions.

## Architectural Fit Assessment

The change directly **restores** Clean Architecture compliance — it does not introduce a new pattern. `Anela.Heblo.Domain` should be infrastructure-agnostic; the `SqlState` field on `GridLayoutPersistenceException` is a textbook upward leak from Npgsql into Domain. Removing it brings GridLayouts in line with the rest of the Domain layer (which has no Npgsql dependency).

**Integration points:**
- `GridLayoutRepository` (Persistence) — only production caller of `TryTranslateGridLayout`.
- `GetGridLayoutHandler` / `SaveGridLayoutHandler` / `ResetGridLayoutHandler` (Application) — three handler log statements reference `ex.SqlState`.
- Tests in `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/` and `backend/test/Anela.Heblo.Tests/Features/GridLayouts/` — five test files construct `GridLayoutPersistenceException` and one asserts on `SqlState`.

**Existing patterns that constrain the design:**
- `PostgresExceptionLoggingInterceptor` (Persistence, registered scoped in `PersistenceModule.cs:83`) **already logs `SqlState` at Error level on every `SaveChangesAsync` failure**, including `Severity`, `Detail`, `TableName`, and `ConstraintName`. The interceptor is wired into the `ApplicationDbContext` via `options.AddInterceptors(...)`.
  - Consequence: for **write paths** (Upsert/Delete), `SqlState` is already in structured logs without any translator change. Adding a translator log on write paths produces a duplicate entry.
  - For **read paths** (`GetAsync`), the `SaveChangesInterceptor` does **not** fire — so the translator is the only place that can capture `SqlState` for reads. This is the real gap the spec must close.
- `PostgresExceptionTranslator` is a **static class** today. Its sibling `PostgresExceptionLoggingInterceptor` is an instance class with `ILogger` injected via DI. Converting the translator to an instance class with DI aligns with the sibling and matches the spec's example.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Domain layer (Anela.Heblo.Domain)                                    │
│                                                                      │
│   GridLayoutPersistenceException                                     │
│     - ctor(string message, Exception inner)         ← simplified     │
│     - NO SqlState property                                           │
│     - NO using Npgsql                                                │
└──────────────────────────────────────────────────────────────────────┘
                              ▲ thrown
                              │
┌──────────────────────────────┴───────────────────────────────────────┐
│ Persistence layer (Anela.Heblo.Persistence)                          │
│                                                                      │
│   GridLayoutRepository                                               │
│     ├─ catch (Exception ex) → _translator.TryTranslate(ex, op)       │
│     └─ throws GridLayoutPersistenceException (no SqlState arg)       │
│                                                                      │
│   PostgresExceptionTranslator  ← becomes instance class              │
│     - ctor(ILogger<PostgresExceptionTranslator>)                     │
│     - TryTranslateGridLayout(ex, operation):                         │
│         • finds Npgsql/PostgresException in chain                    │
│         • LOGS SqlState (Warning) ONCE here                          │
│         • returns new GridLayoutPersistenceException(msg, inner)     │
│                                                                      │
│   PostgresExceptionLoggingInterceptor    [UNCHANGED]                 │
│     - logs SqlState at Error level on every SaveChanges failure      │
│                                                                      │
│   PersistenceModule.AddPersistence(...)                              │
│     - services.AddScoped<PostgresExceptionTranslator>();   ← new     │
└──────────────────────────────────────────────────────────────────────┘
                              ▲ thrown
                              │
┌──────────────────────────────┴───────────────────────────────────────┐
│ Application layer (Anela.Heblo.Application)                          │
│                                                                      │
│   {Get|Save|Reset}GridLayoutHandler                                  │
│     catch (GridLayoutPersistenceException ex)                        │
│       _logger.LogError(ex,                                           │
│         "Database error … user={UserId} gridKey={GridKey}",          │
│         userId, request.GridKey);                                    │
│       // SqlState NOT referenced — owned by Persistence layer        │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Translator becomes instance class with DI, not static + logger parameter

**Options considered:**
- (A) Keep `PostgresExceptionTranslator` static; add `ILogger` as a method parameter; inject `ILogger<GridLayoutRepository>` into the repo and forward it.
- (B) Convert translator to an instance class taking `ILogger<PostgresExceptionTranslator>` via constructor; register scoped in `PersistenceModule`; inject into `GridLayoutRepository`.

**Chosen approach:** **(B) instance + DI.**

**Rationale:**
- Matches the sibling `PostgresExceptionLoggingInterceptor` in the same `Persistence/Infrastructure/` folder, which is already an instance class with `ILogger` injected.
- ILogger-as-constructor-dep is the idiomatic .NET pattern; passing a logger to a static helper method is unusual and forces every caller to know about the logger requirement.
- The category `ILogger<PostgresExceptionTranslator>` correctly attributes the log entry to the translator in structured logs, rather than to the repository.
- One-line DI registration is cheap; YAGNI does not favor static here because the static form pollutes the caller signature.
- Matches the example in `spec.r1.md` (§API / Interface Design).

#### Decision 2: Keep the `TryTranslate` (nullable-return) semantics — do NOT switch to a throwing `Translate`

**Options considered:**
- (A) Preserve the existing `TryTranslateGridLayout(Exception, string operation) -> GridLayoutPersistenceException?` shape; `null` signals "not a Postgres exception, caller rethrows original".
- (B) Adopt the spec's example shape `Translate(string operation, PostgresException ex)` which presumes the caller has already filtered.

**Chosen approach:** **(A) keep `TryTranslate`.**

**Rationale:**
- The repository pattern at `GridLayoutRepository.cs:25-33` catches the broad `Exception`, asks the translator if it can handle it, and rethrows the original on `null`. Switching to (B) forces the repo to pre-filter `NpgsqlException`/`DbUpdateException` inline — duplicating logic the translator already centralizes (`FindNpgsqlException` recursive unwrap).
- The spec's example is illustrative, not binding; FR-3 says "two-argument form (message, inner)" for the exception ctor, which (A) satisfies.
- Spec amendment proposed below to clarify.

#### Decision 3: Log at **Warning**, accept benign duplication for write paths

**Options considered:**
- (A) Log `SqlState` at Warning in the translator on every translation (read + write paths).
- (B) Log at Debug in the translator and rely on `PostgresExceptionLoggingInterceptor` (Error level) for write paths — accept that read paths get no `SqlState` in production.
- (C) Log at Warning in the translator only for non-`DbUpdateException` chains (read paths); skip for write paths because the interceptor already covers them.

**Chosen approach:** **(A) Warning, accept the duplicate.**

**Rationale:**
- The interceptor entry (Error, with full PG context: Severity/Detail/Table/Constraint) and the translator entry (Warning, with `Operation` correlation context) carry **different correlation handles**: the interceptor logs from the EF Core boundary with no operation name; the translator knows the repository operation (`GetAsync`/`UpsertAsync`/`DeleteAsync`). They are complementary, not redundant — a single log line per failure cannot satisfy both vantage points.
- (B) silently drops `SqlState` for read failures in production (Debug is filtered out) — the spec's NFR-3 explicitly rejects this.
- (C) introduces conditional logic based on exception shape, which is brittle (`DbUpdateException` wrapping vs. raw `NpgsqlException` on `FirstOrDefaultAsync` vs. unknown future shapes). Simpler to log unconditionally and accept the duplicate; both entries are useful in incident triage.
- The handler's `LogError(ex, …)` at the Application layer adds a third entry, but it already drops `SqlState` and only adds business context (user/grid). All three layers serve a distinct purpose.

#### Decision 4: Do not introduce an `IPostgresExceptionTranslator` interface

**Options considered:**
- (A) Inject the concrete `PostgresExceptionTranslator` class into the repository.
- (B) Define `IPostgresExceptionTranslator` for testability.

**Chosen approach:** **(A) concrete class.**

**Rationale:** YAGNI. The translator has one production consumer (`GridLayoutRepository`), no behavioral variants, and is already covered by direct unit tests in `PostgresExceptionTranslatorTests`. The repository's behavior is verified end-to-end by `GridLayoutRepositoryTranslationTests` using the real translator. An interface would only be justified if other modules adopted the same pattern — out of scope per spec.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` | Drop `SqlState` property and three-arg ctor; keep `(string message, Exception inner)`. |
| `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` | `static class` → `class`. Add private `ILogger<PostgresExceptionTranslator>` field + ctor. Convert `TryTranslateGridLayout` to instance method. Emit `_logger.LogWarning(...)` with `Operation`, `SqlState`, `Message` before returning. Update XML doc to mention the SqlState log. |
| `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` | Add `PostgresExceptionTranslator` (concrete) as ctor dep; replace `PostgresExceptionTranslator.TryTranslateGridLayout(...)` calls with `_translator.TryTranslateGridLayout(...)`. |
| `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` | Add `services.AddScoped<PostgresExceptionTranslator>();` near line 83 alongside `PostgresExceptionLoggingInterceptor` registration. |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` | Remove `SqlState={SqlState}` token from template (line 69); drop `ex.SqlState` argument (line 70). |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` | Same edits at lines 41–42. |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` | Same edits at lines 36–37. |
| `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` | Construct translator with `Mock<ILogger<PostgresExceptionTranslator>>`. Delete the `result.SqlState.Should().BeNull();` assertion (line 24). Add a new `[Fact]` that verifies `logger.Verify(... LogLevel.Warning ...)` includes `SqlState` and `Operation` when a `PostgresException` is in the chain. |
| `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` | Pass a translator instance (with a `NullLogger`) into the new `GridLayoutRepository` ctor. |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`, `GridLayoutHandlerTests.cs`, `ResetGridLayoutHandlerTests.cs`, `SaveGridLayoutHandlerTests.cs` | Update `new GridLayoutPersistenceException(...)` call sites to the two-arg form `(message, inner)`. |

**Files explicitly NOT touched:** `IGridLayoutRepository.cs` (doc-only references), `PhotobankRepository.cs`, `DataQualitySchemaHealthCheck.cs` — those use `PostgresException.SqlState` directly (Persistence/API layer, no Domain leakage).

### Interfaces and Contracts

**Domain exception (final):**

```csharp
namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner) { }
}
```

**Translator (final):**

```csharp
namespace Anela.Heblo.Persistence.Infrastructure;

public class PostgresExceptionTranslator
{
    private readonly ILogger<PostgresExceptionTranslator> _logger;

    public PostgresExceptionTranslator(ILogger<PostgresExceptionTranslator> logger)
        => _logger = logger;

    public GridLayoutPersistenceException? TryTranslateGridLayout(Exception exception, string operation)
    {
        var npgsqlEx = FindNpgsqlException(exception);
        if (npgsqlEx is null) return null;

        var sqlState = (npgsqlEx as PostgresException)?.SqlState;
        _logger.LogWarning(
            "GridLayout persistence error during {Operation}: SqlState={SqlState} Message={Message}",
            operation, sqlState, exception.Message);

        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            exception);
    }

    private static NpgsqlException? FindNpgsqlException(Exception? exception) => exception switch
    {
        null => null,
        NpgsqlException npg => npg,
        _ => FindNpgsqlException(exception.InnerException)
    };
}
```

**Repository constructor (final):**

```csharp
public GridLayoutRepository(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    PostgresExceptionTranslator translator)
{
    _context = context;
    _timeProvider = timeProvider;
    _translator = translator;
}
```

**Handler `catch` block template (final):**

```csharp
catch (GridLayoutPersistenceException ex)
{
    _logger.LogError(ex,
        "Database error {operation} GridLayout for user={UserId} gridKey={GridKey}",
        userId, request.GridKey);
    return new {Verb}GridLayoutResponse(ErrorCodes.DatabaseError);
}
```

(Get handler returns `{ Layout = null }` as today — preserve existing return value per file.)

### Data Flow

Failure path (read example, `GetGridLayoutHandler`):

```
1. Controller → MediatR → GetGridLayoutHandler.Handle
2. Handler → _repository.GetAsync(userId, gridKey, ct)
3. EF Core → Npgsql → throws NpgsqlException (e.g. connection terminated)
4. GridLayoutRepository.GetAsync catch:
     translated = _translator.TryTranslateGridLayout(ex, "GetAsync")
       └─ logs Warning: "...SqlState=08006 Message=..."   ← only entry for reads
       └─ returns GridLayoutPersistenceException(msg, inner)
     throw translated;
5. Handler catch (GridLayoutPersistenceException ex):
     logs Error with userId + gridKey + full exception chain
     returns Layout = null
```

Failure path (write example, `SaveGridLayoutHandler`):

```
1. … → _repository.UpsertAsync(...)
2. SaveChangesAsync → DbUpdateException wrapping PostgresException(SqlState=23505)
3. PostgresExceptionLoggingInterceptor.SaveChangesFailed fires FIRST
     └─ logs Error: "PostgresException SqlState=23505 Severity=... Detail=... TableName=... ConstraintName=..."
4. Exception propagates to repository catch:
     translated = _translator.TryTranslateGridLayout(ex, "UpsertAsync")
       └─ logs Warning: "...SqlState=23505 Operation=UpsertAsync"   ← duplicate of (3), but correlated to operation
       └─ returns GridLayoutPersistenceException(msg, inner)
     throw translated;
5. Handler catch:
     logs Error with userId + gridKey + full exception chain
     returns SaveGridLayoutResponse(ErrorCodes.DatabaseError)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Duplicate log entries on write paths (interceptor + translator both emit `SqlState`). | Low | Documented and accepted (Decision 3). Different log categories and levels make them distinguishable; both carry distinct correlation context. |
| Test for translator's log emission becomes brittle (`Mock<ILogger>.Verify` against template strings). | Medium | Verify on the `LogLevel.Warning`, `EventId`, and the formatter's `ContainsKey("SqlState")`/`ContainsKey("Operation")` using the standard `ILogger` extension-method matching idiom — don't pin the exact message string. |
| Repository constructor signature change breaks any unlisted test setup (`new GridLayoutRepository(...)`). | Low | Repo-wide grep `new GridLayoutRepository(` before merge; only `GridLayoutRepositoryTranslationTests` constructs it directly today. |
| Future module copies the GridLayouts pattern and re-introduces the leak. | Low | Out of scope per spec, but capture as a follow-up finding: consider a generic `PersistenceException` base class in Domain (no infra fields) and a per-module derived type. |
| Removing `SqlState` from the exception breaks an external dashboard or alert that filters on the property. | Low | This is an internal app (solo developer, single deployable per project facts). Log-side filters on `SqlState` continue to work — the field is preserved in structured logs at the Persistence boundary. |

## Specification Amendments

1. **§FR-2 / §API design example uses a non-nullable `Translate(string, PostgresException)` signature.** The actual production helper returns `GridLayoutPersistenceException?` (the `TryTranslate` pattern) so the caller can rethrow non-Postgres exceptions unchanged. The implementation must preserve the nullable-return `TryTranslateGridLayout(Exception exception, string operation)` shape — do **not** narrow the parameter to `PostgresException` (the repo catches broad `Exception` and lets the translator unwrap). The spec's code block is illustrative only.

2. **§NFR-3 (logging level) — call out the duplicate.** The spec should acknowledge that `PostgresExceptionLoggingInterceptor` already logs `SqlState` at Error level for `SaveChangesAsync` failures. The translator's Warning entry is deliberate: it adds the **operation correlation** (`GetAsync`/`UpsertAsync`/`DeleteAsync`) the interceptor cannot supply and covers read paths the interceptor does not see. Both entries are intentional.

3. **§Dependencies — add `PersistenceModule.cs`.** The translator must be registered: `services.AddScoped<PostgresExceptionTranslator>();`. Spec says "verify the translator is registered" — confirming: today it is **not** registered (static class). The change explicitly requires a new DI registration.

4. **§FR-5 / Acceptance criteria — clarify the assertion target.** The new translator log test should verify the call via `Mock<ILogger<PostgresExceptionTranslator>>.Verify(...)` matching `LogLevel.Warning` and the structured state containing keys `SqlState` and `Operation`. Don't assert on the exact message template.

## Prerequisites

None. No migrations, no config, no infrastructure prerequisites. The change is purely code-structural and can land in a single commit per NFR-2.

- `dotnet build` and `dotnet format` must pass after edits.
- All `backend/test` projects must pass.
- Confirm `dotnet list backend/src/Anela.Heblo.Domain package` shows no Npgsql or EF Core packages (NFR-1 verification step).