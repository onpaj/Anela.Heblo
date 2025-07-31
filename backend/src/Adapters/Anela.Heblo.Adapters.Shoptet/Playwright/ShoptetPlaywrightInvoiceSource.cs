using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Application.Domain.IssuedInvoices;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightInvoiceSource : IIssuedInvoiceSource
{
    private readonly IIssuedInvoiceParser _invoiceParser;
    private readonly IssuedInvoiceExportScenario _exportScenario;

    public ShoptetPlaywrightInvoiceSource(IIssuedInvoiceParser invoiceParser, IssuedInvoiceExportScenario exportScenario)
    {
        _invoiceParser = invoiceParser;
        _exportScenario = exportScenario;
    }

    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query)
    {
        var content = await _exportScenario.RunAsync(query);
        var invoices = await _invoiceParser.ParseAsync(content);
        var invoiceBatch = new IssuedInvoiceDetailBatch()
        {
            Invoices = invoices,
            BatchId = query.RequestId,
        };

        return new List<IssuedInvoiceDetailBatch>() { invoiceBatch };
    }



    public Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default)
    {
        return Task.CompletedTask;
    }

    public Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default)
    {
        return Task.CompletedTask;
    }
}