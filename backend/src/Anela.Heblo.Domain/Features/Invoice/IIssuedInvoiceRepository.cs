namespace Anela.Heblo.Domain.Features.Invoice;

/// <summary>
/// Repository interface for issued invoice operations
/// </summary>
public interface IIssuedInvoiceRepository
{
    // Import tracking operations
    Task<ImportAttempt> RecordImportAttemptAsync(ImportAttempt attempt);
    Task<List<ImportAttempt>> GetImportHistoryAsync(string externalId);
    Task<bool> IsSuccessfullyImportedAsync(string externalId);
    
    // Invoice query operations
    Task<List<IssuedInvoice>> GetImportedInvoicesAsync(int page, int pageSize);
    Task<List<IssuedInvoice>> SearchInvoicesAsync(string searchTerm);
    Task<IssuedInvoice?> GetInvoiceDetailAsync(string externalId);
    Task<int> GetTotalInvoicesCountAsync();
}