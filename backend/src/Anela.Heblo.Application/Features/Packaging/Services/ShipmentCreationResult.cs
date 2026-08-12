using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.Services;

public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }

    /// <summary>Set when IsSuccess == false.</summary>
    public ErrorCodes? ErrorCode { get; init; }

    public Guid ShipmentGuid { get; init; }

    public string CarrierCode { get; init; } = null!;

    public string? CarrierName { get; init; }

    /// <summary>
    /// Exactly `numberOfPackages` entries: filtered to this shipment's GUID, padded with
    /// null-fields entries where Shoptet hasn't generated a label yet.
    /// </summary>
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = [];
}
