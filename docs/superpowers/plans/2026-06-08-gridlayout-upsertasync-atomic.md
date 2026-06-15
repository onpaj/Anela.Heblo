# GridLayoutRepository Atomic Upsert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the racy select-then-insert/update logic in `GridLayoutRepository.UpsertAsync` with a single PostgreSQL `INSERT ... ON CONFLICT DO UPDATE` so concurrent layout saves for the same `(UserId, GridKey)` become idempotent at the database level instead of surfacing as `ErrorCodes.DatabaseError`.

**Architecture:** One production file changes — `GridLayoutRepository.cs`. The new `UpsertAsync` uses `DbContext.Database.ExecuteSqlInterpolatedAsync` with a `FormattableString` (parameterized) `INSERT ... ON CONFLICT ("UserId","GridKey") DO UPDATE ... ` statement. `LastModified` is computed via the injected `TimeProvider` and forced to `DateTimeKind.Unspecified` to match the `timestamp without time zone` column type (otherwise Npgsql throws `InvalidCastException`). The existing `PostgresExceptionTranslator` catch block stays; the existing translator unit tests already prove its contract. The old `UseInMemoryDatabase`-based `UpsertAsync_*` translation tests are deleted (the in-memory provider does not support `ExecuteSqlInterpolatedAsync`) and replaced by a new Testcontainers-backed integration test class that proves: insert, update-in-place, concurrent idempotency, `TimeProvider`-driven `LastModified`, translated exception on real Npgsql failure, and cancellation propagation. The existing `DeleteAsync` in-memory test stays.

**Tech Stack:** .NET 8, EF Core 8, Npgsql, xUnit, FluentAssertions, Testcontainers.PostgreSql 3.6.0.

---

## File Structure

**Modified:**
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — rewrite `UpsertAsync` body. `GetAsync` and `DeleteAsync` untouched.
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` — delete the three `UpsertAsync_*` tests. Keep the file (the one `DeleteAsync_*` test remains).

**Created:**
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs` — Testcontainers-based integration tests covering the new behavior.

**Untouched (callsite verification only):**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — same `try/catch (GridLayoutPersistenceException)` → `ErrorCodes.DatabaseError` contract.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — already walks `InnerException` chains.
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs` — unique index on `(UserId, GridKey)` is the `ON CONFLICT` target.
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` — already covers translator unit-level behavior (direct Npgsql, wrapped, OperationCanceled, plain InvalidOperation).

---

## Pre-flight

- [ ] **Step 1: Verify the worktree builds clean before any edits**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 2: Verify the existing test class layout the new file will mirror**

Open and skim `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`. Confirm the patterns the new file will copy:
- `[Trait("Category", "Integration")]`
- static constructor disables `TestcontainersSettings.ResourceReaperEnabled` for Podman
- `IAsyncLifetime` with `InitializeAsync`/`DisposeAsync`
- `PostgreSqlContainer` field initialised inline with `new PostgreSqlBuilder()`
- manual `SetupSchemaAsync` issuing one `CREATE TABLE` via `NpgsqlConnection`

Expected: file present and uses the exact patterns above (no code change here — just orientation).

---

## Task 1: Add concurrent-upsert failing test, then rewrite UpsertAsync to make it pass

This is the core red→green cycle. The new file plus the new production code land in the same commit because (a) the new test is the gating spec for the new code, and (b) the old code reliably fails the new test against a real PostgreSQL backend.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:36-70`

- [ ] **Step 1: Create the new integration test class with the concurrent failing test**

Write the file `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

