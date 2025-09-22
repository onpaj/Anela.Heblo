using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceDetail;

public class GetInvoiceDetailResponse : BaseResponse
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
    public bool IsSuccessfullyImported { get; set; }
    
    /// <summary>
    /// All import attempts for this invoice, ordered by most recent first
    /// </summary>
    public List<ImportAttemptDto> ImportAttempts { get; set; } = new();
}