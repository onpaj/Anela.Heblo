using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

/// <summary>
/// Dashboard tile showing count of gift packages with critical stock severity.
/// </summary>
public class CriticalGiftPackagesTile : ITile
{
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;

    public CriticalGiftPackagesTile(
        IMediator mediator,
        TimeProvider timeProvider)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
    }

    public string Title => "Kritické balíčky";
    public string Description => "Počet balíčků v kritickém stavu";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetAvailableGiftPackagesRequest
            {
                SalesCoefficient = 1.0m
            };

            var response = await _mediator.Send(request, cancellationToken);

            if (!response.Success)
            {
                return new
                {
                    status = "error",
                    error = "Failed to load gift packages data"
                };
            }

            var criticalCount = response.GiftPackages
                .Count(package => package.Severity == StockSeverity.Critical);

            return new
            {
                status = "success",
                data = new
                {
                    count = criticalCount,
                    date = _timeProvider.GetUtcNow().DateTime
                },
                metadata = new
                {
                    lastUpdated = _timeProvider.GetUtcNow().DateTime,
                    source = "GiftPackageManufacture"
                },
                drillDown = new
                {
                    filters = new { severity = "Critical" },
                    enabled = true,
                    tooltip = "Zobrazit všechny kritické balíčky"
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