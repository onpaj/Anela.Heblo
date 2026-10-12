namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class EshopStockSupply
{
    public string Code { get; set; } = string.Empty;
    public double Amount { get; set; }

    /// <summary><c>false</c> when Shoptet returned a null or unparseable <c>amount</c> and <see cref="Amount"/> is a 0 fallback.</summary>
    public bool IsAmountKnown { get; set; } = true;
    public double Claim { get; set; }
}
