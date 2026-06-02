# Decouple GridLayouts Application Handlers from Npgsql — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the `using Npgsql;` directives from the three GridLayouts MediatR handlers (and their tests) by introducing a domain-layer `GridLayoutPersistenceException` and translating Npgsql/Postgres exceptions at the persistence boundary, restoring the Clean Architecture dependency direction (Application → Domain only).

**Architecture:** Add `GridLayoutPersistenceException` in `Anela.Heblo.Domain.Features.GridLayouts` with a nullable `SqlState` string property so handlers can preserve their existing `{SqlState}` log field without re-importing Npgsql. Introduce a static `PostgresExceptionTranslator` in `Anela.Heblo.Persistence.Infrastructure` that mirrors the unwrap recursion already used by `PostgresExceptionLoggingInterceptor`, then wrap each `GridLayoutRepository` method with it. Update the three handlers and their unit tests to catch the new domain exception. No public API or behavioral change.

**Tech Stack:** .NET 8, C#, EF Core 8 (Npgsql provider), MediatR 12, xUnit, Moq, FluentAssertions, `Microsoft.EntityFrameworkCore.InMemory` for repository-level tests.

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` — domain exception type (FR-1, amended by arch-review to include `SqlState`).
- `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — static helper that detects Npgsql/Postgres family exceptions anywhere in the chain and produces a `GridLayoutPersistenceException`. Returns `null` for non-Pg exceptions so callers rethrow untouched.
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` — pure unit tests for the translator (covers direct `NpgsqlException`, `DbUpdateException` wrapping, and non-Pg passthrough).
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` — repository-level integration test using a derived `ApplicationDbContext` that throws on demand (FR-8).

**Modify:**
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — wrap each of `GetAsync`, `UpsertAsync`, `DeleteAsync` with the translator (FR-2).
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — remove `using Npgsql;`, catch `GridLayoutPersistenceException`, log `ex.SqlState` (FR-3).
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — same as Get (FR-4).
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` — same as Get (FR-5).
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — replace `NpgsqlException` mock throws and log verifications with `GridLayoutPersistenceException` (FR-7).
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` — same (FR-7).
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs` — same (FR-7).

**Do NOT modify (per arch-review):**
- `Anela.Heblo.Application.csproj` — there is no direct `<PackageReference Include="Npgsql" />` to remove. Npgsql is transitive via `Anela.Heblo.Persistence` → `Npgsql.EntityFrameworkCore.PostgreSQL`. FR-6 is satisfied by verifying `grep -r "using Npgsql" backend/src/Anela.Heblo.Application/Features/GridLayouts` returns no matches.
- `PostgresExceptionLoggingInterceptor.cs` — keep as-is; the new translator lives next to it with a distinct purpose.
- The three Application files outside GridLayouts that also `using Npgsql;` (`Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`) — explicitly out of scope per the spec.

---

## Task 1: Create `GridLayoutPersistenceException` in the Domain layer

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs`

- [ ] **Step 1: Create the exception class file**

Write the file exactly as below (final shape from arch-review Decision 2 / Spec Amendment 1):

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

Notes:
- `class`, not `record` — matches BCL exception convention.
- `SqlState` is nullable because connection-level `NpgsqlException` without an inner `PostgresException` has no SqlState.
- The constructor takes `(message, sqlState, inner)` — three parameters. The original spec FR-1 said two; arch-review Spec Amendment 1 elevates this to three so handlers can preserve the `{SqlState}` log field without re-importing Npgsql.
- No `using` directives needed — `Exception` is in the implicitly-imported `System` namespace.

- [ ] **Step 2: Verify the Domain project compiles**

Run from repo root:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded.` with 0 errors, 0 warnings.

If a warning appears about the exception not being serializable (CA-style analyzer), ignore it — the codebase does not enable those analyzers; verify by checking `backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj` does not enable `EnableNETAnalyzers` for that rule. Do **not** add `[Serializable]` or serialization constructors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs
git commit -m "feat: add GridLayoutPersistenceException domain exception"
```

---

