namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync();

    Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId);
}