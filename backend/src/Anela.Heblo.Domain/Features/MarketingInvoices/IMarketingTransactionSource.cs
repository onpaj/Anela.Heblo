namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IMarketingTransactionSource
{
    string Platform { get; }

    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
