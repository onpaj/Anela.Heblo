# Remove PostgreSQL-Specific `SqlState` from Domain Exception — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drop the `SqlState` property from `GridLayoutPersistenceException` (Domain layer) and move the structured `SqlState` log emission to `PostgresExceptionTranslator` (Persistence layer), where the PostgreSQL vocabulary belongs.

**Architecture:** Backend-only Clean Architecture refactor. (1) Convert `PostgresExceptionTranslator` from a `static` helper to an instance class with `ILogger<PostgresExceptionTranslator>` injected via DI; it logs `SqlState`+`Operation` at **Warning** before returning the translated exception. (2) Simplify `GridLayoutPersistenceException` to a 2-arg ctor `(string message, Exception inner)`, drop the `SqlState` property. (3) Update the repository to receive the translator via DI; update three handlers and four test files. No HTTP/UI contract changes.

**Tech Stack:** .NET 8 / C# 12, xUnit, FluentAssertions, Moq, Microsoft.Extensions.Logging, EF Core, Npgsql.

**Critical constraints carried into every task:**
- The Domain layer (`Anela.Heblo.Domain`) MUST NOT reference Npgsql or any persistence-provider type. NFR-1.
- The translator MUST keep its `TryTranslateGridLayout(Exception exception, string operation) → GridLayoutPersistenceException?` shape (nullable return). The repository pattern depends on it — see Architecture Review §Decision 2.
- The repository's catch blocks remain unchanged in shape: `catch (Exception ex) { var translated = _translator.TryTranslateGridLayout(ex, …); if (translated is not null) throw translated; throw; }`. No interface introduced for the translator (YAGNI — Architecture Review §Decision 4).
- `PostgresExceptionLoggingInterceptor` (the existing SaveChanges interceptor) is **NOT modified**. The duplicate `SqlState` entry on write paths is intentional (Architecture Review §Decision 3).

---

## File Inventory

| Path | Action | Notes |
|---|---|---|
| `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` | Modify | `static class` → `class`. Add `ILogger<PostgresExceptionTranslator>` ctor. Emit `LogWarning` before returning translated exception. |
| `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` | Modify | Add translator as ctor dependency; call instance method instead of static. |
| `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` | Modify | Register `services.AddScoped<PostgresExceptionTranslator>();`. |
| `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` | Modify | Drop `SqlState` property; collapse to 2-arg ctor. |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` | Modify | Drop ` SqlState={SqlState}` from log template and `ex.SqlState` argument. |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` | Modify | Drop ` SqlState={SqlState}` from log template and `ex.SqlState` argument. |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` | Modify | Drop ` SqlState={SqlState}` from log template and `ex.SqlState` argument. |
| `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` | Modify | Construct translator with `Mock<ILogger<PostgresExceptionTranslator>>` (or `NullLogger`); delete the `result.SqlState.Should().BeNull()` line; add a new `[Fact]` asserting the Warning log emission with `SqlState` + `Operation`. |
| `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` | Modify | Pass a translator instance with `NullLogger<PostgresExceptionTranslator>.Instance` into the `GridLayoutRepository` constructor in all four `[Fact]` setups. |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` | Modify | Update the `new GridLayoutPersistenceException(...)` test fixture at line 64 to the 2-arg form. |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` | Modify | Update the `new GridLayoutPersistenceException(...)` test fixture at line 88 to the 2-arg form. |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs` | Modify | Update the `new GridLayoutPersistenceException(...)` test fixture at line 38 to the 2-arg form. |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` | Modify | Update the `new GridLayoutPersistenceException(...)` test fixture at line 366 to the 2-arg form. |

**Files explicitly NOT touched:**
- `IGridLayoutRepository.cs` (only doc comments mention `GridLayoutPersistenceException`; no behavior change needed).
- `PostgresExceptionLoggingInterceptor.cs` (write-path interceptor — separate concern; per Architecture Review §Decision 3 the duplicate is intentional).
- `PhotobankRepository.cs`, `DataQualitySchemaHealthCheck.cs`, `ConsumptionCalculationService.cs` (use `PostgresException.SqlState` directly in Persistence/API/Application layers — out of scope per spec).

---

## Task 1: Refactor `PostgresExceptionTranslator` to an instance class with logger injection

**Goal:** Convert the static translator to an instance class that logs `SqlState`+`Operation` at Warning before returning the translated exception. Keep the `TryTranslateGridLayout(Exception, string)` shape. Register in DI. Update the repository and existing tests so the build stays green. **At the end of this task the `GridLayoutPersistenceException` ctor still takes 3 args — Domain change is deferred to Task 2.**

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`

