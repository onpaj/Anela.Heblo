namespace Anela.Heblo.Domain.Features.Catalog.ValueObjects;

public class MonthlyCost
{
    public DateTime Month { get; }
    public decimal Cost { get; }

    public MonthlyCost(DateTime month, decimal cost)
    {
        Month = month;
        Cost = cost;
    }
}