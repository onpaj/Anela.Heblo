namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
