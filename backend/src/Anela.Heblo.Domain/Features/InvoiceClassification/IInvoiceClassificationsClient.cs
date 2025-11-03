namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IInvoiceClassificationsClient
{
    Task<List<AccountingTemplateDto>> GetValidAccountingTemplatesAsync();
    
    Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode);
    
    Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason);
}