- [ ] **Step 1: Add the failing log-emission test in `PostgresExceptionTranslatorTests.cs`**

This test will fail to compile until Step 3 (because `PostgresExceptionTranslator` is static and has no constructor). That's expected — it drives the refactor.

Replace the entire file contents with:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class PostgresExceptionTranslatorTests
{
    private static PostgresExceptionTranslator CreateTranslator(ILogger<PostgresExceptionTranslator>? logger = null) =>
        new(logger ?? NullLogger<PostgresExceptionTranslator>.Instance);

    [Fact]
    public void TryTranslateGridLayout_GivenDirectNpgsqlException_ReturnsTranslatedExceptionWithOriginalAsInner()
    {
        // Arrange
        var inner = new NpgsqlException("connection refused");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(inner, "Get");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(inner);
        result.Message.Should().Contain("Get");
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateExceptionWrappingNpgsqlException_ReturnsTranslatedException()
    {
        // Arrange
        var npgsqlInner = new NpgsqlException("duplicate key value violates unique constraint");
        var outer = new DbUpdateException("An error occurred while saving the entity changes.", npgsqlInner);
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(outer, "Upsert");

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
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateConcurrencyExceptionWithoutNpgsqlInner_ReturnsNull()
    {
        // Arrange
        var ex = new DbUpdateConcurrencyException("Concurrency token mismatch.");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Upsert");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenPlainInvalidOperationException_ReturnsNull()
    {
        // Arrange
        var ex = new InvalidOperationException("something else");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenNpgsqlException_LogsWarningWithSqlStateAndOperation()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<PostgresExceptionTranslator>>();
        var translator = CreateTranslator(loggerMock.Object);
        var npgsqlEx = new NpgsqlException("relation \"GridLayouts\" does not exist");

        // Act
        var result = translator.TryTranslateGridLayout(npgsqlEx, "GetAsync");

        // Assert
        result.Should().NotBeNull();
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("SqlState=") &&
                    state.ToString()!.Contains("Operation=") &&
                    state.ToString()!.Contains("GetAsync")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void TryTranslateGridLayout_GivenNonPostgresException_DoesNotLog()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<PostgresExceptionTranslator>>();
        var translator = CreateTranslator(loggerMock.Object);

        // Act
        var result = translator.TryTranslateGridLayout(new InvalidOperationException("unrelated"), "Get");

        // Assert
        result.Should().BeNull();
        loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run translator tests — confirm compile failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PostgresExceptionTranslatorTests" --no-restore`

Expected: **Build error** — `'PostgresExceptionTranslator' does not contain a constructor that takes 1 arguments` (or similar CS1729). This proves the test drives the refactor.

- [ ] **Step 3: Convert `PostgresExceptionTranslator` to instance class with logger**

Replace the entire file `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` with:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// Translates Npgsql/PostgreSQL exceptions surfaced by EF Core into the domain-layer
/// <see cref="GridLayoutPersistenceException"/>, keeping driver types out of the Application layer.
/// Distinct from <see cref="PostgresExceptionLoggingInterceptor"/>: the interceptor enriches save-failure
/// logs at the EF Core SaveChanges boundary (no operation context); this translator logs the Postgres
/// SqlState with the repository operation name and covers read paths the interceptor does not see.
/// </summary>
public class PostgresExceptionTranslator
{
    private readonly ILogger<PostgresExceptionTranslator> _logger;

    public PostgresExceptionTranslator(ILogger<PostgresExceptionTranslator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a <see cref="GridLayoutPersistenceException"/> wrapping <paramref name="exception"/> when
    /// the chain contains a <see cref="NpgsqlException"/> (which includes <see cref="PostgresException"/>),
    /// or <c>null</c> for anything else so the caller can rethrow unchanged. When a translation occurs,
    /// emits a single <see cref="LogLevel.Warning"/> entry with <c>SqlState</c>, <c>Operation</c>, and the
    /// underlying message — the Persistence boundary is the right place to log Postgres-specific state.
    /// </summary>
    public GridLayoutPersistenceException? TryTranslateGridLayout(Exception exception, string operation)
    {
        var npgsqlEx = FindNpgsqlException(exception);
        if (npgsqlEx is null)
        {
            return null;
        }

        var sqlState = (npgsqlEx as PostgresException)?.SqlState;
        _logger.LogWarning(
            "GridLayout persistence error during {Operation}: SqlState={SqlState} Message={Message}",
            operation, sqlState, exception.Message);

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

Note: this still passes `sqlState` to the **3-arg** `GridLayoutPersistenceException` ctor. The Domain change is in Task 2 — keeping the 3-arg call here lets the build stay green at the end of this task.

- [ ] **Step 4: Update `GridLayoutRepository` to consume the translator via DI**

Replace the entire file `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` with:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly PostgresExceptionTranslator _translator;

    public GridLayoutRepository(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        PostgresExceptionTranslator translator)
    {
        _context = context;
        _timeProvider = timeProvider;
        _translator = translator;
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
            var translated = _translator.TryTranslateGridLayout(ex, nameof(GetAsync));
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
            var translated = _translator.TryTranslateGridLayout(ex, nameof(UpsertAsync));
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
            var translated = _translator.TryTranslateGridLayout(ex, nameof(DeleteAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }
}
```

Only diffs vs. current: added `_translator` field, third ctor parameter `PostgresExceptionTranslator translator`, replaced the three `PostgresExceptionTranslator.TryTranslateGridLayout(...)` static calls with `_translator.TryTranslateGridLayout(...)`. No catch-block shape change.

- [ ] **Step 5: Register the translator in `PersistenceModule`**

Edit `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`. Find the existing line (around line 83):

```csharp
        // Register interceptors
        services.AddScoped<PostgresExceptionLoggingInterceptor>();
```

Replace with:

```csharp
        // Register interceptors
        services.AddScoped<PostgresExceptionLoggingInterceptor>();

        // Register exception translator (used by GridLayoutRepository to surface domain exceptions
        // and log SqlState/Operation at the Persistence boundary — distinct from the SaveChanges
        // interceptor which has no operation context and does not fire on read paths).
        services.AddScoped<PostgresExceptionTranslator>();
```

- [ ] **Step 6: Update `GridLayoutRepositoryTranslationTests` to pass the translator into the repo ctor**

Edit `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`. Two changes:

(a) Add a using directive at the top — find:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
```

Replace with:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
```

(b) Replace every instance of:

```csharp
        var repository = new GridLayoutRepository(context, TimeProvider.System);
```

with:

```csharp
        var translator = new PostgresExceptionTranslator(NullLogger<PostgresExceptionTranslator>.Instance);
        var repository = new GridLayoutRepository(context, TimeProvider.System, translator);
```

There are four such lines (currently at lines 46, 65, 83, 110).

- [ ] **Step 7: Build and run all backend tests — confirm green**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: PASS (0 warnings, 0 errors).

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"`

Expected: PASS. All GridLayout-related tests including the two new translator log tests succeed.

If anything is red, fix before committing.

- [ ] **Step 8: Commit Task 1**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs \
        backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs \
        backend/src/Anela.Heblo.Persistence/PersistenceModule.cs \
        backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs \
        backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs
git commit -m "refactor(persistence): PostgresExceptionTranslator becomes DI instance + logs SqlState/Operation at Warning"
```

---

## Task 2: Drop `SqlState` from `GridLayoutPersistenceException` and clean handler logs

**Goal:** Remove the PostgreSQL-specific `SqlState` property from the Domain exception. Update the translator's ctor call site, three handler log statements, and four handler test fixtures to the new 2-arg form. **At the end of this task NFR-1 is satisfied: no Npgsql vocabulary in the Domain.**

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`

- [ ] **Step 1: Simplify the Domain exception**

Replace the entire file `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
```

The file has no `using` directives — verifies NFR-1 (no Npgsql import in Domain).

- [ ] **Step 2: Run the solution build — confirm cascade of expected compile failures**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: **Build errors** in 7 call sites — these are the next steps. Errors will read roughly:
- `PostgresExceptionTranslator.cs(34,…): error CS1729: 'GridLayoutPersistenceException' does not contain a constructor that takes 3 arguments`
- `GetGridLayoutHandler.cs(70,…): error CS1061: 'GridLayoutPersistenceException' does not contain a definition for 'SqlState'`
- `SaveGridLayoutHandler.cs(42,…): error CS1061: ...`
- `ResetGridLayoutHandler.cs(37,…): error CS1061: ...`
- 4 errors in the handler test files (same CS1729 from `new GridLayoutPersistenceException(... sqlState: "42P01" ...)` calls)

This proves the next steps are required.

- [ ] **Step 3: Update `PostgresExceptionTranslator.cs` ctor call to 2-arg form**

In `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs`, find:

```csharp
        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            sqlState,
            exception);
```

Replace with:

```csharp
        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            exception);
```

(`sqlState` is still computed locally and passed to `LogWarning` — only the ctor signature changed.)

- [ ] **Step 4: Update `GetGridLayoutHandler.cs` log statement**

In `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`, find:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error reading GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new GetGridLayoutResponse { Layout = null };
        }
```

Replace with:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error reading GridLayout for user={UserId} gridKey={GridKey}",
                userId, request.GridKey);
            return new GetGridLayoutResponse { Layout = null };
        }
```

- [ ] **Step 5: Update `SaveGridLayoutHandler.cs` log statement**

In `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`, find:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error saving GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);
        }
```

Replace with:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error saving GridLayout for user={UserId} gridKey={GridKey}",
                userId, request.GridKey);
            return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);
        }
```

- [ ] **Step 6: Update `ResetGridLayoutHandler.cs` log statement**

In `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs`, find:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error resetting GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new ResetGridLayoutResponse(ErrorCodes.DatabaseError);
        }
```

Replace with:

```csharp
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error resetting GridLayout for user={UserId} gridKey={GridKey}",
                userId, request.GridKey);
            return new ResetGridLayoutResponse(ErrorCodes.DatabaseError);
        }
```

- [ ] **Step 7: Update `GetGridLayoutHandlerTests.cs` fixture (line 64)**

In `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`, find:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during GetAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));
```

Replace with:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during GetAsync: relation \"GridLayouts\" does not exist",
                new InvalidOperationException("simulated underlying driver exception")));
```

- [ ] **Step 8: Update `SaveGridLayoutHandlerTests.cs` fixture (line 88)**

In `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs`, find:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during UpsertAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));
```

Replace with:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during UpsertAsync: relation \"GridLayouts\" does not exist",
                new InvalidOperationException("simulated underlying driver exception")));
