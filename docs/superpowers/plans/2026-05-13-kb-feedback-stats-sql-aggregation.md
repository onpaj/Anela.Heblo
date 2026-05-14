# KnowledgeBase Feedback Stats SQL-Side Aggregation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the in-memory row materialisation in `KnowledgeBaseRepository.GetFeedbackStatsAsync` with EF Core SQL-side `COUNT`/`AVG` aggregates so the Feedback page stats query stops scaling with table size.

**Architecture:** Rewrite the body of a single repository method to issue four constant-size aggregate queries (`CountAsync` all, `CountAsync` with predicate, two `AverageAsync` with nullable-double cast over filtered sets) and assemble the existing `FeedbackAggregateStats` DTO from the four scalars. No contract or schema changes. Tests are added to an existing Testcontainers PostgreSQL integration class, whose schema setup is first extended to include the `KnowledgeBaseQuestionLogs` table (currently absent from the test schema, which is why no test currently covers this method).

**Tech Stack:** .NET 8, EF Core (Npgsql provider), xUnit, Testcontainers PostgreSQL (`pgvector/pgvector:pg16` image), `KnowledgeBaseRepositoryIntegrationTests`.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — replace the body of `GetFeedbackStatsAsync` (lines 299–322). Public method signature is unchanged.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs` — extend `SetupSchemaAsync` to create the `KnowledgeBaseQuestionLogs` table; add four new `[Fact]` tests covering FR-4 scenarios; add a private helper for inserting feedback-log rows.

**No new files.** No new namespaces or interfaces. No schema migrations. No frontend/OpenAPI client regeneration.

**Domain shapes (read-only references — do NOT modify):**
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseQuestionLog.cs` — entity with `PrecisionScore int?`, `StyleScore int?` and other columns.
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/FeedbackAggregateStats.cs` — DTO with `TotalQuestions`, `TotalWithFeedback`, `AvgPrecisionScore`, `AvgStyleScore`.
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseQuestionLogConfiguration.cs` — EF Core mapping; the entity lives in the `public` schema.

---

## Task 1: Extend integration test schema to include `KnowledgeBaseQuestionLogs`

