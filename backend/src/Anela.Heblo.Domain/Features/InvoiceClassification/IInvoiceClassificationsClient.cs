namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IInvoiceClassificationsClient
{
    Task<List<AccountingPrescriptionDto>> GetValidAccountingPrescriptionsAsync();
    
    Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingPrescription);
    
    Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason);
}