```

- [ ] **Step 9: Update `ResetGridLayoutHandlerTests.cs` fixture (line 38)**

In `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs`, find:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during DeleteAsync: relation \"GridLayouts\" does not exist",
                sqlState: "42P01",
                new InvalidOperationException("simulated underlying driver exception")));
```

Replace with:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "GridLayout persistence error during DeleteAsync: relation \"GridLayouts\" does not exist",
                new InvalidOperationException("simulated underlying driver exception")));
```

- [ ] **Step 10: Update `GridLayoutHandlerTests.cs` fixture (line 366)**

In `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`, find:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "Database error",
                sqlState: "42P01",
                new InvalidOperationException("simulated error")));
```

Replace with:

```csharp
            .ThrowsAsync(new GridLayoutPersistenceException(
                "Database error",
                new InvalidOperationException("simulated error")));
```

- [ ] **Step 11: Build and run all GridLayout-related tests**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: PASS (0 errors, 0 warnings).

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"`

Expected: PASS. All tests previously green stay green; the new translator log tests still pass.

- [ ] **Step 12: Commit Task 2**

```bash
git add backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs \
        backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs \
        backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs \
        backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs \
        backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs
git commit -m "refactor(domain): drop SqlState from GridLayoutPersistenceException — log moves to Persistence translator"
```

