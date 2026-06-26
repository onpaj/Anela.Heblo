using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentResponse : BaseResponse
{
    public ResetShipmentData? Shipment { get; set; }

    public ResetOrderShipmentResponse(ResetShipmentData shipment)
    {
        Shipment = shipment;
    }

    public ResetOrderShipmentResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class ResetShipmentData
{
    public Guid ShipmentGuid { get; set; }
    public List<ResetShipmentPackage> Packages { get; set; } = [];
    public bool PendingCompletion { get; set; }
}

public class ResetShipmentPackage
{
    public string? TrackingNumber { get; set; }
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
}
