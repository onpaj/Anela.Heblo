I have enough context. Producing the architecture review now.

# Architecture Review: Decouple GridLayouts Application Handlers from Npgsql

## Skip Design: true

## Architectural Fit Assessment

This change restores the Clean Architecture dependency direction (Application → Domain only) without introducing any new patterns. The proposal is well-aligned with what already exists:

- `IGridLayoutRepository` is defined in `Anela.Heblo.Domain.Features.GridLayouts` — the right place for the new exception.
- `GridLayoutRepository` lives in `Anela.Heblo.Persistence.GridLayouts` and uses EF Core (`ApplicationDbContext`). It is the natural boundary to translate driver exceptions.
- The codebase already has `PostgresExceptionLoggingInterceptor` in `Persistence/Infrastructure/` whose `UnwrapPostgresException` recursion is the exact pattern needed for the translation. We should mirror that logic instead of inventing a new one.

Two **integration points** matter:

1. **EF Core wrapping.** The repository calls `_context.SaveChangesAsync()` and `FirstOrDefaultAsync`. In production these can surface either a direct `NpgsqlException` (connection-level) or a `DbUpdateException` whose `InnerException` is `PostgresException` (constraint violations). The translation must handle both — the current handlers only `catch (Exception ex) when (ex is PostgresException or NpgsqlException)`, which means today they only catch the connection-level case; `DbUpdateException` wrapping is **already a latent bug** not addressed by the brief. Spec FR-2 hints at this in passing; this review elevates it.
2. **`SqlState` logging in handlers.** All three handlers currently log a structured `{SqlState}` field extracted from `PostgresException`. Strict preservation of this log shape (NFR-1) is impossible if the handler may not import `Npgsql`. There are three options — see Decision 2.

Three other Application files still import `Npgsql` (`Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`). The brief explicitly scopes this work to GridLayouts; they remain out of scope but should be noted as the next candidates for the same refactor.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API (MVC controllers)                                    │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ MediatR send
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application                                              │
│   GridLayouts handlers                                               │
│     catch (GridLayoutPersistenceException ex)  ← domain exception    │
│       → log with ex.SqlState                                         │
│       → return MediatR response with ErrorCodes.DatabaseError        │
│   ✗ no "using Npgsql;"                                               │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ IGridLayoutRepository (domain)
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Domain.Features.GridLayouts                              │
│   IGridLayoutRepository (existing)                                   │
│   GridLayout (existing)                                              │
│   GridLayoutPersistenceException  ← NEW                              │
│     - Message (string)                                               │
│     - SqlState (string?) ← extracted at translation time             │
│     - InnerException (Exception)                                     │
│   ✗ no infrastructure references                                     │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ implemented by
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Persistence.GridLayouts                                  │
│   GridLayoutRepository (EF Core)                                     │
│     each method wraps DbContext call in                              │
│     PostgresExceptionTranslator.Translate(...)                       │
│   ── Persistence.Infrastructure ──                                   │
│   PostgresExceptionLoggingInterceptor (existing, untouched)          │
│   PostgresExceptionTranslator  ← NEW (or static helper)              │
│     - reuses unwrap recursion from the interceptor                   │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where to perform the Npgsql → domain translation

**Options considered:**
- (A) Inline `try/catch` in every repository method.
- (B) A private static helper (`PostgresExceptionTranslator.Translate`) in `Persistence/Infrastructure/` that the repository wraps each operation with.
- (C) A `SaveChangesInterceptor` that throws the domain exception on save failure.

**Chosen approach:** (B) — a small static translator in `Anela.Heblo.Persistence.Infrastructure.PostgresExceptionTranslator`.

**Rationale:** Inline try/catch (A) duplicates the unwrap logic three times in one file and risks drift with future repository methods. Interceptor-only (C) does not cover read paths (`FirstOrDefaultAsync`) where connection-level `NpgsqlException` is still possible. The static helper centralizes the unwrap recursion (same shape as `PostgresExceptionLoggingInterceptor.UnwrapPostgresException`), keeps the repository readable, and is trivially unit-testable. Future feature modules that need the same translation can reuse it (or copy the pattern for their own domain exception).

