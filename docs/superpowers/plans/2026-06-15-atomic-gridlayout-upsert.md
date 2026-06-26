# Atomic GridLayout Upsert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the read-then-write `UpsertAsync` body in `GridLayoutRepository` with a single PostgreSQL `INSERT ... ON CONFLICT ... DO UPDATE` statement so concurrent debounced layout saves stop racing on the unique index.

**Architecture:** One method body changes inside the Persistence layer. The public `IGridLayoutRepository.UpsertAsync` contract, the `PostgresExceptionTranslator` catch path, and `DeleteAsync` are unchanged. Coverage moves from an in-memory unit test (`SaveChangesAsync` override) to a real Postgres integration test that exercises the atomic upsert path and the race scenario.

**Tech Stack:** .NET 8, EF Core (`ExecuteSqlInterpolatedAsync`), Npgsql, xUnit, FluentAssertions, Testcontainers (`postgres:16` via the shared `PostgresSharedContainerFixture`).

---

## File Structure

### Production changes
- **Modify** `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`
  - Replace the body of `UpsertAsync` (lines 41–75) with a single `ExecuteSqlInterpolatedAsync` call wrapped in the existing try/catch + translator.
  - Add a one-line comment above the SQL pointing to `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.
  - Add a one-line comment above `DeleteAsync` documenting why it is intentionally left on the EF read-then-write path (FR-5).
  - `using` for `Microsoft.EntityFrameworkCore` already present.

### Memory note
- **Modify** `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`
  - Append `GridLayoutRepository.UpsertAsync` to the "Affected repositories" list.

### Test changes
- **Modify** `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`
  - Remove the three `UpsertAsync_*` tests (their `ThrowOnSaveChanges` seam is bypassed by the new raw-SQL path).
  - Keep the `DeleteAsync_WhenSaveChangesThrowsNpgsqlException_*` test.
- **Create** `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`
  - `[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`, `IAsyncLifetime`.
  - Bootstraps the `GridLayouts` table directly via raw SQL (cannot use `EnsureCreatedAsync` — the project schema requires the `vector` extension that the plain `postgres:16` image lacks; see `BankStatementImportRepositoryIntegrationTests.cs:36-60`).
  - Tests: insert path, update path, single-statement assertion, concurrent insert/insert, concurrent update/update, fake `TimeProvider`, cancellation, non-race Postgres failure (`GridKey` exceeding 100 chars) → `GridLayoutPersistenceException`.

### Files **not** touched
- `IGridLayoutRepository.cs`, `GridLayoutPersistenceException.cs`, `PostgresExceptionTranslator.cs`, `PostgresExceptionLoggingInterceptor.cs`, `GridLayoutConfiguration.cs`, migrations, `SaveGridLayoutHandler*`, `ResetGridLayoutHandler*`, `GetGridLayoutHandler*`, frontend.

---

## Task 1: Document the raw-SQL repository in the gotcha file

**Files:**
- Modify: `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`

- [ ] **Step 1: Open the gotcha file and locate the numbered list under "Affected repositories"**

The list currently contains two entries (1. `LeafletDocumentRepository`, 2. `KnowledgeBaseRepository`).

- [ ] **Step 2: Append a third entry**

Edit the file by adding a new list item after the `KnowledgeBaseRepository` block (after the line that ends with `Update this method whenever the KnowledgeBaseChunk entity gains a new column`). Insert:

```markdown
3. **`backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`**
   - Method: `UpsertAsync()`
   - Uses raw `INSERT ... ON CONFLICT ... DO UPDATE` via `ExecuteSqlInterpolatedAsync` to make the upsert atomic.
   - If a new required column is added to `GridLayout`, both the INSERT column list and the `EXCLUDED` SET clause must be updated.
```

- [ ] **Step 3: Verify the change**

Run:

```bash
grep -A 4 "GridLayoutRepository" memory/gotchas/raw-sql-insert-must-match-ef-mapping.md
```

Expected: four-line block showing the new entry under the heading.

- [ ] **Step 4: Commit**

```bash
git add memory/gotchas/raw-sql-insert-must-match-ef-mapping.md
git commit -m "docs: track GridLayoutRepository in raw-SQL/EF drift gotcha"
```

---

## Task 2: Scaffold the Postgres integration test class

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Write the scaffolding file with fixture wiring and schema bootstrap**

Create the file with this exact content:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class GridLayoutRepositoryUpsertIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;
    private PostgresExceptionTranslator _translator = null!;

    public GridLayoutRepositoryUpsertIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("gridlayouts");

        // Create only the GridLayouts table manually.
        // Do NOT use EnsureCreatedAsync — the project schema depends on the "vector" extension
        // which is not available in the plain postgres:16 image.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS public;
            CREATE TABLE IF NOT EXISTS public."GridLayouts" (
                "Id"           serial                       PRIMARY KEY,
                "UserId"       character varying(255)       NOT NULL,
                "GridKey"      character varying(100)       NOT NULL,
                "LayoutJson"   text                         NOT NULL,
                "LastModified" timestamp without time zone  NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GridLayouts_UserId_GridKey"
                ON public."GridLayouts" ("UserId", "GridKey");
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _context = new ApplicationDbContext(options);
        _translator = new PostgresExceptionTranslator(NullLogger<PostgresExceptionTranslator>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private GridLayoutRepository CreateRepository(TimeProvider? timeProvider = null) =>
        new(_context, timeProvider ?? TimeProvider.System, _translator);

    private async Task<int> CountRowsAsync(string userId, string gridKey)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM public."GridLayouts"
            WHERE "UserId" = @userId AND "GridKey" = @gridKey
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("gridKey", gridKey);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<(int Id, string LayoutJson, DateTime LastModified)> ReadRowAsync(string userId, string gridKey)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "LayoutJson", "LastModified"
            FROM public."GridLayouts"
            WHERE "UserId" = @userId AND "GridKey" = @gridKey
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("gridKey", gridKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"Row for (UserId='{userId}', GridKey='{gridKey}') not found.");
        }
        return (reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2));
    }
}
```

- [ ] **Step 2: Verify the project compiles**

Run from the repository root:

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds (no test cases yet, no test runs).

If the build fails because `Microsoft.Extensions.Time.Testing` is missing from the test project, add the package:

```bash
cd backend && dotnet add test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj package Microsoft.Extensions.Time.Testing
```

Then re-run the build. The package matches the .NET 8 runtime already used by the project.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "test: scaffold GridLayoutRepository upsert integration test"
```

---

## Task 3: Add insert-path and update-path tests (FR-1, FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Append two `[Fact]` methods inside the class**

Insert before the closing `}` of the class:

```csharp
    [Fact]
    public async Task UpsertAsync_WhenRowDoesNotExist_InsertsNewRow()
    {
        // Arrange
        var now = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider);

        // Act
        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":1}", CancellationToken.None);

        // Assert
        var row = await ReadRowAsync("user-1", "grid-1");
        row.LayoutJson.Should().Be("{\"col\":1}");
        row.LastModified.Should().Be(now);
        row.Id.Should().BePositive();
    }

    [Fact]
    public async Task UpsertAsync_WhenRowExists_UpdatesLayoutJsonAndTimestampWithoutChangingId()
    {
        // Arrange
        var first = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 6, 15, 10, 5, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(first, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider);

        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":1}", CancellationToken.None);
        var inserted = await ReadRowAsync("user-1", "grid-1");

        timeProvider.SetUtcNow(new DateTimeOffset(second, TimeSpan.Zero));

        // Act
        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":2}", CancellationToken.None);

        // Assert
        (await CountRowsAsync("user-1", "grid-1")).Should().Be(1);
        var updated = await ReadRowAsync("user-1", "grid-1");
        updated.Id.Should().Be(inserted.Id);
        updated.LayoutJson.Should().Be("{\"col\":2}");
        updated.LastModified.Should().Be(second);
    }