---

## Task 3: Verify acceptance criteria and NFRs

**Goal:** Mechanically verify each acceptance criterion in spec.r1.md.

- [ ] **Step 1: NFR-1 — Domain has no Npgsql references**

Run: `grep -rn "Npgsql" backend/src/Anela.Heblo.Domain/ || echo "OK: no matches"`

Expected: `OK: no matches`.

Run: `dotnet list backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj package | grep -iE "Npgsql|EntityFrameworkCore" || echo "OK: no persistence packages"`

Expected: `OK: no persistence packages`.

- [ ] **Step 2: FR-1 — exception type shape**

Run: `grep -nE "SqlState|using Npgsql" backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs || echo "OK: clean"`

Expected: `OK: clean`.

- [ ] **Step 3: FR-3 — only 2-arg constructor calls remain in the repo**

Run: `grep -rn "new GridLayoutPersistenceException(" backend/`

Expected: every match is the 2-arg form `(message, inner)` (no `sqlState:` keyword argument, no third positional argument that is a string). Production call site is in `PostgresExceptionTranslator.cs`; test call sites are in the four handler test files. Inspect each match visually.

- [ ] **Step 4: FR-4 — no `SqlState` reference in GridLayouts handlers**

Run: `grep -rn "SqlState" backend/src/Anela.Heblo.Application/Features/GridLayouts/ || echo "OK: clean"`