#### Decision 2: How to preserve the `{SqlState}` log field in handlers without re-importing Npgsql

**Options considered:**
- (A) Drop `{SqlState}` from handler logs — rely on `PostgresExceptionLoggingInterceptor` to log it once at the persistence boundary.
- (B) Expose `SqlState` as a nullable `string` property on `GridLayoutPersistenceException`, populated at translation time.
- (C) Leave handlers walking the `InnerException` chain — but this still requires importing `Npgsql.PostgresException` to read `.SqlState`, defeating the refactor.

**Chosen approach:** (B) — add `public string? SqlState { get; }` to `GridLayoutPersistenceException`.

**Rationale:** (C) defeats the goal. (A) is tempting (the interceptor already logs SqlState) but it changes the log shape — observability dashboards or queries that grep handler log lines for `SqlState=...` would silently break, and NFR-1 says "Error logs preserve their level, template, and structured fields." (B) preserves the field exactly. `SqlState` is a plain string in the SQL standard (`23505`, `23503`, etc.), so storing it as a string introduces zero coupling to Npgsql in the Domain layer.

**Spec amendment required** — see Specification Amendments below.

#### Decision 3: How to test the translation (FR-8) without a live PostgreSQL

**Options considered:**
- (A) `Testcontainers.PostgreSQL` (already referenced in `Anela.Heblo.Tests.csproj`) — spin a real Postgres container and force a constraint violation.
- (B) Test double: a derived `TestApplicationDbContext` that overrides `SaveChangesAsync` to throw a `new NpgsqlException("...")`.
- (C) Extract a thin `IGridLayoutQueries` seam to inject failures.

**Chosen approach:** (B) — derived `ApplicationDbContext` test double, with `Microsoft.EntityFrameworkCore.InMemory` for the happy-path seed and an override that throws on demand.

**Rationale:** (A) is more realistic but adds container startup time to the unit test suite and is overkill for a translation test. (C) is invasive for one test. (B) matches spec FR-8's "test double or in-memory substitute" wording, runs fast, and `NpgsqlException` has a public `(string message)` constructor that the existing handler tests already use. The test asserts the **boundary contract** (input: NpgsqlException; output: GridLayoutPersistenceException with InnerException preserved) — that is exactly what we want to lock in.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/
  Features/GridLayouts/
    GridLayoutPersistenceException.cs              ← NEW (FR-1)

backend/src/Anela.Heblo.Persistence/
  Infrastructure/
    PostgresExceptionLoggingInterceptor.cs         (unchanged)
    PostgresExceptionTranslator.cs                 ← NEW (Decision 1)
  GridLayouts/
    GridLayoutRepository.cs                        ← MODIFIED (FR-2)

backend/src/Anela.Heblo.Application/
  Features/GridLayouts/UseCases/
    GetGridLayout/GetGridLayoutHandler.cs          ← MODIFIED (FR-3)
    SaveGridLayout/SaveGridLayoutHandler.cs        ← MODIFIED (FR-4)
    ResetGridLayout/ResetGridLayoutHandler.cs      ← MODIFIED (FR-5)

backend/test/Anela.Heblo.Tests/
  Features/GridLayouts/
    GetGridLayoutHandlerTests.cs                   ← MODIFIED (FR-7)
    SaveGridLayoutHandlerTests.cs                  ← MODIFIED (FR-7)
    ResetGridLayoutHandlerTests.cs                 ← MODIFIED (FR-7)
  Persistence/GridLayouts/                         ← NEW directory
    GridLayoutRepositoryTranslationTests.cs        ← NEW (FR-8)
