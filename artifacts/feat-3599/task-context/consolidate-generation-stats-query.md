### task: consolidate-generation-stats-query

## Goal
Rewrite `LeafletGenerationRepository.GetGenerationStatsAsync` (backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs:55-67) so it computes all four leaflet-feedback aggregates (`total`, `withFeedback`, `avgPrecision`, `avgStyle`) in exactly one database round trip instead of four sequential queries, with zero change to its public signature, return type, or callers. Add test coverage proving both value-correctness and the single-round-trip guarantee.

## Context

**Why:** `GetGenerationStatsAsync` is called on every `GET /api/leaflet/feedback/list` request (via `GetLeafletFeedbackListHandler.Handle`, backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs:36), alongside `GetGenerationsPagedAsync`. Today that's 5 DB round trips per page load (1 paged + 4 stats); this fix brings it to 2 (1 paged + 1 stats). Flagged by the 2026-07-11 architecture review routine as an easy, low-risk perf win.

**Files to read before starting:**
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — file to modify. Current `GetGenerationStatsAsync` (lines 55-67):
  ```csharp
  public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
  {
      var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
      var withFeedback = await _context.LeafletGenerations
          .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
      var avgPrecision = await _context.LeafletGenerations
          .Where(g => g.PrecisionScore != null)
          .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
      var avgStyle = await _context.LeafletGenerations
          .Where(g => g.StyleScore != null)
          .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
      return new LeafletFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
  }
  ```
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs` — unchanged return type:
  ```csharp
  public sealed record LeafletFeedbackStats(
      int TotalGenerations,
      int TotalWithFeedback,
      double? AvgPrecisionScore,
      double? AvgStyleScore);
  ```
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs` — entity read by the query. Relevant columns: `Id` (Guid), `PrecisionScore` (`int?`), `StyleScore` (`int?`). Also has `Topic`, `UserId`, `CreatedAt`, `FeedbackComment` (not touched by this query).
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:35-73` — the sibling implementation to mirror structurally (`GroupBy(g => 1)` + conditional `Count`/`Average` + null-group guard).
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` — the exact template for the new integration test class (Postgres Testcontainers, `DbCommandInterceptor` counting SQL commands, `[Collection("PostgresIntegration")]`).
- `backend/test/Anela.Heblo.Tests/Common/PostgresSharedContainerFixture.cs` — shared Testcontainers Postgres 16 fixture; call `CreateDatabaseAsync(nameHint)` to get an isolated DB and connection string.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryTests.cs` — existing EF In-Memory unit test class for this repository (constructor pattern: `UseInMemoryDatabase($"LeafletGenerationRepositoryTests_{Guid.NewGuid()}")`, `IDisposable`). Add new `[Fact]` methods here.

**Architectural decision (from arch review, binding):** Use LINQ `GroupBy(g => 1)` with an anonymous-type `Select`, materialized via `FirstOrDefaultAsync`. Do **not** use `FromSqlRaw`/`FromSqlInterpolated` — there is zero precedent for raw SQL in this codebase, and the sibling `IssuedInvoiceRepository.GetSyncStatsAsync` already proves EF Core + Npgsql translates this exact shape (mixed `Count()`, conditional `Count(predicate)`, nullable-selector `Average`/`Max`) into one SQL statement.

**Semantics that must be preserved exactly (FR-2, FR-3):**
- `TotalGenerations` = count of all rows.
- `TotalWithFeedback` = count where `PrecisionScore != null || StyleScore != null`.
- `AvgPrecisionScore` = average of non-null `PrecisionScore` values; `null` if none exist (empty table or all-null column) — must NOT throw and must NOT return `0`.
- `AvgStyleScore` = same rule for `StyleScore`.
- Empty table → return `LeafletFeedbackStats(0, 0, null, null)` without throwing.

## Steps

1. **Rewrite `GetGenerationStatsAsync`** in `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` (replace lines 55-67) with:
   ```csharp
   public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
   {
       var stats = await _context.LeafletGenerations
           .GroupBy(g => 1)
           .Select(g => new
           {
               Total = g.Count(),
               WithFeedback = g.Count(x => x.PrecisionScore != null || x.StyleScore != null),
               AvgPrecision = g.Average(x => (double?)x.PrecisionScore),
               AvgStyle = g.Average(x => (double?)x.StyleScore),
           })
           .FirstOrDefaultAsync(cancellationToken);

       if (stats is null)
           return new LeafletFeedbackStats(0, 0, null, null);

       return new LeafletFeedbackStats(stats.Total, stats.WithFeedback, stats.AvgPrecision, stats.AvgStyle);
   }
   ```
   Do not touch `SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, or `UpdateFeedbackAsync` in this file — only the body of `GetGenerationStatsAsync` changes. Do not change `ILeafletGenerationRepository`, `LeafletFeedbackStats`, or `GetLeafletFeedbackListHandler`.

