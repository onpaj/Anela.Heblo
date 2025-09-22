namespace Anela.Heblo.Domain.Features.Invoice;

/// <summary>
/// Represents an import attempt for an issued invoice
/// Maps to IssuedInvoiceSyncData table for backward compatibility
/// </summary>
public class ImportAttempt
{
    public Guid Id { get; set; }
    public string ExternalInvoiceId { get; set; } = null!;
    public DateTime AttemptedAt { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ImportId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public string? Currency { get; set; }
    
    /// <summary>
    /// Creates a new import attempt with current timestamp
    /// </summary>
    public static ImportAttempt Create(string externalInvoiceId)
    {
        return new ImportAttempt
        {
            Id = Guid.NewGuid(),
            ExternalInvoiceId = externalInvoiceId,
            AttemptedAt = DateTime.UtcNow,
            IsSuccess = false
        };
    }
    
    /// <summary>
    /// Marks the import attempt as successful
    /// </summary>
    public void MarkAsSuccessful(string importId)
    {
        IsSuccess = true;
        ImportId = importId;
        ErrorMessage = null;
    }
    
    /// <summary>
    /// Marks the import attempt as failed
    /// </summary>
    public void MarkAsFailed(string errorMessage)
    {
        IsSuccess = false;
        ErrorMessage = errorMessage;
        ImportId = null;
    }
}