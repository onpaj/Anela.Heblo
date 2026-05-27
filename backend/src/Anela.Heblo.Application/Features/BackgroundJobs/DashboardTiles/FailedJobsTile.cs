using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;

public class FailedJobsTile : ITile
{
    private readonly JobStorage _jobStorage;
    private readonly ILogger<FailedJobsTile> _logger;

    public string Title => "Failed background jobs";
    public string Description => "Hangfire jobs in the failed queue";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public FailedJobsTile(JobStorage jobStorage, ILogger<FailedJobsTile> logger)
    {
        _jobStorage = jobStorage;
        _logger = logger;
    }

    public Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var failedCount = _jobStorage.GetMonitoringApi().FailedCount();

            return Task.FromResult<object>(new
            {
                status = "success",
                data = new { count = failedCount },
                metadata = new { lastUpdated = DateTime.UtcNow, source = "Hangfire" },
                drillDown = new
                {
                    url = "/hangfire/jobs/failed",
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Hangfire failed job count");

            return Task.FromResult<object>(new
            {
                status = "error",
                data = (object?)null,
                error = ex.Message,
                drillDown = new
                {
                    url = "/hangfire/jobs/failed",
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            });
        }
    }
}
