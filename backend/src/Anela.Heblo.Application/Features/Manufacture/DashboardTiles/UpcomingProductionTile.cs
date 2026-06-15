using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public abstract class UpcomingProductionTile : ITile
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;

    // Self-describing metadata
    public abstract string Title { get; }
    public abstract string Description { get; }
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => true;
    public bool AutoShow => true; // Feature tile - user choice
    public string[] RequiredPermissions => Array.Empty<string>();

    protected abstract DateOnly ReferenceDate { get; set; }

    protected UpcomingProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        var orders = await _repository.GetOrdersForDateRangeAsync(ReferenceDate, ReferenceDate, cancellationToken);
        orders = orders.Where(w => w.State != ManufactureOrderState.Cancelled).ToList();

        return new
        {
            status = "success",
            data = new
            {
                TotalOrders = orders.Count(),
                Products = orders.Take(5).Select(o =>
                        new
                        {
                            ProductName = o.SemiProduct?.ProductName ?? "N/A",
                            SemiProductCompleted = o.State is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed,
                            ProductsCompleted = o.State == ManufactureOrderState.Completed,
                            o.ResponsiblePerson,
                            ActualQuantity = o.SemiProduct?.ActualQuantity ?? 0
                        }
                ).ToArray()
            },
            metadata = new
            {
                lastUpdated = _timeProvider.GetUtcNow().UtcDateTime,
                source = "ManufactureOrderRepository"
            },
            drillDown = new
            {
                filters = GenerateDrillDownFilters(),
                enabled = true,
                tooltip = $"Zobrazit všechny výrobní příkazy na {ReferenceDate:dd.MM.yyyy}"
            }
        };
    }

    protected virtual object GenerateDrillDownFilters()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
        var dateString = ReferenceDate.ToString("yyyy-MM-dd");
        if (ReferenceDate == today)
        {
            return new { date = dateString, view = "weekly" };
        }
        if (ReferenceDate == today.AddDays(1))
        {
            return new { date = dateString, view = "weekly" };
        }
        return new { date = dateString, view = "grid" };
    }
}