## Task 2: Create `PostgresExceptionTranslator` in the Persistence/Infrastructure layer

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs`

This is a small static helper that detects whether an exception thrown by EF Core is a Npgsql-family exception (directly, or wrapped in `DbUpdateException` etc.) and produces a `GridLayoutPersistenceException`. Mirrors the unwrap recursion used in `PostgresExceptionLoggingInterceptor.UnwrapPostgresException` (lines 61–70 of that file).

- [ ] **Step 1: Write the translator class file**

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// Translates Npgsql/PostgreSQL exceptions surfaced by EF Core into the domain-layer
/// <see cref="GridLayoutPersistenceException"/>, keeping driver types out of the Application layer.
/// Distinct from <see cref="PostgresExceptionLoggingInterceptor"/>: the interceptor enriches save-failure
/// logs at the boundary; this helper produces a domain exception so handlers can catch a domain type.
/// </summary>
internal static class PostgresExceptionTranslator
{
    /// <summary>
    /// Returns a <see cref="GridLayoutPersistenceException"/> wrapping <paramref name="exception"/> when
    /// the chain contains a <see cref="NpgsqlException"/> (which includes <see cref="PostgresException"/>),
    /// or <c>null</c> for anything else so the caller can rethrow unchanged.
    /// </summary>
    public static GridLayoutPersistenceException? TryTranslateGridLayout(Exception exception, string operation)
    {
        var npgsqlEx = FindNpgsqlException(exception);
        if (npgsqlEx is null)
        {
            return null;
        }

        var sqlState = (npgsqlEx as PostgresException)?.SqlState;
        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            sqlState,
            exception);
    }

    private static NpgsqlException? FindNpgsqlException(Exception? exception)
    {
        return exception switch
        {
            null => null,
            NpgsqlException npg => npg,
            _ => FindNpgsqlException(exception.InnerException)
        };
    }
}
```

Notes:
- `internal` access — only the Persistence assembly uses it. (`Anela.Heblo.Persistence` already exposes `InternalsVisibleTo("Anela.Heblo.Tests")` indirectly via tests project references; if a test needs to call it, the test references `Anela.Heblo.Persistence` directly. If the internal access blocks tests, change to `public` in Step 3 below — but try `internal` first.)
- Since `PostgresException : NpgsqlException`, the single `FindNpgsqlException` recursion finds both: a direct `NpgsqlException`, a direct `PostgresException`, and a `DbUpdateException { InnerException: NpgsqlException }`.
- The wrapper preserves the original exception in `InnerException`, so `PostgresExceptionLoggingInterceptor` (already attached) still has its enriched log line, and full stack trace remains available.

- [ ] **Step 2: Verify the Persistence project compiles**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.` with 0 errors, 0 warnings.

- [ ] **Step 3: If the test project cannot see `PostgresExceptionTranslator`, expose `InternalsVisibleTo`**

Run a quick check:

```bash
grep -r "InternalsVisibleTo" backend/src/Anela.Heblo.Persistence
```

If `Anela.Heblo.Tests` is not listed, add the following to `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj` inside an `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Anela.Heblo.Tests" />
</ItemGroup>
```

If you prefer not to modify the csproj, change `internal static class PostgresExceptionTranslator` to `public static class PostgresExceptionTranslator` in `PostgresExceptionTranslator.cs`. Either choice is acceptable — pick `InternalsVisibleTo` if the file already exists with that pattern elsewhere in the project, otherwise `public`. Do not do both.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs
# If you modified the csproj in Step 3, also:
git add backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
git commit -m "feat: add PostgresExceptionTranslator for domain exception mapping"
```

---

## Task 3: Add translator unit tests (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs`

- [ ] **Step 1: Create the test directory and write the failing tests**

```bash
mkdir -p backend/test/Anela.Heblo.Tests/Persistence/GridLayouts
```

Write `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class PostgresExceptionTranslatorTests
{
    [Fact]
    public void TryTranslateGridLayout_GivenDirectNpgsqlException_ReturnsTranslatedExceptionWithOriginalAsInner()
    {
        // Arrange
        var inner = new NpgsqlException("connection refused");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(inner, "Get");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(inner);
        result.Message.Should().Contain("Get");
        result.SqlState.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateExceptionWrappingNpgsqlException_ReturnsTranslatedException()
    {
        // Arrange
        var npgsqlInner = new NpgsqlException("duplicate key value violates unique constraint");
        var outer = new DbUpdateException("An error occurred while saving the entity changes.", npgsqlInner);

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(outer, "Upsert");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(outer);
        result.Message.Should().Contain("Upsert");
    }

    [Fact]
    public void TryTranslateGridLayout_GivenOperationCanceledException_ReturnsNull()
    {
        // Arrange
        var ex = new OperationCanceledException("cancelled by caller");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateConcurrencyExceptionWithoutNpgsqlInner_ReturnsNull()
    {
        // Arrange
        var ex = new DbUpdateConcurrencyException("Concurrency token mismatch.");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Upsert");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenPlainInvalidOperationException_ReturnsNull()
    {
        // Arrange
        var ex = new InvalidOperationException("something else");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }
}
```

