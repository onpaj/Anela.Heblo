using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;

public class GetClassificationHistoryResponse : BaseResponse
{
    public List<ClassificationHistoryDto> Items { get; set; } = new();
    
    public int TotalCount { get; set; }
    
    public int Page { get; set; }
    
    public int PageSize { get; set; }
    
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public GetClassificationHistoryResponse() : base() { }

    public GetClassificationHistoryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}