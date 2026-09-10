using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("pricecomparisonstatus")]
public class PriceComparisonStatusTile : ITile
{
    private const string DrillDownRouteKey = "productPricing";

    private readonly IDqtRunRepository _repository;
    private readonly ILogger<PriceComparisonStatusTile> _logger;

    public string Title => "Kontrola cen";
    public string Description => "Rozdíly cen mezi Shoptetem a Flexi";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public string[] RequiredPermissions => Array.Empty<string>();

    public PriceComparisonStatusTile(
        IDqtRunRepository repository,
        ILogger<PriceComparisonStatusTile> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        var drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true };

        try
        {
            var run = await _repository.GetLatestByTestTypeAsync(
                DqtTestType.PriceComparison, cancellationToken);

            if (run is null)
            {
                return new { status = "no_data", data = (object?)null, drillDown };
            }

            var status = run.Status switch
            {
                DqtRunStatus.Failed => "error",
                DqtRunStatus.Running => "warning",
                DqtRunStatus.Completed when run.TotalMismatches > 0 => "warning",
                DqtRunStatus.Completed => "success",
                _ => "error"
            };

            return new
            {
                status,
                data = new
                {
                    runId = run.Id,
                    runStatus = run.Status.ToString(),
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches,
                    completedAt = run.CompletedAt,
                },
                drillDown
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the price comparison tile");
            return new { status = "error", data = (object?)null, drillDown };
        }
    }
}