2. **Add value-correctness unit tests** to `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryTests.cs` (EF In-Memory provider — `GroupBy(g => 1)` evaluates fine against it). Add these `[Fact]` methods to the existing class, following its existing arrange/act/assert style:
   - `GetGenerationStatsAsync_returns_zeroed_stats_when_table_is_empty` — call on empty context, assert `TotalGenerations == 0`, `TotalWithFeedback == 0`, `AvgPrecisionScore == null`, `AvgStyleScore == null`.
   - `GetGenerationStatsAsync_returns_zero_with_feedback_and_null_averages_when_no_rows_have_feedback` — seed 2-3 `LeafletGeneration` rows with `PrecisionScore = null, StyleScore = null`, assert `TotalGenerations` matches row count, `TotalWithFeedback == 0`, both averages `null`.
   - `GetGenerationStatsAsync_computes_correct_totals_and_averages_for_mixed_feedback_rows` — seed a mix: some rows with both scores set (e.g. `PrecisionScore = 4, StyleScore = 5` and `PrecisionScore = 2, StyleScore = 3`), some with neither, some with only `PrecisionScore` set, some with only `StyleScore` set. Assert `TotalGenerations` equals total seeded rows, `TotalWithFeedback` equals count of rows where either score is non-null, `AvgPrecisionScore` equals the manually-computed average of just the non-null `PrecisionScore` values (not counting rows where it's null), and likewise for `AvgStyleScore`.
   
   Each new `LeafletGeneration` needs at minimum `Id = Guid.NewGuid()`, `Topic`, `UserId`, `CreatedAt = DateTimeOffset.UtcNow` set (required fields per the existing test file's pattern), plus the `PrecisionScore`/`StyleScore` values under test.

3. **Add the SQL-shape integration test class** `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs`, modeled directly on `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`:
   ```csharp
   using System.Collections.Generic;
   using System.Data.Common;
   using System.Threading;
   using System.Threading.Tasks;
   using Anela.Heblo.Persistence;
   using Anela.Heblo.Persistence.Features.Leaflet;
   using Anela.Heblo.Tests.Common;
   using FluentAssertions;
   using Microsoft.EntityFrameworkCore;
   using Microsoft.EntityFrameworkCore.Diagnostics;
   using Npgsql;
   using Xunit;

   namespace Anela.Heblo.Tests.Features.Leaflet;

   [Collection("PostgresIntegration")]
   [Trait("Category", "Integration")]
   public class LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests : IAsyncLifetime
   {
       private readonly PostgresSharedContainerFixture _fixture;
       private string _connectionString = null!;
       private readonly CapturingCommandInterceptor _interceptor = new();
       private ApplicationDbContext _context = null!;
       private LeafletGenerationRepository _repository = null!;

       public LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests(PostgresSharedContainerFixture fixture)
       {
           _fixture = fixture;
       }

       public async Task InitializeAsync()
       {
           _connectionString = await _fixture.CreateDatabaseAsync("leafletgenerations");

           // Minimal schema — only the columns GetGenerationStatsAsync touches.
           await using var conn = new NpgsqlConnection(_connectionString);
           await conn.OpenAsync();
           await using var cmd = conn.CreateCommand();
           cmd.CommandText = """
               CREATE TABLE public."LeafletGenerations" (
                   "Id"             uuid NOT NULL PRIMARY KEY,
                   "PrecisionScore" integer NULL,
                   "StyleScore"     integer NULL
               );

               INSERT INTO public."LeafletGenerations" ("Id", "PrecisionScore", "StyleScore") VALUES
                   (gen_random_uuid(), 4,    5),
                   (gen_random_uuid(), 2,    3),
                   (gen_random_uuid(), NULL, NULL),
                   (gen_random_uuid(), 5,    NULL),
                   (gen_random_uuid(), NULL, 1);
               """;
           await cmd.ExecuteNonQueryAsync();

           var options = new DbContextOptionsBuilder<ApplicationDbContext>()
               .UseNpgsql(_connectionString)
               .AddInterceptors(_interceptor)
               .Options;

           _context = new ApplicationDbContext(options);
           _repository = new LeafletGenerationRepository(_context);
       }

       public async Task DisposeAsync()
       {
           await _context.DisposeAsync();
       }

       [Fact]
       public async Task GetGenerationStatsAsync_EmitsExactlyOneSqlCommand()
       {
           _interceptor.Reset();

           await _repository.GetGenerationStatsAsync(CancellationToken.None);

           _interceptor.Commands.Should().HaveCount(1);
       }

       [Fact]
       public async Task GetGenerationStatsAsync_ReturnsCorrectStatsFromRealDatabase()
       {
           _interceptor.Reset();

           var stats = await _repository.GetGenerationStatsAsync(CancellationToken.None);

           stats.TotalGenerations.Should().Be(5);
           stats.TotalWithFeedback.Should().Be(4, "all rows except the fully-null one have at least one non-null score");
           stats.AvgPrecisionScore.Should().Be((4 + 2 + 5) / 3.0, "only 3 rows have a non-null PrecisionScore (4, 2, 5)");
           stats.AvgStyleScore.Should().Be((5 + 3 + 1) / 3.0, "only 3 rows have a non-null StyleScore (5, 3, 1)");
       }

       private sealed class CapturingCommandInterceptor : DbCommandInterceptor
       {
           public List<string> Commands { get; } = new();

           public void Reset() => Commands.Clear();

           public override InterceptionResult<DbDataReader> ReaderExecuting(
               DbCommand command,
               CommandEventData eventData,
               InterceptionResult<DbDataReader> result)
           {
               Commands.Add(command.CommandText);
               return base.ReaderExecuting(command, eventData, result);
           }

           public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
               DbCommand command,
               CommandEventData eventData,
               InterceptionResult<DbDataReader> result,
               CancellationToken cancellationToken = default)
           {
               Commands.Add(command.CommandText);
               return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
           }
       }
   }
   ```
   Also add a `[Fact] GetGenerationStatsAsync_EmptyTable_ReturnsZeroedStatsWithoutThrowing` variant: since the fixture's `InitializeAsync` seeds 5 rows for the two facts above, either (a) add a third fact that truncates the table first (`TRUNCATE TABLE public."LeafletGenerations"` via a fresh `NpgsqlConnection`) then asserts `TotalGenerations == 0`, `TotalWithFeedback == 0`, `AvgPrecisionScore == null`, `AvgStyleScore == null`, or (b) give this empty-table case its own dedicated test class with an empty seed. Prefer (a) — a single extra fact in the same class, truncating before calling the repository method — to avoid spinning up a second Postgres database for one assertion.

4. **Run the full backend test suite** for the touched area:
   ```bash
   cd backend && dotnet test --filter "FullyQualifiedName~LeafletGeneration"
   ```
   Confirm all `LeafletGenerationRepositoryTests` facts pass (EF In-Memory, fast) and both `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests` facts pass (Postgres Testcontainers — requires Docker/Podman available in the environment; this mirrors how `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` already runs in this suite, so no new environment setup is needed).

5. **Run the existing handler tests** to confirm no regression at the consumer level (they mock the repository, so this just proves the contract didn't change):
   ```bash
   cd backend && dotnet test --filter "FullyQualifiedName~GetLeafletFeedbackListHandlerTests"
   ```

6. **Build and format check** per repo convention:
   ```bash
   cd backend && dotnet build && dotnet format --verify-no-changes
   ```

## Acceptance Criteria

- `GetGenerationStatsAsync` issues exactly one SQL query — verified by `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.GetGenerationStatsAsync_EmitsExactlyOneSqlCommand` asserting `interceptor.Commands.Should().HaveCount(1)`.
- Method signature `Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken)` and `ILeafletGenerationRepository` are unchanged (no diff outside `LeafletGenerationRepository.cs`'s method body and the two new/updated test files).
- Value parity with the old four-query implementation confirmed via:
  - New EF In-Memory facts in `LeafletGenerationRepositoryTests.cs` covering empty table, all-feedback-less rows, and mixed rows (including one-sided `PrecisionScore`-only / `StyleScore`-only rows).
  - New Postgres integration fact `GetGenerationStatsAsync_ReturnsCorrectStatsFromRealDatabase` confirming exact totals/averages against a real Npgsql/PostgreSQL round trip.
  - Empty-table fact confirming `LeafletFeedbackStats(0, 0, null, null)` with no exception thrown.
- `GetLeafletFeedbackListHandlerTests` pass unmodified (they mock `ILeafletGenerationRepository`, so a passing run proves the handler contract is untouched).
- `dotnet build` succeeds and `dotnet format --verify-no-changes` reports no formatting diffs.
- No changes to `GetGenerationsPagedAsync`, `GetLeafletFeedbackListHandler`, `LeafletFeedbackStats`, or any DTO/contract type — confirm via `git diff --stat` showing only `LeafletGenerationRepository.cs`, `LeafletGenerationRepositoryTests.cs`, and the new `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs`.