Expected: `OK: clean`.

- [ ] **Step 5: FR-2 — translator log is registered in DI**

Run: `grep -n "PostgresExceptionTranslator" backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

Expected: at least one `AddScoped<PostgresExceptionTranslator>()` registration line.

- [ ] **Step 6: Full build with formatter check**

Run: `dotnet build backend/Anela.Heblo.sln -warnaserror`

Expected: PASS, no warnings.

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: exit 0 (no formatting drift). If non-zero, run `dotnet format backend/Anela.Heblo.sln`, inspect the diff, and amend the appropriate commit (or add a follow-up `chore: format` commit if amending feels wrong).

- [ ] **Step 7: Full test run for the affected suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"`

Expected: PASS. Total test count should be the original count + 2 new translator tests (the log emission test and the no-log-for-non-Postgres test).

- [ ] **Step 8: Full backend solution test run**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: PASS. The Application/Persistence/Domain projects all compile and all suites are green. This is the final NFR-4 (no behavioral regression) gate.

- [ ] **Step 9: Push the branch**

```bash
git push -u origin feat-arch-review-gridlayouts-gridlayoutpersis
```

(No PR opened by this plan — that's a separate decision. The branch carries two clean commits.)

---

## Self-Review

**Spec coverage check** (mapping spec FRs/NFRs → tasks):

- FR-1 (Domain exception simplified) → Task 2 Step 1. Verified Task 3 Step 2.
- FR-2 (Translator logs SqlState before throw) → Task 1 Step 3 (translator body) + Task 1 Step 5 (DI registration). Verified by Task 1 Step 1 test + Task 3 Step 5.
- FR-3 (All call sites updated to 2-arg) → Task 2 Steps 3, 7, 8, 9, 10. Verified Task 3 Step 3.
- FR-4 (Handlers drop `SqlState` from logs) → Task 2 Steps 4, 5, 6. Verified Task 3 Step 4.
- FR-5 (Test coverage preserved + new translator log test) → Task 1 Step 1 (new tests added) + Task 2 Steps 7-10 (test fixtures updated). Verified Task 3 Step 7.
- NFR-1 (Clean Architecture compliance) → Task 2 Step 1 removes the only Npgsql-vocabulary leak. Verified Task 3 Step 1.
- NFR-2 (Backwards-incompatible API change acceptable, single commit) → Plan uses two commits split by concern; spec explicitly allows this since it's a solo-deployable internal app.
- NFR-3 (Warning level, no stack-trace duplication) → Task 1 Step 3 uses `LogWarning` with no exception argument; handlers (Task 2 Steps 4-6) keep their `LogError(ex, …)` so the full chain is only emitted once at the Application boundary. Architecture Review §Decision 3 documents the deliberate duplication with the existing `PostgresExceptionLoggingInterceptor`.
- NFR-4 (No behavioral regression) → Verified Task 3 Step 8 (full solution test run); HTTP status codes and response shapes are not touched by any step.

**Architecture Review amendments applied:**
- Amendment 1 (Keep `TryTranslate?` shape, don't narrow to `PostgresException`) → Task 1 Step 3 preserves `TryTranslateGridLayout(Exception, string) → GridLayoutPersistenceException?` exactly.
- Amendment 2 (Document deliberate dual logging with interceptor) → Task 1 Step 3 XML doc on the translator class explicitly contrasts with `PostgresExceptionLoggingInterceptor`.
- Amendment 3 (DI registration is new, not just verification) → Task 1 Step 5 adds the `AddScoped` call.
- Amendment 4 (Translator log assertion targets state keys, not message string) → Task 1 Step 1 test uses `It.Is<It.IsAnyType>((state, _) => state.ToString().Contains("SqlState=") && state.ToString().Contains("Operation="))` — matches keys/operation, not the full template.

**Placeholder scan:** No "TBD", "implement later", or "Similar to Task N" entries. Every code block contains the exact text to write.

**Type consistency:** Translator method name `TryTranslateGridLayout` is identical across the production file (Task 1 Step 3), repository (Task 1 Step 4), test file (Task 1 Step 1), and DI registration (Task 1 Step 5). Exception ctor uses `(string message, Exception inner)` consistently in Domain, Persistence, and all four test fixtures.

**Gap check:** None found. Every acceptance criterion in spec.r1.md maps to at least one verification step in Task 3.
