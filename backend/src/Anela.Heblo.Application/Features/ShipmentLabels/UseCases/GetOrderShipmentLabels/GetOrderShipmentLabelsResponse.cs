using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsResponse : BaseResponse
{
    public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];

    public GetOrderShipmentLabelsResponse(IReadOnlyList<ShipmentLabelDto> labels)
    {
        Labels = labels;
    }

    public GetOrderShipmentLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }
}
