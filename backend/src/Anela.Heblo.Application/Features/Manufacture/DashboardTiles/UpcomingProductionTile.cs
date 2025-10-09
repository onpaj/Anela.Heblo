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
    
    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _repository.GetOrdersForDateRangeAsync(ReferenceDate, ReferenceDate, cancellationToken);

        return new
        {
            TotalOrders = orders.Count(),
            Products = orders.Take(5).SelectMany(o =>
                o.Products.Take(1).Select(p => new {
                    p.ProductName,
                    SemiProductCompleted = o.State is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed,
                    ProductsCompleted = o.State == ManufactureOrderState.Completed
                })
            ).ToArray()
        };
    }
}