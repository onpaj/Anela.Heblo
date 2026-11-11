using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

public class GetPurchaseStockAnalysisResponse : BaseResponse
{
    public List<StockAnalysisItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public StockAnalysisSummaryDto Summary { get; set; } = new();

    public GetPurchaseStockAnalysisResponse() : base() { }
    public GetPurchaseStockAnalysisResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
