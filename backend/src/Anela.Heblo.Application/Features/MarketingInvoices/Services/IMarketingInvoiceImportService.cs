using Anela.Heblo.Domain.Features.MarketingInvoices;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public interface IMarketingInvoiceImportService
{
    Task<MarketingImportResult> ImportAsync(
        IMarketingTransactionSource source,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
