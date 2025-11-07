namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IInvoiceClassificationsClient
{
    Task<List<AccountingTemplateDto>> GetValidAccountingTemplatesAsync(CancellationToken? cancellationToken = default);

    Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode,
        string? matchedRuleDepartment, CancellationToken? cancellationToken = default);

    Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason, CancellationToken? cancellationToken = default);
}