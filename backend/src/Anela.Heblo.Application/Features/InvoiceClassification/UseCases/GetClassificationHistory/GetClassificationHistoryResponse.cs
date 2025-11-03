using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;

public class GetClassificationHistoryResponse
{
    public List<ClassificationHistoryDto> Items { get; set; } = new();
    
    public int TotalCount { get; set; }
    
    public int Page { get; set; }
    
    public int PageSize { get; set; }
    
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}