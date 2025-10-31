namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);
    
    Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50);
    
    Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId);
    
    Task<ClassificationStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
}