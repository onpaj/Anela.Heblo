namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoice>> GetUnclassifiedInvoicesAsync();

    Task<ReceivedInvoice?> GetInvoiceByIdAsync(string invoiceId);

    /// <summary>
    /// Received invoices whose accounting date falls in [accountingDateFrom, accountingDateTo] (inclusive, dates only)
    /// and whose supplier DIČ is in <paramref name="vatIds"/>. One Flexi call, no paging (limit=0).
    /// </summary>
    Task<List<ReceivedInvoice>> SearchByVatIdsAsync(
        DateTime accountingDateFrom,
        DateTime accountingDateTo,
        IReadOnlyCollection<string> vatIds,
        CancellationToken cancellationToken = default);
}