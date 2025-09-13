using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;

public class CreateManufactureOrderResponse : BaseResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    public CreateManufactureOrderResponse() : base() { }

    public CreateManufactureOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}