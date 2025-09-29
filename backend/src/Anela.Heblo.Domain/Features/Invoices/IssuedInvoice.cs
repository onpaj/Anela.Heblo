namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Entity representing an issued invoice from Shoptet
/// Used for monitoring import statistics and tracking sync history
/// </summary>
public class IssuedInvoice
{
    public Guid Id { get; set; }

    /// <summary>
    /// Invoice code from Shoptet
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Date when the invoice was issued (creation date)
    /// </summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>
    /// Last time this invoice was synchronized from Shoptet
    /// </summary>
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// Creation timestamp in our system
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last modification timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}