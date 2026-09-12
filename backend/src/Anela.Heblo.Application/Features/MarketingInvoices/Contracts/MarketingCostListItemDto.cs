namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingCostListItemDto
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; }
}