Notes:
- We assert `InnerException.Should().BeSameAs(outer)` — i.e., the **outer** exception thrown by EF Core is preserved, not the unwrapped inner `NpgsqlException`. This preserves the full chain for diagnostics; the translator extracts `SqlState` from the unwrapped Pg exception but keeps the original throw as inner.
- We do **not** construct a real `PostgresException` (constructor surface is awkward and Npgsql-version-sensitive). `SqlState` extraction is validated via the handler-level tests' log assertions in Tasks 8–10 and through visual inspection of the one-line property pass-through.

- [ ] **Step 2: Run the tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PostgresExceptionTranslatorTests" --no-restore
```

Expected: 5 passing tests.

If you get a compile error about `PostgresExceptionTranslator` being inaccessible, recheck Task 2 Step 3 (either `InternalsVisibleTo` or change visibility to `public`).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs
git commit -m "test: add unit tests for PostgresExceptionTranslator"
```

---

## Task 4: Wire the translator into `GridLayoutRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`

- [ ] **Step 1: Rewrite the file to wrap each operation**

Replace the entire file contents with:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GridLayoutRepository(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);
        }
        catch (Exception ex)
        {
            var translated = PostgresExceptionTranslator.TryTranslateGridLayout(ex, nameof(GetAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }

    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);

            if (existing is not null)
            {
                existing.LayoutJson = layoutJson;
                existing.LastModified = _timeProvider.GetUtcNow().DateTime;
            }
            else
            {
                _context.GridLayouts.Add(new GridLayout
                {
                    UserId = userId,
                    GridKey = gridKey,
                    LayoutJson = layoutJson,
                    LastModified = _timeProvider.GetUtcNow().DateTime
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var translated = PostgresExceptionTranslator.TryTranslateGridLayout(ex, nameof(UpsertAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }

    public async Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);

            if (existing is not null)
            {
                _context.GridLayouts.Remove(existing);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var translated = PostgresExceptionTranslator.TryTranslateGridLayout(ex, nameof(DeleteAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }
}
```

Notes:
- I inlined the previous `await GetAsync(...)` call in `UpsertAsync` and `DeleteAsync` to avoid double-wrapping (otherwise a `GridLayoutPersistenceException` thrown inside `GetAsync` would be caught again by the outer wrapper — also a `GridLayoutPersistenceException`, so technically harmless, but the duplicate `try/catch` is noise).
- The repository continues to surface `OperationCanceledException`, `DbUpdateConcurrencyException` without a Pg inner, and any other exception type unchanged (translator returns `null`, we rethrow).
- The catch is `catch (Exception ex)` — broad on purpose because the translator is the gate. The translator returning `null` is the explicit pass-through.

- [ ] **Step 2: Run the existing repository-adjacent tests to confirm nothing else broke**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayout" --no-restore
```

Expected: existing GridLayout handler tests still pass against the old assumptions (they mock `IGridLayoutRepository` directly, so they don't exercise this file). New translator tests still pass. No GridLayoutRepository tests yet (Task 5 adds them).

If any handler test fails because it asserts `It.IsAny<NpgsqlException>()` in the logger verification, leave it failing for now — Tasks 8–10 will fix those. Note which test names fail.

- [ ] **Step 3: Build the full backend to confirm no other consumer broke**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. Warnings related to Npgsql usage in the three handler files are still present (will be removed in Tasks 6–8).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs
git commit -m "feat: translate Npgsql exceptions in GridLayoutRepository to domain exception"
```

---

## Task 5: Add repository-level translation tests (FR-8)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`

Uses a derived `ApplicationDbContext` that throws on the read or save path. Built on `Microsoft.EntityFrameworkCore.InMemory` for a working baseline, then selectively short-circuits to throw the desired exception.

- [ ] **Step 1: Write the failing tests**

Write `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class GridLayoutRepositoryTranslationTests
{
    private sealed class ThrowingApplicationDbContext : ApplicationDbContext
    {
        public Exception? ThrowOnSaveChanges { get; set; }

        public ThrowingApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSaveChanges is not null)
            {
                throw ThrowOnSaveChanges;
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private static ThrowingApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"GridLayoutTranslationTests_{Guid.NewGuid()}")
            .Options;
        return new ThrowingApplicationDbContext(options);
    }

    [Fact]
    public async Task UpsertAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException()
    {
        // Arrange
        using var context = CreateContext();
        var npgsqlEx = new NpgsqlException("connection terminated");
        context.ThrowOnSaveChanges = npgsqlEx;

        var repository = new GridLayoutRepository(context, TimeProvider.System);

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-1", "grid-1", "{}", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().BeSameAs(npgsqlEx);
    }

    [Fact]
    public async Task UpsertAsync_WhenSaveChangesThrowsDbUpdateExceptionWrappingNpgsql_ThrowsGridLayoutPersistenceException()
    {
        // Arrange
        using var context = CreateContext();
        var npgsqlInner = new NpgsqlException("duplicate key");
        var dbUpdateEx = new DbUpdateException("An error occurred while saving.", npgsqlInner);
        context.ThrowOnSaveChanges = dbUpdateEx;

        var repository = new GridLayoutRepository(context, TimeProvider.System);

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-1", "grid-1", "{}", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().BeSameAs(dbUpdateEx);
    }

    [Fact]
    public async Task UpsertAsync_WhenSaveChangesThrowsNonPgException_RethrowsOriginal()
    {
        // Arrange
        using var context = CreateContext();
        var unrelatedEx = new InvalidOperationException("unrelated failure");
        context.ThrowOnSaveChanges = unrelatedEx;

        var repository = new GridLayoutRepository(context, TimeProvider.System);

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-1", "grid-1", "{}", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(unrelatedEx);
    }

    [Fact]
    public async Task DeleteAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException()
    {
        // Arrange
        using var context = CreateContext();
        // Seed an entity so DeleteAsync reaches SaveChanges (otherwise it short-circuits).
        context.GridLayouts.Add(new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-1",
            LayoutJson = "{}",
            LastModified = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var npgsqlEx = new NpgsqlException("connection terminated");
        context.ThrowOnSaveChanges = npgsqlEx;

        var repository = new GridLayoutRepository(context, TimeProvider.System);

        // Act
        Func<Task> act = () => repository.DeleteAsync("user-1", "grid-1", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().BeSameAs(npgsqlEx);
    }
}
```

Notes:
- The `ThrowingApplicationDbContext` only overrides `SaveChangesAsync` because: (a) the read path through `DbSet<T>.FirstOrDefaultAsync` against the in-memory provider returns null without throwing; (b) overriding `Set<TEntity>()` to throw is brittle across EF Core 8 internals. Save-path coverage is the meaningful boundary — the spec FR-8 acceptance lists "a repository method invocation whose underlying call throws `NpgsqlException`" and `SaveChangesAsync` is the canonical such path.
- We seed an entity before `DeleteAsync` so the code path reaches `SaveChangesAsync` rather than short-circuiting on the "no existing entity" branch.
- Test 3 (`UpsertAsync_WhenSaveChangesThrowsNonPgException_RethrowsOriginal`) directly verifies FR-2 acceptance criterion: "The wrapper does not swallow other exception types — they continue to propagate unchanged".

- [ ] **Step 2: Run the tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryTranslationTests" --no-restore
```

Expected: 4 passing tests.

If a test fails because `ApplicationDbContext.SaveChangesAsync` is not virtual, check whether the base class exposes the overload taking `CancellationToken` as virtual — in EF Core 8 it is. If it's somehow non-virtual, override the parameterless `SaveChanges()` and `int SaveChanges(bool acceptAllChangesOnSuccess)` too. The repository only awaits `SaveChangesAsync(CancellationToken)`, so that single override is sufficient.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs
git commit -m "test: add repository-level translation tests for GridLayoutRepository"
```

---

## Task 6: Refactor `GetGridLayoutHandler` to catch the domain exception

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutHandler : IRequestHandler<GetGridLayoutRequest, GetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetGridLayoutHandler> _logger;

    public GetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<GetGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

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

            var dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto();
            dto.GridKey = entity.GridKey;
            dto.LastModified = entity.LastModified;

            return new GetGridLayoutResponse { Layout = dto };
        }
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error reading GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new GetGridLayoutResponse { Layout = null };
        }
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 7).
- Changed `catch (Exception ex) when (ex is PostgresException or NpgsqlException)` to `catch (GridLayoutPersistenceException ex)`.
- Removed the local `var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;`.
- `ex.SqlState` now comes from the domain exception (`string?`). Log template, level, and structured fields are byte-for-byte identical.
- Return value (`new GetGridLayoutResponse { Layout = null }`) is unchanged.

- [ ] **Step 2: Build to confirm the handler compiles without Npgsql**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: 0 errors. Warnings about unused Npgsql import disappear (this file used to have one).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs
git commit -m "refactor: catch GridLayoutPersistenceException in GetGridLayoutHandler"
```

---

## Task 7: Refactor `SaveGridLayoutHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutHandler : IRequestHandler<SaveGridLayoutRequest, SaveGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SaveGridLayoutHandler> _logger;

    public SaveGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<SaveGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SaveGridLayoutResponse> Handle(SaveGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        var payload = new GridLayoutDto
        {
            GridKey = request.GridKey,
            Columns = request.Columns
        };

        var json = JsonSerializer.Serialize(payload);

        try
        {
            await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);
            return new SaveGridLayoutResponse();
        }
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error saving GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);
        }
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 9).
- Same pattern as `GetGridLayoutHandler`: catch the domain exception, drop the `as PostgresException` cast.
- `SaveGridLayoutResponse(ErrorCodes.DatabaseError)` return path unchanged.

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs
git commit -m "refactor: catch GridLayoutPersistenceException in SaveGridLayoutHandler"
```

---

## Task 8: Refactor `ResetGridLayoutHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutHandler : IRequestHandler<ResetGridLayoutRequest, ResetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ResetGridLayoutHandler> _logger;

    public ResetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<ResetGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ResetGridLayoutResponse> Handle(ResetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        try
        {
            await _repository.DeleteAsync(userId, request.GridKey, cancellationToken);
            return new ResetGridLayoutResponse();
        }
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error resetting GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new ResetGridLayoutResponse(ErrorCodes.DatabaseError);
        }
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 5).
- Catches `GridLayoutPersistenceException` instead of the Npgsql union.

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs
git commit -m "refactor: catch GridLayoutPersistenceException in ResetGridLayoutHandler"
```

---

## Task 9: Update `GetGridLayoutHandlerTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class GetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<GetGridLayoutHandler>> _loggerMock = new();

    private GetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_WhenNoSavedLayout_ReturnsNull()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync((GridLayout?)null);

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
    }

    [Fact]
    public async Task Handle_WhenSavedLayoutExists_ReturnsDeserializedDto()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        var payload = new { columns = new[] { new { id = "col1", order = 0, width = 120, hidden = false } } };
        var json = JsonSerializer.Serialize(payload);

        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = json,
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.NotNull(response.Layout);
        Assert.Single(response.Layout!.Columns);
        Assert.Equal("col1", response.Layout.Columns[0].Id);
        Assert.Equal(120, response.Layout.Columns[0].Width);
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.GetAsync("user-1", "test-grid", default))
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during GetAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error reading GridLayout")),
                It.IsAny<GridLayoutPersistenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 8).
