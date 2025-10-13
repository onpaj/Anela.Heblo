namespace Anela.Heblo.Domain.Features.Catalog;

public class MarginLevel
{
    public decimal Percentage { get; }
    public decimal Amount { get; }
    public decimal CostTotal { get; }
    public decimal CostLevel { get; }

    public MarginLevel(decimal percentage, decimal amount, decimal costTotal, decimal costLevel)
    {
        Percentage = percentage;
        Amount = amount;
        CostTotal = costTotal;
        CostLevel = costLevel;
    }

    [Obsolete("Use constructor with costLevel parameter instead")]
    public MarginLevel(decimal percentage, decimal amount, decimal costBase)
        : this(percentage, amount, costBase, 0)
    {
    }

    public static MarginLevel Zero => new MarginLevel(0, 0, 0, 0);

    public static MarginLevel Create(decimal sellingPrice, decimal totalCost, decimal levelCost)
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
            Math.Round(totalCost, 2),
            Math.Round(levelCost, 2)
        );
    }

    public static MarginLevel Create(decimal sellingPrice, decimal totalCost)
    {
        return Create(sellingPrice, totalCost, 0);
    }

    [Obsolete("Use CostTotal instead")]
    public decimal CostBase => CostTotal;
}