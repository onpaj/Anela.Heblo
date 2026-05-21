using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

public class PrepareOrderLabelResponse : BaseResponse
{
    public bool ExistingShipmentFound { get; set; }
    public bool LabelReady { get; set; }
    public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];

    // Success: new or recreated shipment
    public PrepareOrderLabelResponse(bool labelReady, IReadOnlyList<ShipmentLabelDto> labels)
    {
        LabelReady = labelReady;
        Labels = labels;
    }

    // Success: labels already existed and forceRecreate=false
    public PrepareOrderLabelResponse(IReadOnlyList<ShipmentLabelDto> existingLabels)
    {
        ExistingShipmentFound = true;
        LabelReady = existingLabels.Any(l => l.LabelUrl is not null);
        Labels = existingLabels;
    }

    // Error
    public PrepareOrderLabelResponse(ErrorCodes errorCode) : base(errorCode)
    {
    }
}