- The error-path test now throws `GridLayoutPersistenceException(...)` from the repository mock.
- The inner exception in the test can be any non-Npgsql exception (we use `InvalidOperationException`) — what matters is that the test asserts the handler catches a `GridLayoutPersistenceException` and produces the same response. Using `NpgsqlException` here would defeat the refactor's intent (test would still reference Npgsql).
- The logger `Verify` now asserts `It.IsAny<GridLayoutPersistenceException>()` — per arch-review Spec Amendment 4.
- The MediatR response assertion (`Assert.Null(response.Layout)`) is unchanged.

- [ ] **Step 2: Run the tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGridLayoutHandlerTests" --no-restore
```

Expected: 3 passing tests.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
git commit -m "test: switch GetGridLayoutHandlerTests to GridLayoutPersistenceException"
```

---

## Task 10: Update `SaveGridLayoutHandlerTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class SaveGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<SaveGridLayoutHandler>> _loggerMock = new();

    private SaveGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_CallsUpsertWithSerializedColumns()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        string? capturedJson = null;
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json)
            .Returns(Task.CompletedTask);

        var request = new SaveGridLayoutRequest
        {
            GridKey = "test-grid",
            Columns = new List<GridColumnStateDto>
            {
                new() { Id = "col1", Order = 0, Width = 150, Hidden = false },
                new() { Id = "col2", Order = 1, Width = null, Hidden = true }
            }
        };

        var handler = CreateHandler();
        await handler.Handle(request, default);

        _repositoryMock.Verify(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default), Times.Once);
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.True(parsed!.ContainsKey("columns"));
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsDatabaseErrorAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during UpsertAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));

        var request = new SaveGridLayoutRequest { GridKey = "test-grid", Columns = new List<GridColumnStateDto>() };

        var handler = CreateHandler();
        var response = await handler.Handle(request, default);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseError, response.ErrorCode);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error saving GridLayout")),
                It.IsAny<GridLayoutPersistenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 9).
