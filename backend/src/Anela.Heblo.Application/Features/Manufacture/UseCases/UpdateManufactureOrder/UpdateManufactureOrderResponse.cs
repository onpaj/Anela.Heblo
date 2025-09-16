using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderResponse : BaseResponse
{
    public UpdateManufactureOrderDto? Order { get; set; }

    public UpdateManufactureOrderResponse() : base() { }

    public UpdateManufactureOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}