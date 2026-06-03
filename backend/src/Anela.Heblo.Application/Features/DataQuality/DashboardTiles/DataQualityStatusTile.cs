using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("dataqualitystatus")]
public class DataQualityStatusTile : ITile
{
    private const string DrillDownRouteKey = "dataQuality";

    private readonly IDqtRunRepository _repository;
    private readonly ILogger<DataQualityStatusTile> _logger;

    public string Title => "Kvalita dat";
    public string Description => "Stav posledního DQT testu faktur";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public DataQualityStatusTile(
        IDqtRunRepository repository,
        ILogger<DataQualityStatusTile> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await _repository.GetLatestByTestTypeAsync(DqtTestType.IssuedInvoiceComparison, cancellationToken);

            if (run is null)
            {
                return new
                {
                    status = "no_data",
                    data = (object?)null,
                    drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                };
            }

            var statusStr = run.Status == DqtRunStatus.Failed
                ? "error"
                : run.TotalMismatches > 0
                    ? "warning"
                    : "success";

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
                "Failed to load DataQuality status tile for {TestType}",
                DqtTestType.IssuedInvoiceComparison);

            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
    }
}
