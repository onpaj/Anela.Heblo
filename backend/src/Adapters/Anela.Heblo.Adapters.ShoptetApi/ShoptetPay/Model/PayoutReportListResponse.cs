namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;

public class PayoutReportListResponse
{
    public List<PayoutReportDto> Items { get; set; } = new();
    public int Count { get; set; }
}
