# Specification: Decouple GridLayouts Application Handlers from Npgsql

## Summary
The GridLayouts MediatR handlers in the Application layer currently catch `NpgsqlException` and `PostgresException` directly, creating a compile-time dependency on the Npgsql PostgreSQL driver. This work introduces a `GridLayoutPersistenceException` domain exception, translates infrastructure exceptions inside the Persistence-layer repository, and updates handlers and their unit tests to depend only on the domain abstraction — restoring the Clean Architecture dependency direction.

## Background
Clean Architecture in this repo dictates that the Application layer depends only on the Domain layer (abstractions). The current implementation violates this in three places:

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — `using Npgsql;` line 7, catch block lines 45–51
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — `using Npgsql;` line 9, catch block lines 44–50
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` — `using Npgsql;` line 5, catch block lines 34–40

Consequences of the leak:
- The `Anela.Heblo.Application` assembly takes a transitive dependency on the Npgsql package.
- The `IGridLayoutRepository` abstraction is no longer truly substitutable — a non-PostgreSQL implementation (in-memory, SQLite, test double) cannot reproduce `NpgsqlException`, so the catch branches are dead under unit tests but live in production.
- Unit tests at `backend/test/Anela.Heblo.Tests/Features/GridLayouts/*` import Npgsql to construct exception instances for mocking, propagating the coupling into the test suite.

This is a refactor with no user-visible behavior change: the same error path must continue to return the same MediatR response (preserved status code, error code, and message). The fix was filed by the daily arch-review routine on 2026-05-29.

## Functional Requirements

### FR-1: Introduce `GridLayoutPersistenceException` domain exception
Add a new exception type in the Domain layer that the Application layer can catch without referencing infrastructure types.

**Location:** `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs`

**Shape:**
```csharp
namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner) { }
}
```

**Acceptance criteria:**
- Class lives in `Anela.Heblo.Domain.Features.GridLayouts` namespace.
- Has a single constructor taking `(string message, Exception inner)` and forwards both to `Exception`.
- No dependency on Npgsql, EF Core, or any infrastructure package.
- Project compiles after the type is added.

### FR-2: Translate Npgsql exceptions in the Persistence-layer repository
The `GridLayoutRepository` implementation must catch `NpgsqlException`/`PostgresException` at the persistence boundary and rethrow them as `GridLayoutPersistenceException`, preserving the inner exception for diagnostics.

**Acceptance criteria:**
- Every repository method that today can surface `NpgsqlException`/`PostgresException` to its caller wraps the call site and rethrows `GridLayoutPersistenceException(originalMessage, ex)`.
- The wrapper does not swallow other exception types (e.g. `OperationCanceledException`, `DbUpdateConcurrencyException`) — they continue to propagate unchanged unless they are themselves Npgsql exceptions.
- Repository methods that already wrap with EF Core (`DbUpdateException`) keep their existing behavior, but `DbUpdateException.InnerException as PostgresException` is also translated to `GridLayoutPersistenceException` when the inner is a PostgreSQL error. (See Open Questions if EF wrapping is encountered.)
- The Persistence layer keeps its Npgsql reference; no other layer gains one.

### FR-3: Remove Npgsql from `GetGridLayoutHandler`
Update `GetGridLayoutHandler.cs` to catch `GridLayoutPersistenceException` instead of `NpgsqlException`/`PostgresException`.

**Acceptance criteria:**
- `using Npgsql;` is removed from the file.
- The catch block (current lines 45–51) catches `GridLayoutPersistenceException`.
- The MediatR response returned from the catch block is byte-for-byte equivalent to today's response (same `ErrorCode`, same `Message`, same HTTP semantics downstream).
- Logging inside the catch preserves the current log level and message template, with the exception still attached for full stack trace.

### FR-4: Remove Npgsql from `SaveGridLayoutHandler`
Same as FR-3 applied to `SaveGridLayoutHandler.cs` (current catch block lines 44–50).

**Acceptance criteria:**
- `using Npgsql;` removed.
- `GridLayoutPersistenceException` is the only persistence-related exception caught.
- MediatR response unchanged.
- Log output unchanged in level, template, and exception attachment.

### FR-5: Remove Npgsql from `ResetGridLayoutHandler`
Same as FR-3 applied to `ResetGridLayoutHandler.cs` (current catch block lines 34–40).

**Acceptance criteria:**
- `using Npgsql;` removed.
- `GridLayoutPersistenceException` is the only persistence-related exception caught.
- MediatR response unchanged.
- Log output unchanged in level, template, and exception attachment.

### FR-6: Remove Npgsql package reference from `Anela.Heblo.Application.csproj`
Once handlers and Application-layer code no longer reference Npgsql types, remove the `<PackageReference>` for `Npgsql` (and any transitive Npgsql-only references) from the Application project file, unless another component in the project still needs it.

**Acceptance criteria:**
- `grep -r "using Npgsql" backend/src/Anela.Heblo.Application` returns no matches.
- `Anela.Heblo.Application.csproj` no longer declares a direct `Npgsql` package reference (if such a reference exists today purely for the GridLayouts handlers).
- Solution still builds with `dotnet build`.
- If other Application features rely on Npgsql, leave the reference in place and note this in Open Questions.

### FR-7: Update GridLayouts handler unit tests to use `GridLayoutPersistenceException`
Modify the three test files so they no longer import or construct `NpgsqlException`:

- `backend/test/.../GridLayouts/.../GetGridLayoutHandlerTests.cs` (currently imports Npgsql at line 8)
- `backend/test/.../GridLayouts/.../SaveGridLayoutHandlerTests.cs` (line 9)
- `backend/test/.../GridLayouts/.../ResetGridLayoutHandlerTests.cs` (line 5)

**Acceptance criteria:**
- No `using Npgsql;` directive in any GridLayouts test file.
- Mocks of `IGridLayoutRepository` that previously threw `NpgsqlException`/`PostgresException` now throw `GridLayoutPersistenceException` with a representative message and inner exception.
- The error-path tests still assert the same MediatR response (same error code, same message, same status).
- All previously passing GridLayouts handler tests still pass.

### FR-8: Add a repository-level test for Npgsql → domain translation
Add at least one test in the persistence test layer that verifies a thrown `NpgsqlException` (or `PostgresException`, whichever is realistically surfaced by EF Core / direct ADO.NET in this codebase) is translated into `GridLayoutPersistenceException` by the repository.

**Acceptance criteria:**
- One new test exists alongside other `GridLayoutRepository` tests (or is added if none exist) that asserts:
  - Given a repository method invocation whose underlying call throws `NpgsqlException`,
  - When the method is invoked,
  - Then a `GridLayoutPersistenceException` is thrown, and `InnerException` is the original `NpgsqlException`.
- The test does not require a live PostgreSQL connection — it uses a test double or in-memory substitute for the underlying data context.

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a refactor with **zero behavioral changes** observable through the HTTP API or MediatR pipeline. The same input produces the same response code, body, and side effects as before. Error logs preserve their level, template, and structured fields.

### NFR-2: Build & static checks
- `dotnet build` passes with no warnings introduced by this change.
- `dotnet format` passes.
- No new analyzer warnings.

### NFR-3: Test coverage
- All existing GridLayouts tests continue to pass.
- New repository translation test (FR-8) is added.
- Coverage on the three handlers does not drop.
- E2E suite is not in PR CI (per project facts) — not required to run for this PR, but should not be broken in nightly.

### NFR-4: Dependency direction
After the change:
- `Anela.Heblo.Application` has no direct or transitive code-level dependency on `Npgsql` (project file does not declare it, source files do not import it).
- `Anela.Heblo.Domain` has no dependency on Npgsql or EF Core.
- The Npgsql dependency is confined to `Anela.Heblo.Persistence` (and any infrastructure-only assemblies).

### NFR-5: Performance
No performance impact. Exception translation adds at most one wrapping allocation on the already-exceptional error path; the happy path is unchanged.

## Data Model
No database schema changes. No new tables, columns, or migrations.

New runtime type:
- `Anela.Heblo.Domain.Features.GridLayouts.GridLayoutPersistenceException` — domain exception, properties: `Message` (string), `InnerException` (Exception, the original Npgsql/Postgres exception).

## API / Interface Design
No public HTTP API contract changes. No new endpoints. No changes to request/response DTOs.

Internal interface contract (`IGridLayoutRepository`):
- Existing method signatures are unchanged.
- New documented exception contract: methods may throw `GridLayoutPersistenceException` to indicate a persistence failure. (Optionally add XML doc `<exception>` tags to the interface methods — recommended but not strictly required.)

MediatR error responses returned by handlers remain identical in shape and content.

## Dependencies
- **Npgsql** — remains in the Persistence project; removed from Application (FR-6).
- **MediatR** — handlers remain MediatR-based; no version change.
- **EF Core (Npgsql provider)** — unchanged in Persistence.
- **xUnit / Moq (or current mocking library)** — used in the test updates; no library swap.
- No external services involved.
- No feature flag needed; this is a pure code refactor.

## Out of Scope
- Refactoring other modules' handlers that may catch infrastructure exceptions (this brief is scoped to GridLayouts only).
- Introducing a generic cross-module persistence exception hierarchy (e.g. `DomainPersistenceException` base). If a future arch-review finds the same anti-pattern elsewhere, that is a separate ticket.
- Changing the MediatR response shape, error codes, or HTTP status codes returned by these handlers.
- Adding new retry / circuit-breaker logic around persistence failures.
- Database migrations or schema changes.
- Updating frontend code — no client-visible contract changes.

## Open Questions
None.

## Status: COMPLETE
