using System.Globalization;
using Anela.Heblo.AdsBackfill;
using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// One-off historical load of ads.ad_daily_facts and ads.ad_search_term_daily (docs/processes/sync-ads-daily.md,
// "Backfill"). The daily job only re-pulls yesterday plus 14 days; this fills everything before that.
// It reuses the daily job's upserters, so re-running any range is safe and resumes nothing it has not
// already finished. Run deliberately, off-hours and outside 05:30–06:15 (the daily job writes the same
// days), against a 1-vCore Postgres shared with production.
//
//   set -a; . ./ads-backfill.env; set +a    # a chmod-600 file, so secrets stay out of shell history:
//   #   AdsDatabase__ConnectionString, GoogleAds__CustomerId, GoogleAds__OAuth2ClientId,
//   #   GoogleAds__OAuth2ClientSecret, GoogleAds__OAuth2RefreshToken
//   dotnet run --project backend/tools/Anela.Heblo.AdsBackfill -- 2025-01-01 2026-09-30
//
// Google is read one calendar month per call (about 10 API operations a month with search terms),
// well inside the daily quota. Change history is not backfilled: Google keeps only 30 days of it.

const string DateFormat = "yyyy-MM-dd";

if (args.Length != 2
    || !DateOnly.TryParseExact(args[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
    || !DateOnly.TryParseExact(args[1], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
{
    Console.Error.WriteLine("usage: AdsBackfill <from yyyy-MM-dd> <to yyyy-MM-dd>   (both inclusive, 'to' before today)");
    return 2;
}

if (from > to)
{
    Console.Error.WriteLine($"'from' ({from:yyyy-MM-dd}) is after 'to' ({to:yyyy-MM-dd}).");
    return 2;
}

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

var services = new ServiceCollection();
services.AddAdsBackfill(configuration);

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdsBackfill");

if (scope.ServiceProvider.GetService<AdsDbContext>() is null)
{
    Console.Error.WriteLine($"{MarketingAdsModule.ConnectionStringKey} is not set or not usable (env AdsDatabase__ConnectionString).");
    return 2;
}

if (!scope.ServiceProvider.GetServices<IAdPlatformReadSource>().Any())
{
    Console.Error.WriteLine("No ad platform is configured. Set GoogleAds__CustomerId, GoogleAds__OAuth2ClientId, "
                            + "GoogleAds__OAuth2ClientSecret and GoogleAds__OAuth2RefreshToken.");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // A second Ctrl+C kills the process: every day is its own transaction, so nothing is left half-written.
    if (cts.IsCancellationRequested)
        return;
    e.Cancel = true;
    logger.LogWarning("Cancellation requested — stopping (Ctrl+C again to kill). Finished days are saved; re-run from the last logged month.");
    cts.Cancel();
};

var startedAt = DateTimeOffset.UtcNow;
logger.LogInformation("Backfilling ads facts and search terms from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}.", from, to);

AdsSyncReport report;
try
{
    report = await scope.ServiceProvider.GetRequiredService<AdsBackfillService>().RunAsync(from, to, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // 130 is the conventional "terminated by Ctrl+C" exit code.
    logger.LogWarning("Backfill stopped on request after {Elapsed}.", DateTimeOffset.UtcNow - startedAt);
    return 130;
}

foreach (var error in report.Errors)
    logger.LogError("{Error}", error);

logger.LogInformation("Backfill {Outcome}: rows={Rows} errors={Errors} elapsed={Elapsed}",
    report.HasFailures ? "FAILED" : "completed", report.RowsWritten, report.Errors.Count, DateTimeOffset.UtcNow - startedAt);

return report.HasFailures ? 1 : 0;
