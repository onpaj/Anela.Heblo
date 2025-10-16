using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.DashboardTiles;

/// <summary>
/// Dashboard tile showing count of products with stock efficiency below 20%.
/// </summary>
public class LowStockEfficiencyTile : ITile
{
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;

    public LowStockEfficiencyTile(
        IMediator mediator,
        TimeProvider timeProvider)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
    }

    public string Title => "Materiál NS < 20%";
    public string Description => "Počet produktů s NS% menší než 20%";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Purchase;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetPurchaseStockAnalysisRequest
            {
                StockStatus = StockStatusFilter.All,
                PageSize = int.MaxValue
            };

            var response = await _mediator.Send(request, cancellationToken);

            if (!response.Success)
            {
                return new
                {
                    status = "error",
                    error = "Failed to load stock analysis data"
                };
            }

            var lowEfficiencyCount = response.Items
                .Count(item => item.StockEfficiencyPercentage < 20);

            return new
            {
                status = "success",
                data = new
                {
                    count = lowEfficiencyCount,
                    date = _timeProvider.GetUtcNow().DateTime
                },
                metadata = new
                {
                    lastUpdated = _timeProvider.GetUtcNow().DateTime,
                    source = "PurchaseStockAnalysis"
                },
                drillDown = new
                {
                    filters = new { filter = "kriticke" },
                    enabled = true,
                    tooltip = "Zobrazit všechny materiály s kritickou zásobou"
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = ex.Message
            };
        }
    }
}