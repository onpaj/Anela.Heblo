using System.Globalization;
using Anela.Heblo.Adapters.GoogleAnalytics;
using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// Records every request it is asked for and replays canned rows, so a test can assert on the
/// window the sync asked GA4 about as well as on what it wrote.
/// </summary>
internal sealed class FakeGa4ReportClient : IGa4ReportClient
{
    private readonly Func<Ga4ReportRequest, IReadOnlyList<Ga4Row>> _rows;

    public FakeGa4ReportClient(Func<Ga4ReportRequest, IReadOnlyList<Ga4Row>> rows)
    {
        _rows = rows;
    }

    public List<Ga4ReportRequest> Requests { get; } = new();

    public Task<Ga4ReportResult> RunReportAsync(Ga4ReportRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        // GA4 only ever returns rows inside the window it was asked about. Canned rows are
        // filtered the same way, so a test that spans several chunks does not see the same day
        // handed back once per chunk.
        var rows = _rows(request).Where(row => InWindow(row, request)).ToList();
        return Task.FromResult(new Ga4ReportResult(rows, null, false, false));
    }

    private static bool InWindow(Ga4Row row, Ga4ReportRequest request)
    {
        var value = row.DimensionValues[0];
        if (DateOnly.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out var date))
            return date >= request.StartDate && date <= request.EndDate;

        if (DateOnly.TryParseExact(value, "yyyyMM", null, DateTimeStyles.None, out var month))
            return month <= request.EndDate && month.AddMonths(1).AddDays(-1) >= request.StartDate;

        return true;
    }
}

internal static class Ga4TestHarness
{
    /// <summary>
    /// A database name a test can open a SECOND context over. Asserting through the same context
    /// the service used proves nothing: Ga4SyncWatermarkRepository.GetOrCreateAsync goes through
    /// FindAsync, which returns the change tracker's own instance without touching the store, so
    /// the assertion reads back the very object the service mutated in memory — and still passes
    /// with persistence removed entirely.
    /// </summary>
    public static string NewDatabaseName() => $"ga4-{Guid.NewGuid()}";

    public static Ga4DbContext NewDbContext(
        string? databaseName = null,
        IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<Ga4DbContext>()
            .UseInMemoryDatabase(databaseName ?? NewDatabaseName());

        if (interceptor != null)
            builder.AddInterceptors(interceptor);

        return new Ga4DbContext(builder.Options);
    }

    public static Ga4SyncOptions Options(Action<Ga4SyncOptions>? configure = null)
    {
        var options = new Ga4SyncOptions
        {
            BackfillFrom = "2026-01-01",
            TrailingReprocessDays = 3,
            ChunkDays = 31,
            ThrottleMilliseconds = 0,
            TimeZone = "UTC",
        };
        configure?.Invoke(options);
        return options;
    }

    public static TrafficSyncService TrafficSync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<TrafficSyncService>.Instance);

    public static Ga4Row TrafficRow(string date, string channel, long sessions) =>
        new([date, channel], [sessions.ToString(), "0", "0", "0", "0", "0"]);

    public static LandingPageSyncService LandingPageSync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<LandingPageSyncService>.Instance);

    public static PageSyncService PageSync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<PageSyncService>.Instance);

    public static TrafficMonthlySyncService TrafficMonthlySync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<TrafficMonthlySyncService>.Instance);

    /// <summary>Reads a sync_state row through a context of its own, never the one under test.</summary>
    public static async Task<SyncState> StoredStateAsync(string databaseName, string entityName)
    {
        await using var verify = NewDbContext(databaseName);
        return await verify.SyncStates.SingleAsync(x => x.EntityName == entityName);
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
