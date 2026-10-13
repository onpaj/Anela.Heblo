namespace Anela.Heblo.Application.Features.ShipmentLabels;

/// <summary>
/// Thrown by IShipmentClient.CreateShipmentAsync implementations when the carrier API rejects
/// shipment creation with a permanent, non-retryable validation error (e.g. Shoptet's
/// "shipment-validation-failed" — recipient address missing required fields). Distinct from a
/// generic HttpRequestException, which still represents a transient/unclassified failure that
/// may succeed on retry. See docs/integrations/shoptet-api.md for the known Shoptet causes.
/// </summary>
public class ShoptetShipmentValidationException : Exception
{
    public string OrderCode { get; }

    /// <summary>The carrier's own error code, e.g. "shipment-validation-failed".</summary>
    public string ShoptetErrorCode { get; }

    /// <summary>The carrier's own "instance" field, e.g. "data.orderCode" — nullable because not every carrier error includes one.</summary>
    public string? Instance { get; }

    public ShoptetShipmentValidationException(string orderCode, string shoptetErrorCode, string message, string? instance)
        : base(message)
    {
        OrderCode = orderCode;
        ShoptetErrorCode = shoptetErrorCode;
        Instance = instance;
    }
}
