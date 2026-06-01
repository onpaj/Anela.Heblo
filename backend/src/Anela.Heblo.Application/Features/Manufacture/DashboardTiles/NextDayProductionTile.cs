using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public class NextDayProductionTile : UpcomingProductionTile
{
    // Self-describing metadata
    public override string Title => $"Zítřejší výroba ({ReferenceDate.ToString("dd.MM.yyyy")})";
    public override string Description => "Plánovaná výroba na zítra";

    protected sealed override DateOnly ReferenceDate { get; set; }

    public NextDayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository)
    {
        ReferenceDate = GetNextWorkingDay(DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
    }

    private static DateOnly GetNextWorkingDay(DateOnly currentDate)
    {
        var nextDay = currentDate.AddDays(1);

        // Skip weekends (Saturday = 6, Sunday = 0)
        while (nextDay.DayOfWeek == DayOfWeek.Saturday || nextDay.DayOfWeek == DayOfWeek.Sunday)
        {
            nextDay = nextDay.AddDays(1);
        }

        return nextDay;
    }
}