```

### Interfaces and Contracts

**`GridLayoutPersistenceException`** (Domain — final shape):
```csharp
namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public string? SqlState { get; }

    public GridLayoutPersistenceException(string message, string? sqlState, Exception inner)
        : base(message, inner)
    {
        SqlState = sqlState;
    }
}
```
- `class`, not `record` — exceptions follow CLR identity semantics; matches BCL convention.
- Constructor takes `(message, sqlState, inner)`. `sqlState` is nullable because connection-level `NpgsqlException` without a `PostgresException` inside has no SqlState.
- No XML doc tags required, but `<exception>` tags on `IGridLayoutRepository` methods are recommended.

**`PostgresExceptionTranslator`** (Persistence/Infrastructure — sketch):
```csharp
internal static class PostgresExceptionTranslator
{
    // Returns null when ex is not a Npgsql/Postgres family exception → caller rethrows untouched.
    public static GridLayoutPersistenceException? TryTranslateGridLayout(Exception ex, string operationDescription) { ... }
}
```
- Reuses the same unwrap recursion as `PostgresExceptionLoggingInterceptor.UnwrapPostgresException`.
- Recognizes: direct `PostgresException`, direct `NpgsqlException`, `DbUpdateException` whose inner is `PostgresException` (or deeper).
- Returns `null` for anything else (so `OperationCanceledException`, `DbUpdateConcurrencyException` without Pg inner, etc. propagate unchanged — FR-2 acceptance criterion).

**`IGridLayoutRepository`** (Domain — unchanged signatures, new exception contract):
```csharp
public interface IGridLayoutRepository
{
    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
    // ... same for UpsertAsync, DeleteAsync
}
```

**MediatR responses** (`GetGridLayoutResponse`, `SaveGridLayoutResponse`, `ResetGridLayoutResponse`) — **unchanged**.

### Data Flow

**Happy path (read):**
```
Controller → MediatR → GetGridLayoutHandler.Handle
  → _repository.GetAsync (Domain interface)
  → GridLayoutRepository.GetAsync (Persistence)
  → EF Core FirstOrDefaultAsync
  → returns entity (or null) → DTO → GetGridLayoutResponse
```

**Error path (PostgreSQL failure, e.g. SqlState 42P01 "relation does not exist"):**
```
EF Core FirstOrDefaultAsync throws NpgsqlException (or DbUpdateException wrapping PostgresException)
  → caught in GridLayoutRepository wrapper
  → PostgresExceptionTranslator.TryTranslateGridLayout(ex, "GetGridLayout") returns GridLayoutPersistenceException(message, sqlState, ex)
  → rethrown
  → caught in GetGridLayoutHandler as GridLayoutPersistenceException
  → _logger.LogError(ex, "Database error reading GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}", userId, request.GridKey, ex.SqlState)
  → returns GetGridLayoutResponse { Layout = null }   ← identical to today
```

**Pass-through path (non-Pg failure):**
```
DbUpdateConcurrencyException (no Pg inner), OperationCanceledException, etc.
  → wrapper sees translator return null → rethrows original
  → handler does NOT catch → propagates to global exception middleware
  → identical to today
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Adding `SqlState` to `GridLayoutPersistenceException` is a spec deviation (FR-1 specifies only `(string, Exception)` constructor). | Medium | Captured in Specification Amendments below; the alternative drops a structured log field and violates NFR-1. |
| `DbUpdateException` wrapping of `PostgresException` is **not** caught by today's handlers but **will be** translated by the new repository wrapper — this is a behavior change (in the better direction, but a change). | Medium | Call this out in the PR description as an intentional bug fix discovered during the refactor. Add a translation test for the `DbUpdateException(inner: PostgresException)` shape so the regression coverage is explicit. |
| Translator misses an exception shape (e.g. deeply nested aggregate exceptions) and lets Npgsql leak past the boundary back to the handler — handler catches `GridLayoutPersistenceException` only and unhandled exception escapes. | Medium | Reuse the same recursive unwrap pattern as `PostgresExceptionLoggingInterceptor` (already battle-tested in this repo). Add a unit test for the `DbUpdateException → PostgresException` nested case. |
| FR-6 (remove Npgsql PackageReference from `Anela.Heblo.Application.csproj`) is **unsatisfiable as written**: there is no direct `<PackageReference Include="Npgsql" />` in that csproj — Npgsql arrives transitively through `Anela.Heblo.Persistence`'s reference to `Npgsql.EntityFrameworkCore.PostgreSQL`. | Low | Reinterpret FR-6 as "no `using Npgsql;` in any Application-layer source file under `Features/GridLayouts/`" (verified by grep). The transitive runtime dependency is unavoidable while Application references Persistence — out of scope. |
| Three other Application files (`Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`) still import Npgsql. Reviewers may expect them in this PR. | Low | Brief explicitly scopes to GridLayouts; mention these as follow-up tickets in the PR description so they don't get lost. |
| New `PostgresExceptionTranslator` overlaps in spirit with the existing `PostgresExceptionLoggingInterceptor`. Future maintainers might wonder why both exist. | Low | Place the translator next to the interceptor in `Persistence/Infrastructure/` and add a one-line comment on each explaining the distinct purpose (logging vs. domain translation). |

