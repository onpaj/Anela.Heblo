namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoice>> GetUnclassifiedInvoicesAsync();

    Task<ReceivedInvoice?> GetInvoiceByIdAsync(string invoiceId);
}