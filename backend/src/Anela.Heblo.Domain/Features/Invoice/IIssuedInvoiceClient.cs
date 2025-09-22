namespace Anela.Heblo.Domain.Features.Invoice;

/// <summary>
/// Interface for importing invoices to external system (e.g., ABRA Flexi)
/// </summary>
public interface IIssuedInvoiceClient
{
    /// <summary>
    /// Import/save an invoice to the external system
    /// </summary>
    /// <param name="invoice">Invoice data to import</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with success status and import ID</returns>
    Task<InvoiceImportResult> SaveAsync(object invoice, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of invoice import operation
/// </summary>
public class InvoiceImportResult
{
    public bool IsSuccess { get; set; }
    public string? ImportId { get; set; }
    public string? ErrorMessage { get; set; }
    
    public static InvoiceImportResult Success(string importId)
    {
        return new InvoiceImportResult
        {
            IsSuccess = true,
            ImportId = importId
        };
    }
    
    public static InvoiceImportResult Failure(string errorMessage)
    {
        return new InvoiceImportResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}