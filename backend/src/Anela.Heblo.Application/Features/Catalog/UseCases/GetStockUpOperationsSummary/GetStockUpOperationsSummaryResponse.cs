using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryResponse : BaseResponse
{
    public int PendingCount { get; set; }
    public int SubmittedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalInQueue => PendingCount + SubmittedCount;
}
