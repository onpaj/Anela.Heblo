using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;

public class SubmitManufactureStockTakingResponse : BaseResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Code { get; set; }
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }

    public SubmitManufactureStockTakingResponse() : base() { }

    public SubmitManufactureStockTakingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}