using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public abstract class UpcomingProductionTile : ITile
{
    private readonly IManufactureOrderRepository _repository;

    // Self-describing metadata
    public abstract string Title { get; }
    public abstract string Description { get; }
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => true;
    public bool AutoShow => true; // Feature tile - user choice
    public Type ComponentType => typeof(object); // Frontend component type not needed for backend
    public string[] RequiredPermissions => Array.Empty<string>();

    protected abstract DateOnly ReferenceDate { get; set; }

    protected UpcomingProductionTile(IManufactureOrderRepository repository)
    {
        _repository = repository;
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
                            o.SemiProduct.ProductName,
                            SemiProductCompleted = o.State is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed,
                            ProductsCompleted = o.State == ManufactureOrderState.Completed,
                            o.ResponsiblePerson,
                            o.SemiProduct.ActualQuantity
                        }
                ).ToArray()
            },
            metadata = new
            {
                lastUpdated = DateTime.UtcNow,
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
        var dateString = ReferenceDate.ToString("yyyy-MM-dd");
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today))
        {
            return new { date = "today", view = "grid" };
        }
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
        {
            return new { date = "tomorrow", view = "grid" };
        }
        return new { date = dateString, view = "grid" };
    }
}