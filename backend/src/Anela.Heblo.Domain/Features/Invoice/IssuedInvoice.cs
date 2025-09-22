namespace Anela.Heblo.Domain.Features.Invoice;

/// <summary>
/// Represents an issued invoice entity
/// </summary>
public class IssuedInvoice
{
    public string ExternalId { get; set; } = null!;
    public string InvoiceNumber { get; set; } = null!;
    public DateTime InvoiceDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? CustomerEmail { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    
    public List<ImportAttempt> ImportAttempts { get; set; } = new();
    
    /// <summary>
    /// Gets the latest import attempt
    /// </summary>
    public ImportAttempt? LatestImportAttempt => 
        ImportAttempts.OrderByDescending(a => a.AttemptedAt).FirstOrDefault();
    
    /// <summary>
    /// Checks if the invoice has been successfully imported
    /// </summary>
    public bool IsSuccessfullyImported => 
        ImportAttempts.Any(a => a.IsSuccess);
    
    /// <summary>
    /// Gets the successful import attempt if any
    /// </summary>
    public ImportAttempt? SuccessfulImportAttempt => 
        ImportAttempts.FirstOrDefault(a => a.IsSuccess);
}