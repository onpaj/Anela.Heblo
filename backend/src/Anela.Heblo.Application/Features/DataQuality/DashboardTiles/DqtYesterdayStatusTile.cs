using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("dqtyesterdaystatus")]
public class DqtYesterdayStatusTile : ITile
{
    private const string DrillDownRouteKey = "dataQuality";

    private readonly IDqtRunRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DqtYesterdayStatusTile> _logger;

    public string Title => "DQT včera";
    public string Description => "Stav včerejšího DQT testu faktur";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public string[] RequiredPermissions => Array.Empty<string>();

    public DqtYesterdayStatusTile(
        IDqtRunRepository repository,
        TimeProvider timeProvider,
        ILogger<DqtYesterdayStatusTile> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var yesterday = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1);

        try
        {
            var run = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison,
                yesterday,
                cancellationToken);

            if (run is null)
            {
                return new
                {
                    status = "no_data",
                    data = (object?)null,
                    drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                };
            }

            var statusStr = run.Status switch
            {
                DqtRunStatus.Failed => "error",
                DqtRunStatus.Running => "warning",
                DqtRunStatus.Completed when run.TotalMismatches > 0 => "warning",
                DqtRunStatus.Completed => "success",
                _ => "error"
            };

            return new
            {
                status = statusStr,
                data = new
                {
                    runId = run.Id,
                    runStatus = run.Status.ToString(),
                    dateFrom = run.DateFrom,
                    dateTo = run.DateTo,
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches
                },
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load yesterday DQT status for {TestType} on {TargetDate}",
                DqtTestType.IssuedInvoiceComparison,
                yesterday);

            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
    }
}
