using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentResponse : BaseResponse
{
    public Guid? ShipmentGuid { get; set; }
    public string? Status { get; set; }
    public bool LabelReady { get; set; }
    public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];
    public bool ExistingShipmentFound { get; set; }

    public CreateOrderShipmentResponse(
        Guid shipmentGuid,
        string? status,
        bool labelReady,
        IReadOnlyList<ShipmentLabelDto> labels)
    {
        ShipmentGuid = shipmentGuid;
        Status = status;
        LabelReady = labelReady;
        Labels = labels;
    }

    public CreateOrderShipmentResponse(
        ErrorCodes errorCode,
        IReadOnlyList<ShipmentLabelDto>? existingLabels = null,
        bool existingShipmentFound = false)
        : base(errorCode)
    {
        Labels = existingLabels ?? [];
        ExistingShipmentFound = existingShipmentFound;
    }
}
