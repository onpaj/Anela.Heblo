namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;

public class PayoutReportListResponse
{
    public List<PayoutReportDto> Data { get; set; } = new();
    public int Total { get; set; }
}
