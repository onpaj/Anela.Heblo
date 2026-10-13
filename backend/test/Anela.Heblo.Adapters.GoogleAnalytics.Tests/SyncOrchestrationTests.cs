using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// Ga4SyncService runs the six tables in turn. Its whole job is deciding what one table's failure
/// means for the other five, and what a cancelled run does.
/// </summary>
public class SyncOrchestrationTests
{
    private sealed class StubEntitySync : IGa4EntitySyncService
    {
        private readonly Func<CancellationToken, Ga4SyncResult> _run;

        public StubEntitySync(string entityName, Func<CancellationToken, Ga4SyncResult> run)
        {
            EntityName = entityName;
            _run = run;
        }

        public string EntityName { get; }
        public int Invocations { get; private set; }

        public Task<Ga4SyncResult> SyncAsync(CancellationToken ct = default)
        {
            Invocations++;
            return Task.FromResult(_run(ct));
        }
    }

    private static StubEntitySync Succeeding(string name, int fetched, int upserted) =>
        new(name, _ => new Ga4SyncResult(name, fetched, upserted, true));

    private static Ga4SyncService ServiceOver(params IGa4EntitySyncService[] services) =>
        new(services, NullLogger<Ga4SyncService>.Instance);

    [Fact]
    public async Task one_failing_table_does_not_stop_the_others_from_syncing()
    {
        // Each table owns its own watermark, so there is no reason a GA4 failure on one should
        // cost the other five a night's data.
        var first = Succeeding("traffic_monthly", 10, 10);
        var broken = new StubEntitySync("traffic_daily", _ => throw new HttpRequestException("GA4 said no"));
        var last = Succeeding("page_daily", 5, 5);

        var report = await ServiceOver(first, broken, last).SyncAllAsync();

        last.Invocations.Should().Be(1, "the table after the failure still runs");
        report.FailedServices.Should().Be(1);
        report.IsFullSuccess.Should().BeFalse();
        report.TotalFetched.Should().Be(15, "only the tables that succeeded contribute");
        report.TotalUpserted.Should().Be(15);
    }

    [Fact]
    public async Task counts_a_table_that_reported_failure_without_throwing()
    {
        var reportedFailure = new StubEntitySync("traffic_daily", _ => new Ga4SyncResult("traffic_daily", 99, 99, false));

        var report = await ServiceOver(Succeeding("traffic_monthly", 10, 10), reportedFailure).SyncAllAsync();

        report.FailedServices.Should().Be(1);
        report.IsFullSuccess.Should().BeFalse();
        report.TotalFetched.Should().Be(10, "a failed table's partial counts are not banked");
    }

    [Fact]
    public async Task stops_the_run_on_cancellation_instead_of_marching_every_table_into_a_dead_token()
    {
        // Ga4SyncJob cancels after RequestTimeoutSeconds. Carrying on produced a stack trace per
        // remaining table instead of one clean stop.
        using var cts = new CancellationTokenSource();

        var first = new StubEntitySync("traffic_monthly", _ =>
        {
            cts.Cancel();
            return new Ga4SyncResult("traffic_monthly", 10, 10, true);
        });
        var second = Succeeding("traffic_daily", 5, 5);
        var third = Succeeding("page_daily", 5, 5);

        var report = await ServiceOver(first, second, third).SyncAllAsync(cts.Token);

        second.Invocations.Should().Be(0);
        third.Invocations.Should().Be(0);
        report.TotalFetched.Should().Be(10, "the table that did finish is still banked");
    }

    [Fact]
    public async Task reports_full_success_only_when_every_table_succeeded()
    {
        var report = await ServiceOver(
            Succeeding("traffic_monthly", 1, 1),
            Succeeding("traffic_daily", 2, 2)).SyncAllAsync();

        report.IsFullSuccess.Should().BeTrue();
        report.FailedServices.Should().Be(0);
        report.TotalFetched.Should().Be(3);
    }
}
