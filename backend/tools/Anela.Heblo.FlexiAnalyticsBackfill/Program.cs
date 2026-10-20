using System.Globalization;
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.DI;

// One-off historical load of flexi_raw.ledger_entry. See docs/architecture/metabase.md for why
// this is a manual tool rather than something the nightly job discovers on its own: the first run
// covers ~680k rows against a 1-vCore Postgres shared with production, and it is run deliberately,
// off-hours, by a person watching it.
//
//   AnalyticsDatabase__ConnectionString=... \
//   FlexiBeeSettings__Server=... FlexiBeeSettings__Company=... \
//   FlexiBeeSettings__Login=... FlexiBeeSettings__Password=... \
//   dotnet run --project backend/tools/Anela.Heblo.FlexiAnalyticsBackfill -- 2020-01-01 2026-09-30
//
// Pass --incremental instead of a date range to run the nightly job's work by hand.

// --incremental runs exactly what the nightly Hangfire job runs, by hand. It is how you verify the
// job end to end (or catch up after an outage) without waiting for 03:00 or redeploying.
var incremental = args is ["--incremental"];
DateOnly from = default, to = default;

if (!incremental
    && (args.Length != 2
        || !DateOnly.TryParseExact(args[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from)
        || !DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to)))
{
    Console.Error.WriteLine("usage: FlexiAnalyticsBackfill <from yyyy-MM-dd> <to yyyy-MM-dd>");
    Console.Error.WriteLine("       FlexiAnalyticsBackfill --incremental");
    return 2;
}

if (!incremental && from > to)
{
    Console.Error.WriteLine($"'from' ({from}) is after 'to' ({to}).");
    return 2;
}

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

var connectionString = configuration["AnalyticsDatabase:ConnectionString"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("AnalyticsDatabase__ConnectionString is not set.");
    return 2;
}

var services = new ServiceCollection();
services.AddLogging(b => b
    .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ")
    .SetMinimumLevel(LogLevel.Information)
    // EF logs every INSERT at Information; over a 680k-row load that is the only thing you see.
    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
services.AddHttpClient();
services.AddMemoryCache();
services.AddFlexiBee(configuration);

services.AddSingleton<System.Diagnostics.Metrics.IMeterFactory, DefaultMeterFactory>();
services.AddSingleton<DbResilienceMetrics>();
services.AddSingleton<NpgsqlConnectionInterceptor>();
services.AddAnalyticsPersistenceServices(
    connectionString,
    configuration.GetValue<int?>("AnalyticsDatabase:MaxPoolSize") ?? 4);

services.Configure<FlexiAnalyticsSyncOptions>(
    configuration.GetSection(FlexiAnalyticsSyncOptions.ConfigurationKey));
services.AddScoped<ISyncWatermarkRepository, SyncWatermarkRepository>();
services.AddScoped<LedgerSyncService>();
services.AddScoped<ILedgerBackfillService>(sp => sp.GetRequiredService<LedgerSyncService>());
// The three dimension tables are full-refresh and small, and are loaded here so a backfilled
// database is complete rather than waiting for the first nightly run. Note the read views do NOT
// join them: v_ad_spend_monthly matches on ledger_entry's own denormalised contact label, for the
// reason documented in flexi_raw_read_views.sql. They are dimensions for ad-hoc Metabase use.
services.AddScoped<DepartmentSyncService>();
services.AddScoped<AccountingTemplateSyncService>();
services.AddScoped<ContactSyncService>();
services.AddScoped<IEntitySyncService>(sp => sp.GetRequiredService<LedgerSyncService>());
services.AddScoped<IEntitySyncService>(sp => sp.GetRequiredService<DepartmentSyncService>());
services.AddScoped<IEntitySyncService>(sp => sp.GetRequiredService<AccountingTemplateSyncService>());
services.AddScoped<IEntitySyncService>(sp => sp.GetRequiredService<ContactSyncService>());
services.AddScoped<IFlexiAnalyticsSyncService, FlexiAnalyticsSyncService>();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogWarning("Cancellation requested — finishing the current page, then stopping.");
    cts.Cancel();
};

var startedAt = DateTimeOffset.UtcNow;

if (incremental)
{
    FlexiAnalyticsSyncReport report;
    try
    {
        report = await scope.ServiceProvider
            .GetRequiredService<IFlexiAnalyticsSyncService>()
            .SyncAllAsync(cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        logger.LogWarning("Incremental sync stopped on request after {Elapsed}.", DateTimeOffset.UtcNow - startedAt);
        return 130;
    }

    logger.LogInformation(
        "Incremental sync {Outcome}: fetched={Fetched} upserted={Upserted} failedServices={Failed} elapsed={Elapsed}",
        report.IsFullSuccess ? "completed" : "FAILED",
        report.TotalFetched, report.TotalUpserted, report.FailedServices,
        DateTimeOffset.UtcNow - startedAt);

    return report.IsFullSuccess ? 0 : 1;
}

logger.LogInformation("Refreshing the flexi_raw dimension tables.");
foreach (var dimension in new IEntitySyncService[]
         {
             scope.ServiceProvider.GetRequiredService<DepartmentSyncService>(),
             scope.ServiceProvider.GetRequiredService<AccountingTemplateSyncService>(),
             scope.ServiceProvider.GetRequiredService<ContactSyncService>(),
         })
{
    var dimensionResult = await dimension.SyncAsync(cts.Token);
    logger.LogInformation(
        "{Dimension}: fetched={Fetched} upserted={Upserted} success={Success}",
        dimension.GetType().Name, dimensionResult.RowsFetched, dimensionResult.RowsUpserted, dimensionResult.IsSuccess);

    // A dimension that silently refreshed to nothing is the exact failure this PR exists to fix,
    // so it ends the run rather than being logged and stepped over.
    if (!dimensionResult.IsSuccess)
    {
        logger.LogError("{Dimension} failed; aborting before the ledger backfill.", dimension.GetType().Name);
        return 1;
    }
}

logger.LogInformation("Backfilling flexi_raw.ledger_entry from {From} to {To}.", from, to);
SyncResult result;
try
{
    result = await scope.ServiceProvider
        .GetRequiredService<ILedgerBackfillService>()
        .BackfillAsync(from, to, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // Progress is persisted per month window, so re-running the same range resumes rather than
    // restarting. 130 is the conventional "terminated by Ctrl+C" exit code.
    logger.LogWarning("Backfill stopped on request after {Elapsed}. Re-run the same range to resume.",
        DateTimeOffset.UtcNow - startedAt);
    return 130;
}
var elapsed = DateTimeOffset.UtcNow - startedAt;

logger.LogInformation(
    "Backfill {Outcome}: fetched={Fetched} upserted={Upserted} elapsed={Elapsed}",
    result.IsSuccess ? "completed" : "FAILED", result.RowsFetched, result.RowsUpserted, elapsed);

return result.IsSuccess ? 0 : 1;

/// <summary>
/// DbResilienceMetrics needs an IMeterFactory; the generic host would normally supply one, and this
/// tool does not build a host.
/// </summary>
internal sealed class DefaultMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
        new(options.Name, options.Version);

    public void Dispose() { }
}
