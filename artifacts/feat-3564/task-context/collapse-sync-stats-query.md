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
