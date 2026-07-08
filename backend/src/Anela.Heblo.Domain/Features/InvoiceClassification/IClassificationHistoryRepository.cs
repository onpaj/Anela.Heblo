namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);

    Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1,
        int pageSize = 20,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? invoiceNumber = null,
        string? companyName = null);
}