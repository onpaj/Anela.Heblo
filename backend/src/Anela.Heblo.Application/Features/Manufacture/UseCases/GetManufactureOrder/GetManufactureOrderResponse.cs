using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;

public class GetManufactureOrderResponse : BaseResponse
{
    public ManufactureOrderDto? Order { get; set; }

    public GetManufactureOrderResponse() : base()
    {
    }

    public GetManufactureOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters)
    {
    }
}