```

- [ ] **Step 2: Run only the new tests**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests"
```

Expected: both tests **PASS** against the existing read-then-write `UpsertAsync` (FR-1 / FR-3 acceptance only require that the persisted state is correct — the current implementation already produces correct state in the sequential single-call case). The tests will continue to pass after the implementation change. They are guards, not bug repros.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: cover GridLayoutRepository upsert insert and update paths"
```

---

## Task 4: Add the concurrent-insert race repro test (FR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Append the concurrent test**

Insert before the closing `}` of the class:

```csharp
    [Fact]
    public async Task UpsertAsync_ConcurrentInsertsForSameKey_AllSucceedAndProduceSingleRow()
    {
        // Arrange
        const int concurrency = 5;
        var payloads = Enumerable.Range(0, concurrency)
            .Select(i => $"{{\"v\":{i}}}")
            .ToArray();

        // Build one repository per task, each backed by its own DbContext + connection.
        // The shared in-test _context is single-threaded by design; concurrent SQL must
        // come from independent connections to actually race on the unique index.
        var contexts = new List<ApplicationDbContext>();
        try
        {
            var tasks = payloads.Select(payload =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;
                var context = new ApplicationDbContext(options);
                contexts.Add(context);
                var repo = new GridLayoutRepository(context, TimeProvider.System, _translator);
                return repo.UpsertAsync("user-race", "grid-race", payload, CancellationToken.None);
            }).ToArray();

            // Act
            await Task.WhenAll(tasks);

            // Assert
            (await CountRowsAsync("user-race", "grid-race")).Should().Be(1);
            var row = await ReadRowAsync("user-race", "grid-race");
            payloads.Should().Contain(row.LayoutJson);
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }
```

- [ ] **Step 2: Run only this test and confirm it fails against the current implementation**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpsertAsync_ConcurrentInsertsForSameKey_AllSucceedAndProduceSingleRow"
```

