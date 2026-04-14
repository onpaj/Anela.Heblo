using Anela.Heblo.Domain.Features.MarketingInvoices;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

/// <summary>
/// Null implementation of IMarketingTransactionSource for shared core.
/// Consuming applications must register their own implementation.
/// </summary>
public class NullMarketingTransactionSource : IMarketingTransactionSource
{
    public string Platform => "null";

    public Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        return Task.FromResult(new List<MarketingTransaction>());
    }
}