[Trait("Category", "Integration")]
public class GridLayoutRepositoryUpsertIntegrationTests : IAsyncLifetime
{
    static GridLayoutRepositoryUpsertIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await SetupSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."GridLayouts" (
                "Id"           serial      NOT NULL PRIMARY KEY,
                "UserId"       varchar(255) NOT NULL,
                "GridKey"      varchar(100) NOT NULL,
                "LayoutJson"   text         NOT NULL,
                "LastModified" timestamp    NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GridLayouts_UserId_GridKey"
                ON public."GridLayouts" ("UserId", "GridKey");
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private GridLayoutRepository CreateRepository(ApplicationDbContext context, TimeProvider? timeProvider = null)
    {
        return new GridLayoutRepository(context, timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentCallsForSameKey_AllSucceedAndLeaveSingleRow()
    {
        // Arrange
        const string userId = "user-concurrent";
        const string gridKey = "grid-concurrent";
        const int parallelism = 20;

        var tasks = Enumerable.Range(0, parallelism)
            .Select(async i =>
            {
                await using var ctx = CreateContext();
                var repo = CreateRepository(ctx);
                await repo.UpsertAsync(userId, gridKey, $"{{\"call\":{i}}}", CancellationToken.None);
            })
            .ToList();

        // Act
        var act = async () => await Task.WhenAll(tasks);

        // Assert: no caller should throw — the unique-index race must not surface to callers.
        await act.Should().NotThrowAsync();

        // Assert: exactly one row exists for the (userId, gridKey) tuple.
        await using var verify = CreateContext();
        var rows = await verify.GridLayouts
            .Where(x => x.UserId == userId && x.GridKey == gridKey)
            .ToListAsync();
        rows.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run the concurrent test against current code to confirm it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_ConcurrentCallsForSameKey_AllSucceedAndLeaveSingleRow"`

Expected: FAIL. The current `UpsertAsync` does select-then-insert. With 20 parallel callers from 20 separate `DbContext` instances, several lose the race and surface `GridLayoutPersistenceException` (translated from the unique-constraint `PostgresException`). The exception bubbles out of `Task.WhenAll`, breaking `NotThrowAsync()`.

(If Docker/Podman is unavailable, the test errors with a Testcontainers startup failure — that is an environment problem, not a code state. Resolve it before proceeding.)

- [ ] **Step 3: Rewrite `UpsertAsync` to use `INSERT ... ON CONFLICT`**

Replace lines 36–70 of `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`. The new body:

```csharp
    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        // Column is 'timestamp without time zone' (see GridLayoutConfiguration via AsUtcTimestamp()).
        // Npgsql throws InvalidCastException when writing a DateTime with Kind=Utc into a non-tz column,
        // so normalise the kind to Unspecified while preserving the UTC instant on the wire.
        var lastModified = DateTime.SpecifyKind(
            _timeProvider.GetUtcNow().UtcDateTime,
            DateTimeKind.Unspecified);

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public."GridLayouts" ("UserId", "GridKey", "LayoutJson", "LastModified")
                VALUES ({userId}, {gridKey}, {layoutJson}, {lastModified})
                ON CONFLICT ("UserId", "GridKey") DO UPDATE
                    SET "LayoutJson" = EXCLUDED."LayoutJson",
                        "LastModified" = EXCLUDED."LastModified"
                """,
                cancellationToken);
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
```

Leave `GetAsync` (lines 18–34) and `DeleteAsync` (lines 72–94) untouched.

- [ ] **Step 4: Run the concurrent test again to confirm it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_ConcurrentCallsForSameKey_AllSucceedAndLeaveSingleRow"`

Expected: PASS. All 20 callers complete; exactly one row exists.

- [ ] **Step 5: Confirm the broader solution still builds (the in-memory translation tests will still compile but the three `UpsertAsync_*` cases will now FAIL — that is expected and addressed in Task 6)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs \
        backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs
git commit -m "fix: make GridLayoutRepository.UpsertAsync atomic via INSERT ON CONFLICT"
```

---

## Task 2: Cover the insert path explicitly

The concurrent test only proves "no failure + one row". Add an explicit insert-path test so future maintainers see the simple case in isolation.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Add the insert-path test**

Append the following `[Fact]` inside the existing class, after the concurrent test:

```csharp
    [Fact]
    public async Task UpsertAsync_NewKey_InsertsSingleRowWithSuppliedValues()
    {
        // Arrange
        const string userId = "user-insert";
        const string gridKey = "grid-insert";
        const string layoutJson = "{\"columns\":[{\"id\":\"name\",\"width\":120}]}";

        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        // Act
        await repo.UpsertAsync(userId, gridKey, layoutJson, CancellationToken.None);

        // Assert
        await using var verify = CreateContext();
        var rows = await verify.GridLayouts
            .Where(x => x.UserId == userId && x.GridKey == gridKey)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].LayoutJson.Should().Be(layoutJson);
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_NewKey_InsertsSingleRowWithSuppliedValues"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: cover UpsertAsync insert path against real PostgreSQL"
```

---

## Task 3: Cover the update-in-place path

Prove that a second call for an existing `(UserId, GridKey)` overwrites in place — `Id` is unchanged, `LayoutJson` and `LastModified` reflect the latest values.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Add the update-path test**

Append inside the existing class:

```csharp
    [Fact]
    public async Task UpsertAsync_ExistingKey_OverwritesInPlace_AndPreservesId()
    {
        // Arrange
        const string userId = "user-update";
        const string gridKey = "grid-update";
        const string firstJson = "{\"v\":1}";
        const string secondJson = "{\"v\":2}";

        await using var ctx1 = CreateContext();
        var repo1 = CreateRepository(ctx1);
        await repo1.UpsertAsync(userId, gridKey, firstJson, CancellationToken.None);

        await using var verify1 = CreateContext();
        var beforeUpdate = await verify1.GridLayouts
            .SingleAsync(x => x.UserId == userId && x.GridKey == gridKey);

        // Act
        await using var ctx2 = CreateContext();
        var repo2 = CreateRepository(ctx2);
        await repo2.UpsertAsync(userId, gridKey, secondJson, CancellationToken.None);

        // Assert
        await using var verify2 = CreateContext();
        var rows = await verify2.GridLayouts
            .Where(x => x.UserId == userId && x.GridKey == gridKey)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Id.Should().Be(beforeUpdate.Id);
        rows[0].LayoutJson.Should().Be(secondJson);
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_ExistingKey_OverwritesInPlace_AndPreservesId"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: cover UpsertAsync update-in-place semantics against real PostgreSQL"
```

---

## Task 4: Cover the TimeProvider-driven LastModified value (FR-3)

Prove that an injected fake `TimeProvider` produces the persisted `LastModified` instant — both on insert and on update — and that no `InvalidCastException` is thrown when writing the `DateTime` value (locking in the `Kind=Unspecified` normalisation from the architecture review).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Add the LastModified test**

Append inside the existing class:

```csharp
    [Fact]
    public async Task UpsertAsync_UsesInjectedTimeProvider_ForLastModified_OnInsertAndUpdate()
    {
        // Arrange
        const string userId = "user-time";
        const string gridKey = "grid-time";
        var insertInstant = new DateTimeOffset(2026, 6, 8, 9, 0, 0, TimeSpan.Zero);
        var updateInstant = new DateTimeOffset(2026, 6, 8, 9, 5, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(insertInstant);

        // Insert
        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx, fakeTime);
            await repo.UpsertAsync(userId, gridKey, "{\"v\":1}", CancellationToken.None);
        }

        await using (var verify = CreateContext())
        {
            var row = await verify.GridLayouts.SingleAsync(x => x.UserId == userId && x.GridKey == gridKey);
            row.LastModified.Should().Be(insertInstant.UtcDateTime);
        }

        // Update
        fakeTime.SetUtcNow(updateInstant);
        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx, fakeTime);
            await repo.UpsertAsync(userId, gridKey, "{\"v\":2}", CancellationToken.None);
        }

        await using (var verifyAfter = CreateContext())
        {
            var row = await verifyAfter.GridLayouts.SingleAsync(x => x.UserId == userId && x.GridKey == gridKey);
            row.LastModified.Should().Be(updateInstant.UtcDateTime);
        }
    }
```

> If `Microsoft.Extensions.Time.Testing.FakeTimeProvider` is not already referenced in the test project, add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />` to `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` in alphabetical position with the other `Microsoft.*` entries. Otherwise the `using Microsoft.Extensions.Time.Testing;` import will not resolve.

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_UsesInjectedTimeProvider_ForLastModified_OnInsertAndUpdate"`
Expected: PASS. The persisted `LastModified` equals the fake clock's `UtcDateTime` on insert; equals the advanced fake clock's `UtcDateTime` after the update.

(If the test fails with `InvalidCastException: Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'`, the `DateTime.SpecifyKind(..., Unspecified)` normalisation in Task 1 step 3 is missing or wrong — re-check the production code change.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "test: verify TimeProvider drives UpsertAsync LastModified across insert and update"
```

---

## Task 5: Cover Npgsql exception translation against the real provider (FR-2)

Force a real PostgreSQL error and prove the repository emits `GridLayoutPersistenceException` with `SqlState` populated and the original exception preserved as `InnerException`.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Add the translation-on-real-failure test**

Append inside the existing class:

```csharp
    [Fact]
    public async Task UpsertAsync_WhenUnderlyingTableMissing_ThrowsGridLayoutPersistenceExceptionWithSqlState()
    {
        // Arrange: drop the table so the next UpsertAsync forces a PostgreSQL "undefined_table" error (SqlState 42P01).
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var drop = conn.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS public.\"GridLayouts\";";
            await drop.ExecuteNonQueryAsync();
        }

        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        try
        {
            // Act
            Func<Task> act = () => repo.UpsertAsync("user-x", "grid-x", "{}", CancellationToken.None);

            // Assert
            var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
            thrown.Which.SqlState.Should().Be("42P01");
            thrown.Which.InnerException.Should().NotBeNull();
        }
        finally
        {
            // Restore schema for any subsequent tests in the same container lifetime.
            await SetupSchemaAsync();
        }
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_WhenUnderlyingTableMissing_ThrowsGridLayoutPersistenceExceptionWithSqlState"`
Expected: PASS. The repository's catch block invokes `PostgresExceptionTranslator.TryTranslateGridLayout`, which finds the `PostgresException` in the inner chain and returns a `GridLayoutPersistenceException` carrying `SqlState = "42P01"`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: verify UpsertAsync translates Npgsql failures to GridLayoutPersistenceException"
```

---

## Task 6: Cover CancellationToken propagation (FR-2)

Prove that a pre-cancelled token surfaces as `OperationCanceledException` (or its `TaskCanceledException` subclass) without being wrapped into `GridLayoutPersistenceException`.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs`

- [ ] **Step 1: Add the cancellation test**

Append inside the existing class:

```csharp
    [Fact]
    public async Task UpsertAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () => repo.UpsertAsync("user-cancel", "grid-cancel", "{}", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryUpsertIntegrationTests.UpsertAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException"`
Expected: PASS. `ExecuteSqlInterpolatedAsync` observes the cancelled token and throws `OperationCanceledException` (typically `TaskCanceledException`); the translator returns `null`; the catch block rethrows.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs
git commit -m "test: verify UpsertAsync propagates cancellation without translation"
```

---

## Task 7: Delete the stale in-memory UpsertAsync translation tests (FR-6)

The three `UpsertAsync_*` cases in `GridLayoutRepositoryTranslationTests` overrode `SaveChangesAsync` to throw. The new code path bypasses `SaveChangesAsync`, so those tests no longer exercise the contract they claim to prove (and against the in-memory provider, `ExecuteSqlInterpolatedAsync` throws `InvalidOperationException`, not the asserted types). The translator-level invariants are already proven by `PostgresExceptionTranslatorTests`; the repository-level translation is proven by the integration test added in Task 5. The `DeleteAsync_*` test stays — `DeleteAsync` still uses `SaveChangesAsync`.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`

- [ ] **Step 1: Remove the three `UpsertAsync_*` test methods**

In `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs`, delete lines 38–91 (the three `[Fact]` methods named `UpsertAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException`, `UpsertAsync_WhenSaveChangesThrowsDbUpdateExceptionWrappingNpgsql_ThrowsGridLayoutPersistenceException`, and `UpsertAsync_WhenSaveChangesThrowsNonPgException_RethrowsOriginal`).

Keep:
- The file header (`using` directives, `namespace`, `public class GridLayoutRepositoryTranslationTests`).
- The private `ThrowingApplicationDbContext` class and `CreateContext()` helper (both are used by the remaining `DeleteAsync_*` test).
- The `[Fact]` method `DeleteAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException` (lines 93–118).

After the edit, the file contains exactly one `[Fact]` (the `DeleteAsync_*` one).

- [ ] **Step 2: Build and run the surviving test class**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build succeeds.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayoutRepositoryTranslationTests"`
Expected: 1 test passes (the surviving `DeleteAsync_*` case). 0 tests fail.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs
git commit -m "test: drop stale in-memory UpsertAsync translation cases superseded by integration coverage"
```

---

## Task 8: Final validation gate

Mirror the project's "before declaring any task done" checklist (`CLAUDE.md`).

- [ ] **Step 1: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: completes with no diff in already-clean files; any whitespace fixes are applied automatically. Stage and commit only formatting noise if it touches our edited files.

- [ ] **Step 2: Build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `0 Error(s)`. Warnings unrelated to this change are acceptable.

- [ ] **Step 3: Run the targeted test suites that this change affects**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"`
Expected: every test in `GridLayoutRepositoryUpsertIntegrationTests`, `GridLayoutRepositoryTranslationTests`, and `PostgresExceptionTranslatorTests` passes. No skipped tests in this set.

- [ ] **Step 4: Run the full backend test suite to confirm nothing else regressed**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: same pass/fail topology as before the change for tests outside the GridLayouts namespace.

- [ ] **Step 5: If `dotnet format` produced any whitespace changes, commit them**

```bash
git add -A
git diff --cached --quiet || git commit -m "chore: dotnet format"
```

If there is no diff, skip the commit — do not create an empty commit.

---

## Spec coverage map

| Spec requirement | Task(s) | Verifies |
|---|---|---|
| FR-1: atomic single-statement upsert | Task 1 (step 3) production code; Tasks 1, 2, 3 tests | Insert path, update-in-place path, concurrent idempotency, `Id` preserved on update |
| FR-2: exception contract preserved | Task 5 (Npgsql → `GridLayoutPersistenceException` with `SqlState`); Task 6 (cancellation pass-through); existing `PostgresExceptionTranslatorTests` (translator-level Npgsql/`DbUpdateException`/non-Npgsql cases) | Translated + rethrown + cancellation paths |
| FR-3: `TimeProvider`-driven `LastModified` | Task 1 step 3 (uses `_timeProvider.GetUtcNow()`); Task 4 test | Insert and update both reflect the injected clock |
| FR-4: `DeleteAsync` untouched | Task 1 step 3 leaves `DeleteAsync` lines 72–94 unchanged; Task 7 keeps the existing `DeleteAsync_*` test | No drift in `DeleteAsync` behavior or coverage |
| FR-5: concurrent integration test | Task 1 test | 20 parallel callers, real Postgres, no exceptions, one row |
| FR-6: rewrite/replace old translation tests | Tasks 5, 6 add the replacements; Task 7 deletes the obsolete `UpsertAsync_*` cases | Translation contract proven against real provider |
| NFR-1: one DB round-trip | Task 1 step 3 production code uses a single `ExecuteSqlInterpolatedAsync` call (no preceding `SELECT`) | Inspect the diff |
| NFR-2: parameterized SQL | Task 1 step 3 uses `FormattableString` interpolation — all four values become Npgsql parameters | Inspect the diff |
| NFR-3: no schema changes | No migration created, `GridLayoutConfiguration` untouched | Inspect the diff |
| NFR-4: handler-side log carries `SqlState` | `SaveGridLayoutHandler` unchanged; Task 5 test asserts the translated exception still exposes `SqlState` | Inspect handler file + Task 5 assertion |
| NFR-5: PostgreSQL-specific OK | `ON CONFLICT` is fine; project targets Postgres only | Confirmed in arch review |

## Arch-review amendments applied

- `LastModified` value uses `DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified)` (Decision 2, prevents `InvalidCastException` against `timestamp without time zone`).
- `ON CONFLICT ("UserId", "GridKey")` uses column inference, not the EF-generated constraint name (Decision 3).
- Stale in-memory `UpsertAsync_*` tests are **deleted** (not migrated) and replaced with Testcontainers-backed integration tests (Amendment 2).
- The interceptor-observability regression for `UpsertAsync` failures is accepted (Amendment 3) — the production change does not add new logging; the handler-side log with `SqlState` is sufficient.
- The concurrent integration test asserts both `LayoutJson` (Task 1) and `LastModified` (Task 4) so FR-3 is locked in against the real provider (Amendment 4 / FR-5 supplement).
