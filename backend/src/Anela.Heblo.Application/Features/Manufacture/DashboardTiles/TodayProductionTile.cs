using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public class TodayProductionTile : UpcomingProductionTile
{
    private readonly IManufactureOrderRepository _repository;

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