Expected: **FAIL** with `GridLayoutPersistenceException` (the wrapper around `DbUpdateException` containing `PostgresException` 23505 / `duplicate key value violates unique constraint "IX_GridLayouts_UserId_GridKey"`). This is the bug being fixed.

If the test passes by accident (Postgres did not surface the race in this run), increase `concurrency` to 10 and retry. With independent connections and a tight loop, the duplicate-key violation reproduces consistently.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: reproduce GridLayoutRepository upsert race on duplicate key"
```

---

## Task 5: Add the concurrent-update test and the single-statement guard (FR-1, FR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Append two more `[Fact]` methods**

Insert before the closing `}` of the class:

```csharp
    [Fact]
    public async Task UpsertAsync_ConcurrentUpdatesForSameKey_AllSucceedAndProduceSingleRow()
    {
        // Arrange — seed an existing row so each task takes the UPDATE branch.
        await CreateRepository().UpsertAsync("user-update", "grid-update", "{\"seed\":true}", CancellationToken.None);
        var seededId = (await ReadRowAsync("user-update", "grid-update")).Id;

        const int concurrency = 5;
        var payloads = Enumerable.Range(0, concurrency)
            .Select(i => $"{{\"u\":{i}}}")
            .ToArray();

        var contexts = new List<ApplicationDbContext>();
        try
        {
            var tasks = payloads.Select(payload =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;
                var context = new ApplicationDbContext(options);
                contexts.Add(context);
                var repo = new GridLayoutRepository(context, TimeProvider.System, _translator);
                return repo.UpsertAsync("user-update", "grid-update", payload, CancellationToken.None);
            }).ToArray();

            // Act
            await Task.WhenAll(tasks);

            // Assert
            (await CountRowsAsync("user-update", "grid-update")).Should().Be(1);
            var row = await ReadRowAsync("user-update", "grid-update");
            row.Id.Should().Be(seededId);
            payloads.Should().Contain(row.LayoutJson);
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task UpsertAsync_IssuesExactlyOneStatementPerCall()
    {
        // Arrange
        var statements = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .LogTo(message =>
            {
                if (message.Contains("Executed DbCommand", StringComparison.Ordinal))
                {
                    statements.Add(message);
                }
            }, new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted })
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new GridLayoutRepository(context, TimeProvider.System, _translator);

        // Act
        await repository.UpsertAsync("user-stmt", "grid-stmt", "{\"x\":1}", CancellationToken.None);

        // Assert
        statements.Should().HaveCount(1, "the upsert must be a single round-trip");
        statements[0].Should().Contain("ON CONFLICT", "the statement must use the atomic upsert");
    }
```

- [ ] **Step 2: Run both new tests and confirm expected results**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpsertAsync_ConcurrentUpdatesForSameKey_AllSucceedAndProduceSingleRow|FullyQualifiedName~UpsertAsync_IssuesExactlyOneStatementPerCall"
```

Expected:
- `UpsertAsync_ConcurrentUpdatesForSameKey_*` **PASSES** even against the current implementation (the update-update race is benign — every UPDATE finds the row).
- `UpsertAsync_IssuesExactlyOneStatementPerCall` **FAILS**. The current implementation logs at least two `Executed DbCommand` entries (one for the SELECT, one for the INSERT/UPDATE) and contains no `ON CONFLICT` SQL.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: cover concurrent updates and single-statement invariant for upsert"
```

---

## Task 6: Add the cancellation and non-race-failure tests (FR-4, FR-6)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Append two more `[Fact]` methods**

Insert before the closing `}` of the class:

```csharp
    [Fact]
    public async Task UpsertAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var repository = CreateRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-cancel", "grid-cancel", "{}", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await CountRowsAsync("user-cancel", "grid-cancel")).Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_WhenPostgresRejectsValue_TranslatesToGridLayoutPersistenceException()
    {
        // Arrange — GridKey column is character varying(100); 101 chars triggers a Postgres
        // 22001 string-data-right-truncation error, which is not a unique-violation and
        // exercises the translator's non-race path.
        var repository = CreateRepository();
        var tooLongGridKey = new string('k', 101);

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-bad", tooLongGridKey, "{}", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().NotBeNull();
    }
