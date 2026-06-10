using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.Infrastructure.Jobs;

public sealed class FillTrackingNumbersJob : IRecurringJob
{
    private const int DaysBack = 3;

    private readonly IPackageRepository _repo;
    private readonly IShipmentClient _client;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<FillTrackingNumbersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "fill-tracking-numbers",
        DisplayName = "Fill Tracking Numbers",
        Description = "Backfills TrackingNumber for recently-packed shipments where Shoptet had not yet generated the carrier label at scan time.",
        CronExpression = "*/10 * * * *",
        DefaultIsEnabled = true,
    };

    public FillTrackingNumbersJob(
        IPackageRepository repo,
        IShipmentClient client,
        IRecurringJobStatusChecker statusChecker,
        ILogger<FillTrackingNumbersJob> logger)
    {
        _repo = repo;
        _client = client;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var packages = await _repo.GetWithNullTrackingNumberAsync(DaysBack, cancellationToken);
        if (packages.Count == 0)
            return;

        _logger.LogInformation(
            "FillTrackingNumbers: found {Count} package(s) with null TrackingNumber in the last {Days} days.",
            packages.Count, DaysBack);

        var byOrder = packages.GroupBy(p => p.OrderCode);
        var updated = 0;

        foreach (var group in byOrder)
        {
            var orderCode = group.Key;
            IReadOnlyList<ShipmentLabel> labels;

            try
            {
                labels = await _client.GetLabelsByOrderCodeAsync(orderCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FillTrackingNumbers: failed to fetch labels for order {OrderCode}. Will retry next run.",
                    orderCode);
                continue;
            }

            var labelByPackageName = labels
                .Where(l => l.TrackingNumber is not null)
                .ToDictionary(l => l.PackageName, l => l.TrackingNumber!);

            foreach (var package in group)
            {
                if (!labelByPackageName.TryGetValue(package.PackageNumber, out var trackingNumber))
                    continue;

                await _repo.SetTrackingNumberAsync(package.Id, trackingNumber, cancellationToken);
                updated++;

                _logger.LogInformation(
                    "FillTrackingNumbers: set TrackingNumber={TrackingNumber} on Package {Id} (order {OrderCode}).",
                    trackingNumber, package.Id, orderCode);
            }
        }

        _logger.LogInformation("FillTrackingNumbers: updated {Updated}/{Total} package(s).", updated, packages.Count);
    }
}
