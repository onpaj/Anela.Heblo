using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

[TileId("todayproduction")]
public class TodayProductionTile : UpcomingProductionTile
{
    // Self-describing metadata
    public override string Title => $"Dnešní výroba ({ReferenceDate.ToString("dd.MM.yyyy")})";
    public override string Description => "Produkty vyráběné dnes";
    // Self-describing metadata

    protected sealed override DateOnly ReferenceDate { get; set; }

    public TodayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository)
    {
        ReferenceDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().Date); // Get next workday
    }
}
