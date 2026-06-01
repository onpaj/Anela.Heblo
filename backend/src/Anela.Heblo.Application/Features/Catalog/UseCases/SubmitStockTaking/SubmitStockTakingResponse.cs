using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingResponse : BaseResponse
{
    public int Id { get; set; }
    public StockTakingType Type { get; set; }
    public string Code { get; set; } = null!;
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }

    public SubmitStockTakingResponse() : base() { }
    public SubmitStockTakingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}