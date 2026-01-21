using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryResponse : BaseResponse
{
    public int PendingCount { get; set; }
    public int SubmittedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalInQueue => PendingCount + SubmittedCount;

    public GetStockUpOperationsSummaryResponse() : base() { }

    public GetStockUpOperationsSummaryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
