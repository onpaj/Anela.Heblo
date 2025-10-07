namespace Anela.Heblo.Domain.Features.Catalog;

public class MarginLevel
{
    public decimal Percentage { get; }
    public decimal Amount { get; }
    public decimal CostBase { get; }

    public MarginLevel(decimal percentage, decimal amount, decimal costBase)
    {
        Percentage = percentage;
        Amount = amount;
        CostBase = costBase;
    }

    public static MarginLevel Zero => new MarginLevel(0, 0, 0);

    public static MarginLevel Create(decimal sellingPrice, decimal totalCost)
    {
        if (sellingPrice <= 0)
        {
            return Zero;
        }

        var marginAmount = sellingPrice - totalCost;
        var marginPercentage = (marginAmount / sellingPrice) * 100;

        return new MarginLevel(
            Math.Round(marginPercentage, 2),
            Math.Round(marginAmount, 2),
            Math.Round(totalCost, 2)
        );
    }
}