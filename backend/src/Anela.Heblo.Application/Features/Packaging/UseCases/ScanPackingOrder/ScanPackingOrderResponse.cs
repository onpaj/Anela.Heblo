using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderResponse : BaseResponse
{
    public ScanOrderData? Order { get; set; }
    public ScanShipmentData? Shipment { get; set; }

    public ScanPackingOrderResponse(ScanOrderData order)
    {
        Order = order;
    }

    public ScanPackingOrderResponse(ScanOrderData order, ScanShipmentData shipment)
    {
        Order = order;
        Shipment = shipment;
    }

    public ScanPackingOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class ScanOrderData
{
    public string Code { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string ShippingMethodName { get; set; } = null!;
    public Cooling Cooling { get; set; }
    public bool IsCooled { get; set; }
    public string? CustomerNote { get; set; }
    public string? EshopNote { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
    public ScanOrderEligibility Eligibility { get; set; } = null!;
    public List<ScanPackingOrderItemDto> Items { get; set; } = [];
}

public class ShippingAddress
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
}

public class ScanOrderEligibility
{
    public bool IsEligible { get; set; }
    public string? WarningTitle { get; set; }
    public string? WarningBody { get; set; }
}

public class ScanShipmentData
{
    public Guid ShipmentGuid { get; set; }
    public List<ScanShipmentPackage> Packages { get; set; } = [];
    public bool AlreadyExisted { get; set; }
}

public class ScanShipmentPackage
{
    public string Name { get; set; } = null!;
    public string? TrackingNumber { get; set; }
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
}