```

- [ ] **Step 2: Run both new tests and confirm expected results**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpsertAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceled|FullyQualifiedName~UpsertAsync_WhenPostgresRejectsValue_TranslatesToGridLayoutPersistenceException"
```

Expected: both **PASS** against the current implementation as well — the EF SELECT honours the cancellation token, and a too-long `GridKey` produces an Npgsql error that the existing translator already converts to `GridLayoutPersistenceException`. These tests are guards that must keep passing after the implementation change.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: cover cancellation and translator path on upsert"
```

---

## Task 7: Replace `UpsertAsync` with the atomic SQL implementation (FR-1, FR-2, FR-3, FR-4, FR-6)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:41-75`

- [ ] **Step 1: Replace the body of `UpsertAsync`**

Replace lines 41–75 of `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` with:

```csharp
    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        var lastModified = _timeProvider.GetUtcNow().DateTime;

        try
        {
            // Raw SQL: column list must match the EF mapping in GridLayoutConfiguration.
            // See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md when adding columns.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO public.""GridLayouts"" (""UserId"", ""GridKey"", ""LayoutJson"", ""LastModified"")
                   VALUES ({userId}, {gridKey}, {layoutJson}, {lastModified})
                   ON CONFLICT (""UserId"", ""GridKey"") DO UPDATE
                      SET ""LayoutJson""   = EXCLUDED.""LayoutJson"",
                          ""LastModified"" = EXCLUDED.""LastModified""",
                cancellationToken);
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
```

Notes on what changed and what stayed:
- `Id` is omitted from the column list because `GridLayout.Id` is `int` with `IdentityByDefaultColumn` (see `20260409083238_AddGridLayouts.cs:19-20`). Postgres assigns it.
- All four data values are interpolated through `ExecuteSqlInterpolatedAsync`, which sends them as Npgsql parameters — no SQL injection risk.
- `LastModified` is computed in code from `_timeProvider.GetUtcNow().DateTime`, preserving `FakeTimeProvider`-driven test control.
- The catch + translator block is unchanged; `OperationCanceledException` is not an `NpgsqlException` so it falls through `TryTranslateGridLayout` returning `null` and is rethrown.

- [ ] **Step 2: Add a one-line rationale comment above `DeleteAsync` (FR-5)**

Locate the `DeleteAsync` method header (currently `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:77`) and insert directly above it:

```csharp
    // DeleteAsync stays on the EF read-then-write path: a delete/delete race is benign (idempotent), so there is no defect to fix here.
```

- [ ] **Step 3: Build the Persistence project**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Run the full Upsert integration test class**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests"
```

Expected: all eight tests **PASS**, including the previously failing `UpsertAsync_ConcurrentInsertsForSameKey_*` and `UpsertAsync_IssuesExactlyOneStatementPerCall`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs
git commit -m "fix: make GridLayoutRepository.UpsertAsync atomic via ON CONFLICT"
```

---

## Task 8: Remove obsolete in-memory unit tests for `UpsertAsync` (NFR-5)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`

- [ ] **Step 1: Delete the three `UpsertAsync_*` test methods**

Remove these three `[Fact]` methods from the file (lines 40–96 in the current revision):
- `UpsertAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException`
- `UpsertAsync_WhenSaveChangesThrowsDbUpdateExceptionWrappingNpgsql_ThrowsGridLayoutPersistenceException`
- `UpsertAsync_WhenSaveChangesThrowsNonPgException_RethrowsOriginal`

Rationale: their seam — overriding `SaveChangesAsync` on a `UseInMemoryDatabase` context — is bypassed by the new raw-SQL upsert path. Equivalent coverage now lives in `GridLayoutRepositoryUpsertIntegrationTests` against a real Postgres backend (FR-6 acceptance).

Keep the `DeleteAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException` test and the `ThrowingApplicationDbContext` helper as-is. `DeleteAsync` still goes through `SaveChangesAsync` (FR-5).

