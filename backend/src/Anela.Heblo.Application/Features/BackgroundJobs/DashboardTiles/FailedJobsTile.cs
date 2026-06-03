using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;

[TileId("failedjobs")]
public sealed class FailedJobsTile : ITile
{
    private const string DrillDownRouteKey = "hangfireFailedJobs";
    private const string DrillDownTooltip = "Open Hangfire failed jobs";

    private readonly IFailedJobCounter _failedJobCounter;
    private readonly ILogger<FailedJobsTile> _logger;

    public string Title => "Failed background jobs";
    public string Description => "Hangfire jobs in the failed queue";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public string[] RequiredPermissions => Array.Empty<string>();

    public FailedJobsTile(IFailedJobCounter failedJobCounter, ILogger<FailedJobsTile> logger)
    {
        _failedJobCounter = failedJobCounter;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var failedCount = await _failedJobCounter.GetFailedCountAsync(cancellationToken);

            return new
            {
                status = "success",
                data = new { count = failedCount },
                metadata = new { lastUpdated = DateTime.UtcNow, source = "Hangfire" },
                drillDown = new
                {
                    routeKey = DrillDownRouteKey,
                    enabled = true,
                    tooltip = DrillDownTooltip
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Hangfire failed job count");

            return new
            {
                status = "error",
                data = (object?)null,
                error = "Failed to retrieve job count. See server logs.",
                drillDown = new
                {
                    routeKey = DrillDownRouteKey,
                    enabled = true,
                    tooltip = DrillDownTooltip
                }
            };
        }
    }
}
