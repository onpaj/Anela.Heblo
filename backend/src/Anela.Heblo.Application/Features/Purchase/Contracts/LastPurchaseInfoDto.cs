namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class LastPurchaseInfoDto
{
    public DateTime Date { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public double Amount { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }
}