The resulting file should contain: the `using` directives, the `ThrowingApplicationDbContext` class, the `CreateContext` helper, and one `[Fact]` (the `DeleteAsync_*` one).

- [ ] **Step 2: Build and run the modified test class**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GridLayoutRepositoryTranslationTests"
```

Expected: one test runs (`DeleteAsync_*`), and it **PASSES**.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs
git commit -m "test: drop in-memory upsert translation tests superseded by integration"
```

---

## Task 9: Full backend validation

**Files:** none modified — verification only.

- [ ] **Step 1: Build the full backend solution**

Run:

```bash
cd backend && dotnet build
```

Expected: build succeeds with zero errors and zero new warnings.

- [ ] **Step 2: Apply the project's formatter**

Run:

```bash
cd backend && dotnet format
```

Expected: no diffs to commit. If `dotnet format` modifies files, review the changes — they should only touch whitespace/usings in the files this plan edited. Commit any such formatting changes:

```bash
git add -A
git commit -m "chore: dotnet format after upsert refactor"
```

- [ ] **Step 3: Run all GridLayout-related tests**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GridLayout"
```

Expected: every test passes. This covers the new integration suite, the trimmed translation tests, and the existing handler-level unit tests (`SaveGridLayoutHandlerTests`, `ResetGridLayoutHandlerTests`, `GetGridLayoutHandlerTests`, `GridLayoutHandlerTests`). The handler tests use mocked `IGridLayoutRepository` and must remain green untouched.

- [ ] **Step 4: Run the full backend test suite**

Run:

```bash
cd backend && dotnet test
```

Expected: full suite passes. The change is internal to a single repository method; nothing else should regress.

- [ ] **Step 5: Final verification — confirm the unique-index name matches the SQL**

Run:

```bash
grep -n "IX_GridLayouts_UserId_GridKey" backend/src/Anela.Heblo.Persistence/Migrations/20260409083238_AddGridLayouts.cs
grep -n "ON CONFLICT" backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs
```

Expected: the migration creates the unique index on `("UserId", "GridKey")`, and the repository's `ON CONFLICT` clause targets the same two columns. If either changes in the future, both must move in lockstep — there is no symbolic link.

No commit at this step (verification only).

---

## Self-Review

**Spec coverage:**
- FR-1 (atomic single-statement upsert) → Tasks 7 (impl) + 5 (single-statement assertion test).
- FR-2 (idempotency under concurrency) → Task 4 (insert race) + Task 5 (update race).
- FR-3 (timestamp from `TimeProvider`) → Task 3 (`FakeTimeProvider` round-trip on both insert and update paths).
- FR-4 (public contract unchanged) → no signature change in Task 7; Task 6 cancellation guard; Task 9 full-build pass.
- FR-5 (`DeleteAsync` untouched + rationale note) → Task 7 step 2 adds the one-line comment; Task 8 preserves the `DeleteAsync` unit test.
- FR-6 (translator path retained for non-race failures) → Task 6 too-long-`GridKey` test; Task 7 retains the catch/translator block.
- NFR-1 (single round-trip) → Task 5 single-statement assertion.
- NFR-2 (parameterised SQL) → Task 7 uses `ExecuteSqlInterpolatedAsync` with no string concatenation of user-controlled values; identifiers are literals.
- NFR-3 (no spurious error logs on race) → Race path no longer raises an exception, so the translator's Warning log no longer fires for it (verified implicitly by Task 4 passing).
- NFR-4 (no schema or contract changes) → no migration, no DI, no API surface touched.
- NFR-5 (test coverage) → obsolete tests removed (Task 8); new integration suite added (Tasks 2–6); existing handler tests untouched.
- Architecture review amendments → `Id` omitted from INSERT, translation moved to integration, `DeleteAsync` rationale committed as code comment, observability path documented via translator-only logging (no new log statements added).

**Placeholder scan:** none. Every code step shows the actual code, every command shows the expected outcome.

**Type consistency:** SQL column names match `GridLayoutConfiguration` (`UserId`, `GridKey`, `LayoutJson`, `LastModified`). Schema/table literal (`public."GridLayouts"`) matches the migration. The unique index `IX_GridLayouts_UserId_GridKey` exists in the production migration and is recreated in the integration test bootstrap with the same column order. `LastModified` is `DateTime` (matches the entity, the EF configuration's `AsUtcTimestamp()`, and the migration's `timestamp without time zone`). `TimeProvider.GetUtcNow().DateTime` is used consistently.
