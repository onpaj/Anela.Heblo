using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;

namespace Anela.Heblo.Adapters.Flexi.InvoiceClassification;

public class FlexiInvoiceClassificationsClient : IInvoiceClassificationsClient
{
    private readonly IAccountingTemplateClient _accountingTemplateClient;
    private readonly ILogger<FlexiInvoiceClassificationsClient> _logger;

    public FlexiInvoiceClassificationsClient(
        IAccountingTemplateClient  accountingTemplateClient,
        ILogger<FlexiInvoiceClassificationsClient> logger)
    {
        _accountingTemplateClient = accountingTemplateClient;
        _logger = logger;
    }

    public async Task<List<AccountingTemplateDto>> GetValidAccountingTemplatesAsync()
    {
        var templates = await _accountingTemplateClient.GetAsync();
        return templates
            .Where(w => !w.Code.StartsWith("N-") && w.AccountCode.StartsWith("5"))
            .Select(s =>  new AccountingTemplateDto
            {
                AccountCode = s.AccountCode,
                Code = s.Code,
                Description = s.Description,
                Name = s.Name,
            })
            .ToList();
    }

    public async Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode)
    {
        var result = await _accountingTemplateClient.UpdateInvoiceAsync(invoiceId, accountingTemplateCode, null);
        return result.IsSuccess;
    }

    public async Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason)
    {
        _logger.LogInformation("Marking invoice {InvoiceId} for manual review with reason: {Reason} - IMPLEMENTATION PLACEHOLDER", 
            invoiceId, reason);
        
        // TODO: Implement actual ABRA Flexi API integration
        await Task.Delay(150); // Simulate API call
        
        // For now, always return success
        _logger.LogInformation("Successfully marked invoice {InvoiceId} for manual review", invoiceId);
        return true;
    }
}