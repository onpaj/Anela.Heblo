using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingJobStatus;

public class GetStockTakingJobStatusResponse : BaseResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsSucceeded { get; set; }
    public bool IsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public StockTakingResultDto? Result { get; set; }

    public GetStockTakingJobStatusResponse() : base() { }
    
    public GetStockTakingJobStatusResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}