**Why first:** Without this, no FR-4 test can run against the real Postgres container. The existing schema setup only creates `KnowledgeBaseDocuments` and `KnowledgeBaseChunks`. Inserting `KnowledgeBaseQuestionLog` rows via EF Core would fail at runtime with a missing-relation error.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs:52-91` — extend `SetupSchemaAsync`.

- [ ] **Step 1: Write a failing schema-presence test**

Add this `[Fact]` at the bottom of the class (just above the closing brace at line 331), so it runs against the post-setup schema. The intent is purely to assert the table exists; we will delete this scaffolding test in Task 2 once real feedback tests cover the same surface.

```csharp
    [Fact]
    public async Task SetupSchema_CreatesKnowledgeBaseQuestionLogsTable()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = 'KnowledgeBaseQuestionLogs'
            );
            """;

        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        Assert.True(exists, "KnowledgeBaseQuestionLogs table must be created by SetupSchemaAsync");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetupSchema_CreatesKnowledgeBaseQuestionLogsTable" --logger "console;verbosity=normal"`

Expected: FAIL. Assertion fails with `KnowledgeBaseQuestionLogs table must be created by SetupSchemaAsync` because `SetupSchemaAsync` does not create it.

- [ ] **Step 3: Extend `SetupSchemaAsync` to create the table**

In `KnowledgeBaseRepositoryIntegrationTests.cs`, modify `SetupSchemaAsync` (currently lines 52–91). Append the `KnowledgeBaseQuestionLogs` table creation to the existing `cmd.CommandText` triple-quoted string. The column types and nullability must match the EF Core entity mapping in `KnowledgeBaseQuestionLogConfiguration` and the entity property types in `KnowledgeBaseQuestionLog.cs`. Note `CreatedAt` is `DateTimeOffset` so it maps to `timestamp with time zone` (`timestamptz`).

Replace the existing `SetupSchemaAsync` method body with:

```csharp
    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseDocuments" (
                "Id"           uuid NOT NULL PRIMARY KEY,
                "Filename"     varchar(500) NOT NULL,
                "SourcePath"   varchar(1000) NOT NULL,
                "ContentType"  varchar(100) NOT NULL,
                "ContentHash"  varchar(64) NOT NULL UNIQUE,
                "Status"       varchar(50) NOT NULL,
                "DocumentType" integer NOT NULL DEFAULT 0,
                "CreatedAt"    timestamp NOT NULL,
                "IndexedAt"    timestamp NULL,
                "DriveId"      text NULL,
                "GraphItemId"  text NULL
            );

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseChunks" (
                "Id"           uuid NOT NULL PRIMARY KEY,
                "DocumentId"   uuid NOT NULL REFERENCES public."KnowledgeBaseDocuments"("Id") ON DELETE CASCADE,
                "ChunkIndex"   integer NOT NULL,
                "Content"      text NOT NULL DEFAULT '',
                "Summary"      text NOT NULL DEFAULT '',
                "DocumentType" integer NOT NULL DEFAULT 0,
                "Embedding"    vector(3)
            );

            CREATE INDEX IF NOT EXISTS idx_kb_chunks_embedding
                ON public."KnowledgeBaseChunks"
                USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseQuestionLogs" (
                "Id"              uuid NOT NULL PRIMARY KEY,
                "Question"        text NOT NULL,
                "Answer"          text NOT NULL,
                "TopK"            integer NOT NULL,
                "SourceCount"     integer NOT NULL,
                "DurationMs"      bigint NOT NULL,
                "CreatedAt"       timestamp with time zone NOT NULL,
                "UserId"          text NULL,
                "PrecisionScore"  integer NULL,
                "StyleScore"      integer NULL,
                "FeedbackComment" text NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetupSchema_CreatesKnowledgeBaseQuestionLogsTable" --logger "console;verbosity=normal"`

Expected: PASS — the table now exists.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs
git commit -m "test(knowledge-base): create KnowledgeBaseQuestionLogs table in integration test schema"
```

---

## Task 2: Write failing feedback-stats integration tests (TDD red phase)

**Why:** Before changing `GetFeedbackStatsAsync`, lock in the four FR-4 scenarios against the real Postgres container so any regression in SQL translation or rounding is caught.

The existing implementation already returns correct values for these scenarios (the bug is performance, not correctness). The tests below should therefore PASS against the current implementation — we use them to (a) prove the schema setup from Task 1 is usable and (b) provide a regression guard for the rewrite in Task 3. The "TDD red phase" here is on the schema-setup change, not on the production code; the existing scaffold test from Task 1 is replaced by these realistic tests.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs` — delete the scaffold `SetupSchema_CreatesKnowledgeBaseQuestionLogsTable` test from Task 1, add a private builder helper, and add four feedback-stats `[Fact]` tests.

- [ ] **Step 1: Delete the scaffold test from Task 1**

Remove the `SetupSchema_CreatesKnowledgeBaseQuestionLogsTable` method added in Task 1. The four new tests below cover the same schema indirectly by inserting and querying rows.

- [ ] **Step 2: Add a private helper for building question-log rows**

Insert this private static method directly below the existing `MakeDocument` helper (around line 110 in the current file, just before the first `[Fact]` test):

```csharp
    private static KnowledgeBaseQuestionLog MakeQuestionLog(
        int? precisionScore = null,
        int? styleScore = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Question = "q",
            Answer = "a",
            TopK = 5,
            SourceCount = 1,
            DurationMs = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = null,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
            FeedbackComment = null,
        };
```

- [ ] **Step 3: Add the empty-table test**

Add this `[Fact]` at the bottom of the class (just above the closing brace):

```csharp
    [Fact]
    public async Task GetFeedbackStatsAsync_EmptyTable_ReturnsZeroCountsAndNullAverages()
    {
        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(0, stats.TotalQuestions);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }
```

- [ ] **Step 4: Add the rows-but-no-feedback test**

Add this `[Fact]` immediately after the previous one:

```csharp
    [Fact]
    public async Task GetFeedbackStatsAsync_RowsWithoutScores_ReturnsTotalsAndNullAverages()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(),
            MakeQuestionLog(),
            MakeQuestionLog());
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(3, stats.TotalQuestions);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }
```

- [ ] **Step 5: Add the mixed-feedback test**

Add this `[Fact]` immediately after the previous one. The dataset contains:
- 1 row with neither score
- 1 row with precision only (`PrecisionScore = 5`)
- 1 row with style only (`StyleScore = 3`)
- 1 row with both (`PrecisionScore = 4`, `StyleScore = 5`)

So `TotalQuestions = 4`, `TotalWithFeedback = 3`, `AvgPrecisionScore = (5 + 4) / 2 = 4.5`, `AvgStyleScore = (3 + 5) / 2 = 4.0`.

```csharp
    [Fact]
    public async Task GetFeedbackStatsAsync_MixedFeedback_ReturnsCorrectCountsAndAverages()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(),
            MakeQuestionLog(precisionScore: 5),
            MakeQuestionLog(styleScore: 3),
            MakeQuestionLog(precisionScore: 4, styleScore: 5));
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(4, stats.TotalQuestions);
        Assert.Equal(3, stats.TotalWithFeedback);
        Assert.Equal(4.5, stats.AvgPrecisionScore);
        Assert.Equal(4.0, stats.AvgStyleScore);
    }
```

- [ ] **Step 6: Add the rounding test**

Add this `[Fact]` immediately after the previous one. Three precision scores `5, 5, 4` average to `4.6666...`, which `Math.Round(_, 1)` (banker's rounding, default `MidpointRounding.ToEven`) rounds to `4.7`. Two style scores `2, 3` average to exactly `2.5`, which banker's rounding rounds to `2.5` (no rounding needed at 1 decimal — already exact). To exercise the banker's-rounding midpoint, also pick `3, 2` style scores so the average is `2.5` (already exact at 1 dp). Use precision `1, 2` → average `1.5` (exact at 1 dp). The point of this test is: rounding does not throw and matches `Math.Round(value, 1)` for a known computed value.

```csharp
    [Fact]
    public async Task GetFeedbackStatsAsync_RoundsAveragesToOneDecimalUsingBankersRounding()
    {
        // PrecisionScore raw average = (5 + 5 + 4) / 3 = 4.6666... -> 4.7
        // StyleScore raw average     = (1 + 2) / 2     = 1.5       -> 1.5
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(precisionScore: 5, styleScore: 1),
            MakeQuestionLog(precisionScore: 5, styleScore: 2),
            MakeQuestionLog(precisionScore: 4));
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(3, stats.TotalQuestions);
        Assert.Equal(3, stats.TotalWithFeedback);
        Assert.NotNull(stats.AvgPrecisionScore);
        Assert.NotNull(stats.AvgStyleScore);
        Assert.Equal(Math.Round((5d + 5d + 4d) / 3d, 1), stats.AvgPrecisionScore!.Value);
        Assert.Equal(Math.Round((1d + 2d) / 2d, 1), stats.AvgStyleScore!.Value);
    }
```

- [ ] **Step 7: Run all four feedback tests to verify they pass against the current implementation**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetFeedbackStatsAsync" --logger "console;verbosity=normal"`

Expected: 4 tests PASS. The current implementation already returns correct values; we are establishing a regression baseline before changing the implementation. Each test runs against a fresh Testcontainer instance because the class implements `IAsyncLifetime` and re-creates the schema per test class lifetime (each `[Fact]` shares the same container but the table is empty at start of class; tests inside the class share data. **Important:** rerun the tests one at a time if cross-test pollution becomes visible — current container lifecycle gives each test class one container, not one per test.)

Actually, looking at the class, `_container` is per-instance (xUnit creates one instance per test by default), so each `[Fact]` gets a fresh container — good. No clean-up needed between tests.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs
git commit -m "test(knowledge-base): cover GetFeedbackStatsAsync FR-4 scenarios"
```

---

## Task 3: Rewrite `GetFeedbackStatsAsync` to use SQL-side aggregation

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs:299-322` — replace method body. Public signature, namespace, and return type are unchanged.

- [ ] **Step 1: Replace the method body**

Locate `GetFeedbackStatsAsync` (currently lines 299–322). Replace the entire method with the version below. All four DB calls forward the `CancellationToken`; no row materialisation occurs; the nullable-double cast in `AverageAsync` makes empty filter sets return `null` without throwing.

```csharp
    public async Task<FeedbackAggregateStats> GetFeedbackStatsAsync(CancellationToken ct = default)
    {
        var totalQuestions = await _context.KnowledgeBaseQuestionLogs
            .CountAsync(ct);

        var totalWithFeedback = await _context.KnowledgeBaseQuestionLogs
            .CountAsync(l => l.PrecisionScore != null || l.StyleScore != null, ct);

        var avgPrecision = await _context.KnowledgeBaseQuestionLogs
            .Where(l => l.PrecisionScore != null)
            .AverageAsync(l => (double?)l.PrecisionScore, ct);

        var avgStyle = await _context.KnowledgeBaseQuestionLogs
            .Where(l => l.StyleScore != null)
            .AverageAsync(l => (double?)l.StyleScore, ct);

        return new FeedbackAggregateStats
        {
            TotalQuestions = totalQuestions,
            TotalWithFeedback = totalWithFeedback,
            AvgPrecisionScore = avgPrecision.HasValue ? Math.Round(avgPrecision.Value, 1) : null,
            AvgStyleScore = avgStyle.HasValue ? Math.Round(avgStyle.Value, 1) : null,
        };
    }
```

Notes:
- `Math.Round(value, 1)` uses default `MidpointRounding.ToEven` — do **not** pass `MidpointRounding.AwayFromZero`. The spec requires exact UI parity with the prior implementation, which used the default.
- Do not consolidate the four calls into a `GroupBy(_ => 1).Select(...)` projection. Arch-review Decision 1 explicitly rejects it.
- Do not wrap in a transaction (arch-review Decision 3).

- [ ] **Step 2: Build to confirm compilation**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run the four feedback-stats tests to confirm they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetFeedbackStatsAsync" --logger "console;verbosity=normal"`

Expected: 4 tests PASS — same outputs as in Task 2 step 7, now produced by SQL-side aggregation.

- [ ] **Step 4: Run the full integration test class to confirm no collateral damage**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseRepositoryIntegrationTests" --logger "console;verbosity=normal"`

Expected: all tests in `KnowledgeBaseRepositoryIntegrationTests` PASS (existing tests for `AddChunksAsync`, `SearchSimilarAsync`, `GetChunkByIdAsync`, etc. plus the four new feedback tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
git commit -m "perf(knowledge-base): aggregate feedback stats in SQL instead of materialising rows"
```

---

## Task 4: Verify SQL translation does not materialise rows (FR-1 acceptance)

**Why:** FR-1's acceptance criterion includes verifying that the EF Core query log shows aggregate-only SQL — no `SELECT *` and no `Question`/`Answer` column projection. We add a one-off test that captures executed SQL via `DbContext` logging and asserts the patterns.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs` — add one `[Fact]` and a small private `LogCapturingLoggerProvider` class scoped to the test file.

- [ ] **Step 1: Add a SQL-capture helper class inside the test file**

At the end of the file (after the closing brace of `KnowledgeBaseRepositoryIntegrationTests`), append the following helper. It captures EF Core's `Microsoft.EntityFrameworkCore.Database.Command` log messages into a list for assertion.

```csharp
internal sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    public List<string> Messages { get; } = new();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        new CapturingLogger(Messages);

    public void Dispose() { }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
```

Add the required using directive at the top of the file if not already present:

```csharp
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Add the SQL-shape test**

Add this `[Fact]` to `KnowledgeBaseRepositoryIntegrationTests`, just below the rounding test from Task 2. It builds a *separate* `ApplicationDbContext` wired up with the capturing logger, invokes the repository, and asserts the executed SQL contains the expected aggregate forms and does **not** project the `Question` or `Answer` columns.

```csharp
    [Fact]
    public async Task GetFeedbackStatsAsync_ExecutesAggregateSqlWithoutMaterialisingRows()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(precisionScore: 5, styleScore: 4),
            MakeQuestionLog());
        await _context.SaveChangesAsync();

        var loggerProvider = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging()
            .Options;

        await using var ctx = new ApplicationDbContext(options);
        var repo = new KnowledgeBaseRepository(ctx);

        var stats = await repo.GetFeedbackStatsAsync();

        Assert.Equal(2, stats.TotalQuestions);

        var sql = string.Join("\n", loggerProvider.Messages);
        Assert.Contains("COUNT(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AVG(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Question\"", sql);
        Assert.DoesNotContain("\"Answer\"", sql);
    }
```

- [ ] **Step 3: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetFeedbackStatsAsync_ExecutesAggregateSqlWithoutMaterialisingRows" --logger "console;verbosity=normal"`

Expected: PASS. The captured SQL log contains `COUNT(*)` and `AVG(` substrings, and contains no `"Question"` or `"Answer"` identifiers.

If FAIL with "did not contain COUNT(*)": EF Core may log the aggregate as `COUNT(*)::int` or wrap inside a subquery — adjust the substring search to `COUNT(` and re-run. The intent is to confirm aggregation, not match exact whitespace.

If FAIL with "contains \"Question\"" — that is a real regression in the repository implementation and must be fixed in Task 3 before continuing.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs
git commit -m "test(knowledge-base): assert GetFeedbackStatsAsync emits aggregate SQL without row materialisation"
```

---

## Task 5: Final validation

**Why:** Confirm the full backend solution builds, formats cleanly, and the integration test class passes end-to-end.

**Files:** none modified.

- [ ] **Step 1: Run `dotnet build` on the full backend solution**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: `Build succeeded. 0 Error(s)`.

If the warning count is non-zero, check whether any new warning was introduced by Tasks 1–4. The repository policy is to keep warnings flat; address only warnings you introduced. Pre-existing warnings stay as-is.

- [ ] **Step 2: Run `dotnet format` on the modified files**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`

Expected: command exits 0 with no formatting changes printed. If it makes edits, review them and amend the most recent commit:

```bash
git add -u
git commit --amend --no-edit
```

- [ ] **Step 3: Run the full `KnowledgeBaseRepositoryIntegrationTests` class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseRepositoryIntegrationTests" --logger "console;verbosity=normal"`

Expected: all tests PASS, including:
- Existing tests for `AddChunksAsync`, `SearchSimilarAsync`, `GetChunkByIdAsync`, `AddDocumentAsync`, `DeleteDocumentAsync`, `GetDocumentByGraphItemIdAsync`.
- `GetFeedbackStatsAsync_EmptyTable_ReturnsZeroCountsAndNullAverages`
- `GetFeedbackStatsAsync_RowsWithoutScores_ReturnsTotalsAndNullAverages`
- `GetFeedbackStatsAsync_MixedFeedback_ReturnsCorrectCountsAndAverages`
- `GetFeedbackStatsAsync_RoundsAveragesToOneDecimalUsingBankersRounding`
- `GetFeedbackStatsAsync_ExecutesAggregateSqlWithoutMaterialisingRows`

- [ ] **Step 4: Run the full backend test project (smoke check for cross-test regressions)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --logger "console;verbosity=minimal"`

Expected: all tests PASS, including the unrelated unit/integration tests. If something unrelated fails, check whether it was already failing on the branch tip before the plan was executed; if so, it is out of scope.

- [ ] **Step 5: No further commit needed**

If Steps 1–4 produced no edits, there is nothing to commit. The change is complete.

---

## Spec Coverage Check

| Spec / Arch-Review item | Covered by |
|---|---|
| FR-1: SQL-side aggregation, no `ToListAsync`, cancellation token propagation | Task 3 step 1; Task 4 step 2 (SQL-shape assertion) |
| FR-2: Identical `FeedbackAggregateStats` shape and rounding (banker's rounding) | Task 3 step 1 (no DTO changes; `Math.Round(_, 1)` default); Task 2 step 6 (rounding test) |
| FR-3: Empty / no-score behaviour does not throw | Task 2 step 3 (empty table); Task 2 step 4 (no scores anywhere); Task 3 step 1 (nullable-double cast) |
| FR-4: Test coverage for empty / no-feedback / mixed / rounding cases | Task 2 steps 3, 4, 5, 6 |
| FR-4 prerequisite (arch-review amendment 1): schema setup for `KnowledgeBaseQuestionLogs` | Task 1 step 3 |
| FR-4 pinning (arch-review amendment 2): Testcontainers PostgreSQL, not InMemory | All tests live in `KnowledgeBaseRepositoryIntegrationTests`, which uses `PostgreSqlContainer` |
| Arch-review amendment 3: do not use `GroupBy(_=>1)` projection | Task 3 step 1 explicit guidance + code |
| Arch-review Decision 2: nullable-double cast in `AverageAsync` | Task 3 step 1 code |
| Arch-review Decision 3: no explicit transaction | Task 3 step 1 (none added) |
| NFR-1: bytes transferred constant; latency target — implicitly satisfied by FR-1 (four scalar aggregates) | Task 4 SQL-shape test confirms no row projection |
| NFR-2: cancellation propagation | Task 3 step 1 — every `*Async` call receives `ct` |
| NFR-3: backward compatibility — signature unchanged, DTO unchanged, no migration, no client regen | Task 3 step 1 (method signature unchanged); no migration file added; no contract file changed |
| Out-of-scope items (caching, new indexes, UI changes, other repos) | Not touched in any task |

No spec requirements are missing from the plan.