## Specification Amendments

1. **FR-1 — extend the exception shape.** Change the exception to expose `SqlState`:
   ```csharp
   public class GridLayoutPersistenceException : Exception
   {
       public string? SqlState { get; }
       public GridLayoutPersistenceException(string message, string? sqlState, Exception inner)
           : base(message, inner) { SqlState = sqlState; }
   }
   ```
   **Why:** handlers log `{SqlState}` as a structured field today (NFR-1 requires log shape preservation), and any handler-side extraction of `SqlState` from the inner exception chain would require re-importing `Npgsql.PostgresException`, defeating the refactor.

2. **FR-2 — extend acceptance criteria to cover EF wrapping.** Add: "If a thrown exception is `DbUpdateException` whose inner exception chain contains a `PostgresException`, it is also translated to `GridLayoutPersistenceException`." The current handler `catch (Exception ex) when (ex is PostgresException or NpgsqlException)` does **not** catch this case today, so the new repository wrapper actually broadens the caught surface. Frame this as a deliberate, documented improvement in the PR — not silent behavior change.

3. **FR-6 — reinterpret.** No direct `<PackageReference>` for Npgsql exists in `Anela.Heblo.Application.csproj`; the Npgsql assembly is transitive via `Anela.Heblo.Persistence` → `Npgsql.EntityFrameworkCore.PostgreSQL`, and that cannot be removed while Application references Persistence. Restate FR-6 as: *"`grep -r "using Npgsql" backend/src/Anela.Heblo.Application/Features/GridLayouts` returns no matches; no Npgsql type appears in any file under `Application/Features/GridLayouts/`."* No csproj change is required.

4. **FR-7 — clarify the log assertion update.** The existing tests assert `It.IsAny<NpgsqlException>()` in the logger verification. They must be changed to `It.IsAny<GridLayoutPersistenceException>()`. Make this explicit in FR-7 acceptance criteria so reviewers don't miss the verification update.

5. **FR-8 — specify the test double mechanism.** Use a derived `ApplicationDbContext` that overrides `SaveChangesAsync` (and exposes a way to throw on the read path) to throw `new NpgsqlException("…")`. Two test cases: (a) direct `NpgsqlException` translation, (b) `DbUpdateException` wrapping a `PostgresException` translation — both assert the resulting `GridLayoutPersistenceException.InnerException` equals the original. The `PostgresException` constructor is parameter-heavy; if constructing one is awkward, the test for case (b) can use `new DbUpdateException("test", new NpgsqlException("inner"))` since `NpgsqlException` is `PostgresException`'s base.

## Prerequisites

None. All required infrastructure exists:

- `Anela.Heblo.Domain.Features.GridLayouts` namespace exists.
- `Anela.Heblo.Persistence.Infrastructure` namespace exists (sibling to the new `PostgresExceptionTranslator`).
- `Anela.Heblo.Tests.csproj` already references `Moq`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory`, and (unused for this PR but available) `Testcontainers.PostgreSql`.
- No database migrations, configuration, feature flags, secrets, or infrastructure changes are required.
- No frontend changes; OpenAPI client generation produces no new types since DTOs and response shapes are unchanged.

Implementation can begin immediately.