- The error-path test throws `GridLayoutPersistenceException` instead of `NpgsqlException`.
- Logger `Verify` asserts `It.IsAny<GridLayoutPersistenceException>()`.

- [ ] **Step 2: Run the tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveGridLayoutHandlerTests" --no-restore
```

Expected: 2 passing tests.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs
git commit -m "test: switch SaveGridLayoutHandlerTests to GridLayoutPersistenceException"
```

---

## Task 11: Update `ResetGridLayoutHandlerTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire file contents with:

```csharp
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class ResetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<ResetGridLayoutHandler>> _loggerMock = new();

    private ResetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_CallsDeleteWithCorrectUserAndGrid()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.DeleteAsync("user-1", "test-grid", default)).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        _repositoryMock.Verify(x => x.DeleteAsync("user-1", "test-grid", default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsDatabaseErrorAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.DeleteAsync("user-1", "test-grid", default))
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during DeleteAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));

        var handler = CreateHandler();
        var response = await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseError, response.ErrorCode);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error resetting GridLayout")),
                It.IsAny<GridLayoutPersistenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

Diff vs. before:
- Removed `using Npgsql;` (line 7).
- Error-path test throws and asserts `GridLayoutPersistenceException`.

- [ ] **Step 2: Run the tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResetGridLayoutHandlerTests" --no-restore
```

