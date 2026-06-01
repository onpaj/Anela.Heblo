using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;

public class GetStockTakingHistoryResponse : BaseResponse
{
    public List<StockTakingHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public GetStockTakingHistoryResponse() : base() { }
    public GetStockTakingHistoryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class StockTakingHistoryItemDto
{
    public int Id { get; set; }
    public StockTakingType Type { get; set; }
    public string Code { get; set; } = null!;
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }
    public double Difference => AmountNew - AmountOld;
}