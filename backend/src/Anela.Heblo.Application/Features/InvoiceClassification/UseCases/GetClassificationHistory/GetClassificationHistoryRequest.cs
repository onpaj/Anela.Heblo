using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;

public class GetClassificationHistoryRequest : IRequest<GetClassificationHistoryResponse>
{
    public int Page { get; set; } = 1;
    
    public int PageSize { get; set; } = 20;
    
    public DateTime? FromDate { get; set; }
    
    public DateTime? ToDate { get; set; }
    
    public string? InvoiceNumber { get; set; }
    
    public string? CompanyName { get; set; }
}