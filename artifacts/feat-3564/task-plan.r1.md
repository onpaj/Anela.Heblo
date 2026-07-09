# Collapse GetSyncStatsAsync Into a Single Query — Implementation Plan

**Goal:** Rewrite `IssuedInvoiceRepository.GetSyncStatsAsync` so it computes `TotalInvoices`, `SyncedInvoices`, `InvoicesWithErrors`, `CriticalErrors`, and `LastSyncTime` in exactly one database round trip (one `GroupBy(_ => 1).Select(...)` aggregate query) instead of the current four `CountAsync` calls plus one `MaxAsync` call, with no change to the method's public signature, the `IIssuedInvoiceRepository` interface, or the `IssuedInvoiceSyncStats` DTO. Add a durable, CI-enforced regression guard (Postgres/Testcontainers `SqlShapeTests`) proving the round-trip claim, plus InMemory regression coverage for `LastSyncTime` correctness.

**Architecture:** Pure internal-implementation change confined to one method body in `Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`. No module-boundary, contract, or DTO change. Verification of the "1 round trip" claim uses the codebase's existing `PostgresSharedContainerFixture` + `DbCommandInterceptor` pattern (see `PhotobankRepositoryGetTagsSqlShapeTests.cs`), since the existing InMemory-provider unit tests cannot observe SQL round-trip count.

**Tech Stack:** .NET 8, EF Core (Npgsql provider), xUnit, Moq, FluentAssertions, Testcontainers Postgres (`postgres:16`).

---

### task: collapse-sync-stats-query

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:35-58`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`

