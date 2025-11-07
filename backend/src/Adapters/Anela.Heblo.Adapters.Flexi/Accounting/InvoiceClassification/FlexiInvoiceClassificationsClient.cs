using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;

namespace Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;

public class FlexiInvoiceClassificationsClient : IInvoiceClassificationsClient
{
    private readonly IAccountingTemplateClient _accountingTemplateClient;
    private readonly IReceivedInvoiceClient _receivedInvoiceClient;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly ILogger<FlexiInvoiceClassificationsClient> _logger;

    public FlexiInvoiceClassificationsClient(
        IAccountingTemplateClient accountingTemplateClient,
        IReceivedInvoiceClient receivedInvoiceClient,
        IOptions<DataSourceOptions> options,
        ILogger<FlexiInvoiceClassificationsClient> logger)
    {
        _accountingTemplateClient = accountingTemplateClient;
        _receivedInvoiceClient = receivedInvoiceClient;
        _options = options;
        _logger = logger;
    }

    public async Task<List<AccountingTemplateDto>> GetValidAccountingTemplatesAsync(CancellationToken? cancellationToken = default)
    {
        var templates = await _accountingTemplateClient.GetAsync();
        return templates
            .Where(w => !w.Code.StartsWith("N-") && w.AccountCode.StartsWith("5"))
            .Select(s => new AccountingTemplateDto
            {
                AccountCode = s.AccountCode,
                Code = s.Code,
                Description = s.Description,
                Name = s.Name,
            })
            .OrderBy(o => o.Code)
            .ToList();
    }

    public async Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode, CancellationToken? cancellationToken = default)
    {
        var result = await _accountingTemplateClient.UpdateInvoiceAsync(invoiceId, accountingTemplateCode, null);
        if (result.IsSuccess)
        {
            await _receivedInvoiceClient.RemoveTagAsync(invoiceId, [_options.Value.InvoiceClassificationTriggerLabel, _options.Value.InvoiceClassificationManualReviewLabel], cancellationToken ?? CancellationToken.None);
        }

        return result.IsSuccess;
    }

    public async Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason, CancellationToken? cancellationToken = default)
    {
        var resultAdd =
            await _receivedInvoiceClient.AddTagAsync(invoiceId, _options.Value.InvoiceClassificationManualReviewLabel, cancellationToken ?? CancellationToken.None);
        var resultRemove =
            await _receivedInvoiceClient.RemoveTagAsync(invoiceId, _options.Value.InvoiceClassificationTriggerLabel, cancellationToken ?? CancellationToken.None);

        return resultAdd.IsSuccess && resultRemove.IsSuccess;
    }
}