Expected: 2 passing tests.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs
git commit -m "test: switch ResetGridLayoutHandlerTests to GridLayoutPersistenceException"
```

---

## Task 12: Optional — annotate `IGridLayoutRepository` with `<exception>` XML docs

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs`

Per spec API/Interface Design: "Optionally add XML doc `<exception>` tags to the interface methods — recommended but not strictly required." We add them — costs nothing, helps consumers.

- [ ] **Step 1: Rewrite the interface file**

```csharp
namespace Anela.Heblo.Domain.Features.GridLayouts;

public interface IGridLayoutRepository
{
    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default);

    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);

    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to confirm**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs
git commit -m "docs: document GridLayoutPersistenceException on IGridLayoutRepository"
```

---

## Task 13: Final verification — grep, build, format, full test run

This task gates the change against the spec's NFRs and FR-6 reinterpretation.

- [ ] **Step 1: Confirm no `using Npgsql;` remains under `Application/Features/GridLayouts/`**

```bash
grep -r "using Npgsql" backend/src/Anela.Heblo.Application/Features/GridLayouts
```

Expected: no output (exit code 1 from grep).

If the grep tool prints any line, return to Task 6/7/8 and remove the lingering directive. Do **not** modify the three out-of-scope Application files (`Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`) — they remain as follow-up work per the spec.

- [ ] **Step 2: Confirm no `using Npgsql;` remains under `test/.../Features/GridLayouts/`**

```bash
grep -r "using Npgsql" backend/test/Anela.Heblo.Tests/Features/GridLayouts
```

Expected: no output.

- [ ] **Step 3: Confirm Npgsql is still allowed in the test files that legitimately need it**

```bash
grep -rn "using Npgsql" backend/test/Anela.Heblo.Tests/Persistence/GridLayouts
```

Expected output:
- `PostgresExceptionTranslatorTests.cs` — uses `Npgsql` to construct test `NpgsqlException` instances (correct, this is a persistence-layer test).
- `GridLayoutRepositoryTranslationTests.cs` — same (correct).

- [ ] **Step 4: Build the entire backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. Zero **new** warnings introduced by this change (the codebase may have pre-existing warnings; verify by comparing warning count with the count from the base branch if uncertain — `git stash && dotnet build && git stash pop && dotnet build` and compare).

