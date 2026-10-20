using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// All six sync services and the watermark repository share one scoped Ga4DbContext, so a failed
/// SaveChangesAsync leaves its entities tracked and the NEXT save commits them — in a run that
/// reports itself FAILED. The InMemory provider practically never fails a save on its own, so the
/// failure has to be injected to exercise the path at all.
/// </summary>
public class FailedWriteIsolationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

    /// <summary>Fails the first save that tries to write traffic rows, then behaves normally.</summary>
    private sealed class FailOnFirstTrafficWrite : SaveChangesInterceptor
    {
        private bool _fired;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var writesTraffic = eventData.Context!.ChangeTracker
                .Entries()
                .Any(e => e.Entity is Persistence.Ga4.Entities.TrafficDaily && e.State == EntityState.Added);

            if (writesTraffic && !_fired)
            {
                _fired = true;
                throw new DbUpdateException("transient failure against the shared Postgres server");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task a_run_that_reports_failure_does_not_commit_the_rows_whose_write_failed()
    {
        // Arrange
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database, new FailOnFirstTrafficWrite());
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");
        var client = new FakeGa4ReportClient(_ => new[] { Ga4TestHarness.TrafficRow("20260309", "Organic Search", 100) });

        // Act
        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert — the run failed, and the failed write stayed failed. Without discarding the
        // change tracker, the bookkeeping save that records FAILED would replay the still-tracked
        // row and commit it, so the table would hold data from a run reported as a failure.
        result.IsSuccess.Should().BeFalse();

        await using var verify = Ga4TestHarness.NewDbContext(database);
        verify.TrafficDaily.Should().BeEmpty("a FAILED run must not leave rows behind");

        var state = await Ga4TestHarness.StoredStateAsync(database, "traffic_daily");
        state.LastRunStatus.Should().Be("FAILED", "the failure is still recorded where an operator looks");
        state.LastErrorMessage.Should().Contain("transient failure");
        state.WatermarkDate.Should().BeNull("the window was not stored, so it must be retried");
    }
}
