namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class MarketingTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string? RawData { get; init; }
}
