namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public interface IMarketingTransactionSource
{
    string Platform { get; }

    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