**Context:** This refactor does not change observable correctness (it only collapses round trips), so there is no InMemory-observable "red" state to drive from — the InMemory provider cannot detect round-trip count at all (that's what task `add-sync-stats-sql-shape-test` is for). This task therefore starts by pinning current `LastSyncTime` behavior with a regression test (expected to pass immediately, both before and after the rewrite), then performs the rewrite, then re-verifies nothing broke.

- [ ] **Step 1: Add LastSyncTime regression coverage to the existing InMemory test file (pins current behavior before refactor)**

  In `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`, extend the existing `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats` test's Assert block (the current implementation lacks any `LastSyncTime` assertion, so a correctness regression in that specific aggregate would not be caught) and add two new `[Fact]`s. Replace the existing test's Assert section:

  ```csharp
      // Assert
      Assert.Equal(4, stats.TotalInvoices); // Only in-range invoices
      Assert.Equal(1, stats.SyncedInvoices); // Only synced invoice
      Assert.Equal(3, stats.UnsyncedInvoices); // Unsynced, error, paired
      Assert.Equal(2, stats.InvoicesWithErrors); // Error and paired
      Assert.Equal(1, stats.CriticalErrors); // Only error invoice (paired is not critical)
  ```

  with:

  ```csharp
      // Assert
      Assert.Equal(4, stats.TotalInvoices); // Only in-range invoices
      Assert.Equal(1, stats.SyncedInvoices); // Only synced invoice
      Assert.Equal(3, stats.UnsyncedInvoices); // Unsynced, error, paired
      Assert.Equal(2, stats.InvoicesWithErrors); // Error and paired
      Assert.Equal(1, stats.CriticalErrors); // Only error invoice (paired is not critical)
      Assert.Equal(
          new[] { syncedInvoice.LastSyncTime, errorInvoice.LastSyncTime, pairedInvoice.LastSyncTime }.Max(),
          stats.LastSyncTime); // Max LastSyncTime among in-range invoices that have one; unsyncedInvoice/oldInvoice have none
  ```

  Then add `using System.Linq;` to the top of the file if not already present (check first — the file currently has no `System.Linq` using and calls `.ToList()`/`.First()` via LINQ extension methods on `IEnumerable`, so add it if the `.Max()` call above doesn't resolve).

  Add two new `[Fact]` methods immediately after `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats` (before `GetPaginatedAsync_WithFilters_ReturnsFilteredAndPaginatedResults`):

  ```csharp
      [Fact]
      public async Task GetSyncStatsAsync_WithMixedSyncTimes_ReturnsMaxLastSyncTime()
      {
          // Arrange
          var dateFrom = DateTime.Today.AddDays(-7);
          var dateTo = DateTime.Today;

          var earlySynced = new IssuedInvoice { Id = "INV-EARLY", InvoiceDate = DateTime.Today.AddDays(-3), DueDate = DateTime.Today.AddDays(27), TaxDate = DateTime.Today.AddDays(-3) };
          earlySynced.SyncSucceeded(CreateTestSyncData());

          var neverSynced = new IssuedInvoice { Id = "INV-NEVERSYNCED", InvoiceDate = DateTime.Today.AddDays(-2), DueDate = DateTime.Today.AddDays(28), TaxDate = DateTime.Today.AddDays(-2) };

          var lateSynced = new IssuedInvoice { Id = "INV-LATE", InvoiceDate = DateTime.Today.AddDays(-1), DueDate = DateTime.Today.AddDays(29), TaxDate = DateTime.Today.AddDays(-1) };
          lateSynced.SyncSucceeded(CreateTestSyncData());

          await _repository.AddAsync(earlySynced);
          await _repository.AddAsync(neverSynced);
          await _repository.AddAsync(lateSynced);
          await _repository.SaveChangesAsync();

          // Act
          var stats = await _repository.GetSyncStatsAsync(dateFrom, dateTo);

          // Assert
          Assert.Equal(new[] { earlySynced.LastSyncTime, lateSynced.LastSyncTime }.Max(), stats.LastSyncTime);
      }

      [Fact]
      public async Task GetSyncStatsAsync_WithNoInvoiceHavingLastSyncTime_ReturnsNullLastSyncTime()
      {
          // Arrange
          var dateFrom = DateTime.Today.AddDays(-7);
          var dateTo = DateTime.Today;

          var unsyncedOne = new IssuedInvoice { Id = "INV-NOSYNC1", InvoiceDate = DateTime.Today.AddDays(-2), DueDate = DateTime.Today.AddDays(28), TaxDate = DateTime.Today.AddDays(-2) };
          var unsyncedTwo = new IssuedInvoice { Id = "INV-NOSYNC2", InvoiceDate = DateTime.Today.AddDays(-1), DueDate = DateTime.Today.AddDays(29), TaxDate = DateTime.Today.AddDays(-1) };

          await _repository.AddAsync(unsyncedOne);
          await _repository.AddAsync(unsyncedTwo);
          await _repository.SaveChangesAsync();

          // Act
          var stats = await _repository.GetSyncStatsAsync(dateFrom, dateTo);

          // Assert
          Assert.Equal(2, stats.TotalInvoices);
          Assert.Equal(0, stats.SyncedInvoices);
          Assert.Null(stats.LastSyncTime);
      }
  ```

- [ ] **Step 2: Run the extended test file — expect all tests to PASS**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests"
  ```

  Expected: all tests pass, including the three `GetSyncStatsAsync_*` tests. This is expected to pass against the *current* (pre-rewrite) five-query implementation too — correctness is unchanged by the upcoming refactor, so this step pins the baseline behavior rather than demonstrating a red state. (The genuine failing-test-first verification for this change — the "1 round trip" claim — is delivered by the `add-sync-stats-sql-shape-test` task below, since the InMemory provider used here cannot observe SQL round-trip count.)

- [ ] **Step 3: Rewrite `GetSyncStatsAsync` to a single grouped aggregate query**

  In `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`, replace the method body (lines 35-58):

  ```csharp
      public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
      {
          var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

          var totalInvoices = await query.CountAsync(cancellationToken);
          var syncedInvoices = await query.CountAsync(x => x.IsSynced, cancellationToken);
          var unsyncedInvoices = totalInvoices - syncedInvoices;
          var invoicesWithErrors = await query.CountAsync(x => x.ErrorType.HasValue, cancellationToken);
          var criticalErrors = await query.CountAsync(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired, cancellationToken);

          var lastSyncTime = await query
              .Where(x => x.LastSyncTime.HasValue)
              .MaxAsync(x => (DateTime?)x.LastSyncTime, cancellationToken);

          return new IssuedInvoiceSyncStats
          {
              TotalInvoices = totalInvoices,
              SyncedInvoices = syncedInvoices,
              UnsyncedInvoices = unsyncedInvoices,
              InvoicesWithErrors = invoicesWithErrors,
              CriticalErrors = criticalErrors,
              LastSyncTime = lastSyncTime
          };
      }
  ```

  with:

  ```csharp
      public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
      {
          var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

          var stats = await query
              .GroupBy(_ => 1)
              .Select(g => new
              {
                  Total = g.Count(),
                  Synced = g.Count(x => x.IsSynced),
                  WithErrors = g.Count(x => x.ErrorType.HasValue),
                  Critical = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired),
                  LastSyncTime = g.Where(x => x.LastSyncTime.HasValue).Max(x => (DateTime?)x.LastSyncTime)
              })
              .FirstOrDefaultAsync(cancellationToken);

          var totalInvoices = stats?.Total ?? 0;
          var syncedInvoices = stats?.Synced ?? 0;

          return new IssuedInvoiceSyncStats
          {
              TotalInvoices = totalInvoices,
              SyncedInvoices = syncedInvoices,
              UnsyncedInvoices = totalInvoices - syncedInvoices,
              InvoicesWithErrors = stats?.WithErrors ?? 0,
              CriticalErrors = stats?.Critical ?? 0,
              LastSyncTime = stats?.LastSyncTime
          };
      }
  ```

  No `using` changes needed — `Microsoft.EntityFrameworkCore` is already imported in this file.

- [ ] **Step 4: Run the test file again — expect all tests to PASS (green, confirms no regression from the rewrite)**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests"
  ```

  Expected: same tests as Step 2, all still passing, including `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats` (`TotalInvoices=4, SyncedInvoices=1, UnsyncedInvoices=3, InvoicesWithErrors=2, CriticalErrors=1`, plus the new `LastSyncTime` assertion) and the two new `LastSyncTime`-focused facts.

- [ ] **Step 5: Build and format**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep/backend
  dotnet build
  dotnet format --verify-no-changes
  ```

  If `dotnet format --verify-no-changes` reports changes, run `dotnet format` (no `--verify-no-changes`) to apply them, then re-run `dotnet build`.

- [ ] **Step 6: Commit**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep
  git add backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs
  git commit -m "Collapse GetSyncStatsAsync into a single grouped aggregate query

Replaces four CountAsync calls plus one MaxAsync call with a single
GroupBy(_ => 1).Select(...) projection, reducing GetSyncStatsAsync from
5 database round trips to 1. Adds LastSyncTime regression coverage to
the InMemory test suite (mixed sync times, and the no-LastSyncTime case)."
  ```

---

### task: add-sync-stats-sql-shape-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`

**Context:** The InMemory provider used by `IssuedInvoiceRepositoryTests.cs` cannot detect SQL round-trip count, so NFR-1's core claim ("1 round trip, down from 5") is otherwise unverified in CI. This task adds a Postgres/Testcontainers-backed test, modeled directly on `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs`, that captures every SQL command sent to the server via a `DbCommandInterceptor` and asserts exactly one is issued. Because `collapse-sync-stats-query` already landed the single-query rewrite, this new test passes immediately once written — the failing-state verification below is done as an explicit, non-destructive check (temporarily reintroducing the old five-query body, confirming the assertion fails with 5 commands recorded, then restoring the collapsed version) so the regression guard is proven to actually catch what it claims to catch, without ever leaving the working tree in a red, committed state.

- [ ] **Step 1: Create the SqlShapeTests file**

  Create `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Data.Common;
  using System.Threading;
  using System.Threading.Tasks;
  using Anela.Heblo.Domain.Features.Invoices;
  using Anela.Heblo.Persistence;
  using Anela.Heblo.Persistence.Invoices;
  using Anela.Heblo.Tests.Common;
  using FluentAssertions;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.EntityFrameworkCore.Diagnostics;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Npgsql;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Invoices;

  [Collection("PostgresIntegration")]
  [Trait("Category", "Integration")]
  public class IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests : IAsyncLifetime
  {
      private readonly PostgresSharedContainerFixture _fixture;
      private string _connectionString = null!;
      private readonly CapturingCommandInterceptor _interceptor = new();
      private ApplicationDbContext _context = null!;
      private IssuedInvoiceRepository _repository = null!;

      private static readonly DateTime FromDate = new(2026, 1, 1);
      private static readonly DateTime ToDate = new(2026, 1, 31);

      public IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests(PostgresSharedContainerFixture fixture)
      {
          _fixture = fixture;
      }

      public async Task InitializeAsync()
      {
          _connectionString = await _fixture.CreateDatabaseAsync("issuedinvoices");

          // Minimal schema — only the columns GetSyncStatsAsync touches, no FKs or audit
          // columns, matching how PhotobankRepositoryGetTagsSqlShapeTests scopes its CREATE TABLE.
          await using var conn = new NpgsqlConnection(_connectionString);
          await conn.OpenAsync();
          await using var cmd = conn.CreateCommand();
          cmd.CommandText = """
              CREATE TABLE public."IssuedInvoices" (
                  "Id"            varchar(64) NOT NULL PRIMARY KEY,
                  "InvoiceDate"   timestamp without time zone NOT NULL,
                  "IsSynced"      boolean NOT NULL,
                  "ErrorType"     integer NULL,
                  "LastSyncTime"  timestamp without time zone NULL
              );

              INSERT INTO public."IssuedInvoices" ("Id", "InvoiceDate", "IsSynced", "ErrorType", "LastSyncTime") VALUES
                  ('INV-SYNCED',   '2026-01-10', true,  NULL, '2026-01-10 08:00:00'),
                  ('INV-UNSYNCED', '2026-01-11', false, NULL, NULL),
                  ('INV-ERROR',    '2026-01-12', false, 0,    '2026-01-12 09:00:00'),
                  ('INV-PAIRED',   '2026-01-13', false, 1,    '2026-01-13 10:00:00'),
                  ('INV-OLD',      '2025-12-01', false, NULL, NULL);
              """;
          await cmd.ExecuteNonQueryAsync();

          var options = new DbContextOptionsBuilder<ApplicationDbContext>()
              .UseNpgsql(_connectionString)
              .AddInterceptors(_interceptor)
              .Options;

          _context = new ApplicationDbContext(options);
          _repository = new IssuedInvoiceRepository(_context, Mock.Of<ILogger<IssuedInvoiceRepository>>());
      }

      public async Task DisposeAsync()
      {
          await _context.DisposeAsync();
      }

      [Fact]
      public async Task GetSyncStatsAsync_EmitsExactlyOneSqlCommand()
      {
          _interceptor.Reset();

          await _repository.GetSyncStatsAsync(FromDate, ToDate, CancellationToken.None);

          _interceptor.Commands.Should().HaveCount(1);
      }

      [Fact]
      public async Task GetSyncStatsAsync_ReturnsCorrectStatsFromRealDatabase()
      {
          _interceptor.Reset();

          var stats = await _repository.GetSyncStatsAsync(FromDate, ToDate, CancellationToken.None);

          stats.TotalInvoices.Should().Be(4, "INV-OLD is outside the [2026-01-01, 2026-01-31] range");
          stats.SyncedInvoices.Should().Be(1, "only INV-SYNCED has IsSynced = true");
          stats.UnsyncedInvoices.Should().Be(3);
          stats.InvoicesWithErrors.Should().Be(2, "INV-ERROR and INV-PAIRED both have a non-null ErrorType");
          stats.CriticalErrors.Should().Be(1, "INV-PAIRED's ErrorType is InvoicePaired, which is excluded from CriticalErrors");
          stats.LastSyncTime.Should().Be(new DateTime(2026, 1, 13, 10, 0, 0), "INV-PAIRED has the latest LastSyncTime among in-range rows that have one");
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

- [ ] **Step 2: Run the new test class — expect both facts to PASS**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests"
  ```

  Requires Docker (or Podman) available, same as the existing `PhotobankRepositoryGetTagsSqlShapeTests`/`PurchaseOrderRepositoryHistorySqlShapeTests` suites. Expected: both facts pass, since `collapse-sync-stats-query` already landed the single-query implementation — `GetSyncStatsAsync_EmitsExactlyOneSqlCommand` passes because the current implementation issues one command, and `GetSyncStatsAsync_ReturnsCorrectStatsFromRealDatabase` passes because the seeded fixture matches the asserted values.

- [ ] **Step 3: Prove the round-trip test actually catches a regression (non-destructive, not committed)**

  Temporarily verify `GetSyncStatsAsync_EmitsExactlyOneSqlCommand` fails against the pre-refactor five-query implementation, to confirm the new test is a real regression guard and not a tautology:

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep
  git show HEAD~1:backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs > /tmp/claude-0/-home-user-Anela-Heblo/515b7581-b8c6-56cb-ab45-48c48e54018f/scratchpad/IssuedInvoiceRepository.pre-refactor.cs
  cp backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs /tmp/claude-0/-home-user-Anela-Heblo/515b7581-b8c6-56cb-ab45-48c48e54018f/scratchpad/IssuedInvoiceRepository.current.cs
  cp /tmp/claude-0/-home-user-Anela-Heblo/515b7581-b8c6-56cb-ab45-48c48e54018f/scratchpad/IssuedInvoiceRepository.pre-refactor.cs backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.GetSyncStatsAsync_EmitsExactlyOneSqlCommand"
  ```

  Expected: this run FAILS — `interceptor.Commands` has 5 entries, not 1 — confirming the test genuinely detects the N+1-style round-trip regression described in the spec's Background section. Then restore the collapsed implementation before continuing (do not commit the reverted file):

  ```bash
  cp /tmp/claude-0/-home-user-Anela-Heblo/515b7581-b8c6-56cb-ab45-48c48e54018f/scratchpad/IssuedInvoiceRepository.current.cs backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests"
  ```

  Expected: both facts pass again (green), confirming the working tree is back to the `collapse-sync-stats-query` state and nothing was left modified.

  If `git show HEAD~1:...` doesn't resolve (e.g., `collapse-sync-stats-query`'s commit isn't exactly one commit back due to intervening commits), use `git log --oneline -- backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` to find the correct pre-refactor commit SHA and substitute it for `HEAD~1`.

- [ ] **Step 4: Build and format**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep/backend
  dotnet build
  dotnet format --verify-no-changes
  ```

  If `dotnet format --verify-no-changes` reports changes, run `dotnet format` (no `--verify-no-changes`) to apply them, then re-run `dotnet build`.

- [ ] **Step 5: Commit**

  ```bash
  cd /home/user/worktrees/feature-3564-Arch-Review-Invoices-Getsyncstatsasync-Fires-5-Sep
  git add backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs
  git commit -m "Add Postgres SQL-shape test asserting GetSyncStatsAsync issues exactly one command

Follows the existing PhotobankRepositoryGetTagsSqlShapeTests convention
(PostgresSharedContainerFixture + a local DbCommandInterceptor) so the
1-round-trip claim from the GetSyncStatsAsync collapse is enforced in CI
against a real relational provider, not just the InMemory test suite."
  ```
