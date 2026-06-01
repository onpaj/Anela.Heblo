namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;

public class PayoutReportDto
{
    public string Id { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public int SerialNumber { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime CreatedAt { get; set; }
}
