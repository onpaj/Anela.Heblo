namespace Anela.Heblo.Application.Features.Invoice.Contracts;

public class ImportAttemptDto
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
}