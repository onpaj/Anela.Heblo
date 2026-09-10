using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceImportStatisticsSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetDailyCountsAsync(startDate, endDate, dateType, cancellationToken);
    }
}
