using Anela.Heblo.Adapters.Shoptet.IssuedInvoices;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightInvoiceSource : IIssuedInvoiceSource
{
    private readonly IIssuedInvoiceParser _invoiceParser;
    private readonly IssuedInvoiceExportScenario _exportScenario;
    private readonly ILogger<ShoptetPlaywrightInvoiceSource> _logger;

    public ShoptetPlaywrightInvoiceSource(
        IIssuedInvoiceParser invoiceParser,
        IssuedInvoiceExportScenario exportScenario,
        ILogger<ShoptetPlaywrightInvoiceSource> logger)
    {
        _invoiceParser = invoiceParser;
        _exportScenario = exportScenario;
        _logger = logger;
    }

    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceRequest query)
    {
        var content = await ScenarioRetryHelper.ExecuteWithRetryAsync(
            async () => await _exportScenario.RunAsync(query),
            _logger,
            "IssuedInvoiceExportScenario",
            maxRetries: 3,
            delayMilliseconds: 2000
        );

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