- [ ] **Step 5: Format**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0 (no formatting changes needed). If the command reports unformatted files, run `dotnet format backend/Anela.Heblo.sln` and commit the formatting fixes with `chore: dotnet format`.

- [ ] **Step 6: Run the full GridLayouts test suite (handlers + persistence)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayout" --no-restore
```

Expected: all GridLayouts tests pass. Total count = 3 (Get) + 2 (Save) + 2 (Reset) + 5 (translator unit) + 4 (repository integration) = **16 passing tests**.

If any pre-existing handler test fails, re-read its assertion against the rewritten file in Tasks 9–11 — most failures will be `It.IsAny<NpgsqlException>()` not being updated to `It.IsAny<GridLayoutPersistenceException>()`. Other failures should be investigated, not silenced.

- [ ] **Step 7: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-restore
```

Expected: all tests pass (no regressions in other modules). This guards against the small chance that another assembly indirectly depended on the old `NpgsqlException` propagation from `IGridLayoutRepository`.

- [ ] **Step 8: Final commit (only if Steps 1–7 introduced any formatting or trivial fixes; otherwise skip)**

```bash
# Only run if a previous step required follow-up edits.
git status
git add -A
git commit -m "chore: final cleanup after GridLayouts Npgsql decoupling"
```

If git status shows no pending changes, do not create an empty commit.

---

## Notes for the PR description (for the engineer wrapping up)

When opening the PR, include these notes (they correspond to arch-review Risks and Spec Amendments):

1. **Latent bug fix:** Today's `catch (Exception ex) when (ex is PostgresException or NpgsqlException)` in the three handlers does **not** catch `DbUpdateException` that wraps a `PostgresException` (constraint violations from `SaveChangesAsync` would have bubbled past the handler). The new repository wrapper translates that case too. This is an intentional, deliberate improvement — frame it as a fix discovered during the refactor.

2. **FR-6 reinterpretation:** No direct `<PackageReference Include="Npgsql" />` exists in `Anela.Heblo.Application.csproj`; Npgsql is transitive via `Anela.Heblo.Persistence` → `Npgsql.EntityFrameworkCore.PostgreSQL`. FR-6 is satisfied by the `grep -r "using Npgsql"` check on `Application/Features/GridLayouts/` returning no matches. No csproj change.

3. **Spec FR-1 amendment:** `GridLayoutPersistenceException` exposes a nullable `SqlState` property to preserve the `{SqlState}` structured log field (NFR-1 log shape preservation). The constructor is `(string message, string? sqlState, Exception inner)`.

4. **Out-of-scope follow-ups:** Three other Application files still import Npgsql — `Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`. The spec scopes this PR to GridLayouts; these are next candidates for the same pattern.

---

## Self-Review Notes (from the plan author)

Spec coverage check:
- FR-1 → Task 1 (with amended shape).
- FR-2 → Tasks 2 + 4 (translator + repository wiring), Task 5 (FR-2 acceptance "wrapper does not swallow other exception types" tested explicitly).
- FR-3 → Task 6.
- FR-4 → Task 7.
- FR-5 → Task 8.
- FR-6 → Task 13 Step 1 (grep verification, no csproj change per arch-review).
- FR-7 → Tasks 9 + 10 + 11 (with logger `Verify` updated per Spec Amendment 4).
- FR-8 → Task 3 (translator unit) + Task 5 (repository integration via `ThrowingApplicationDbContext`).
- NFR-1 (behavior preservation) → handler rewrites in Tasks 6–8 keep response shape, error codes, and log templates byte-for-byte identical.
- NFR-2 (build/static checks) → Task 13 Steps 4 + 5.
- NFR-3 (test coverage) → Tasks 3, 5, 9, 10, 11; total 16 GridLayouts tests after the change.
- NFR-4 (dependency direction) → Task 13 Step 1 grep verification.
- NFR-5 (performance) → trivially satisfied; happy path unchanged.

Type consistency check:
- `GridLayoutPersistenceException` constructor signature `(string message, string? sqlState, Exception inner)` is used identically in Tasks 1 (declaration), 2 (instantiation in translator), 9, 10, 11 (instantiation in test mocks).
- `PostgresExceptionTranslator.TryTranslateGridLayout(Exception, string)` signature is used identically in Tasks 2 (declaration), 3 (translator tests), 4 (repository call sites).
- `ThrowingApplicationDbContext.ThrowOnSaveChanges` property name used consistently in Task 5.
