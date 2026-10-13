using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Accounting.MarketingPerformance;

/// <summary>Cost step: one Flexi call per month — received invoices by accounting date and supplier DIČ.</summary>
public class FlexiMonthlyAdCostSource : IMonthlyAdCostSource
{
    private readonly IReceivedInvoicesClient _client;
    private readonly ILogger<FlexiMonthlyAdCostSource> _logger;

    public FlexiMonthlyAdCostSource(IReceivedInvoicesClient client, ILogger<FlexiMonthlyAdCostSource> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken)
    {
        if (vatIds.Count == 0)
        {
            _logger.LogWarning("Marketing performance: no VAT IDs configured, skipping Flexi call for {Month}", month);
            return Array.Empty<AdCostInvoice>();
        }

        var invoices = await _client.SearchByVatIdsAsync(month.Start, month.LastDay, vatIds, cancellationToken);

        return invoices.Select(i => new AdCostInvoice
        {
            InvoiceNumber = i.InvoiceNumber,
            SupplierVatId = i.SupplierVatId ?? string.Empty,
            AccountingDate = i.AccountingDate ?? month.Start,
            AmountWithoutVat = i.TotalAmountWithoutVat,
            IsCancelled = i.IsCancelled,
        }).ToList();
    }
}
