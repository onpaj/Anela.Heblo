namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class ShoptetOrdersSettings
{
    public const string ConfigurationKey = "ShoptetOrders";

    /// <summary>
    /// Shoptet order status IDs that are valid source states for the blocking operation.
    /// Orders in any other state will be rejected with ShoptetOrderInvalidSourceState.
    /// Configure actual values in user secrets / Azure App Config per environment.
    /// </summary>
    public int[] AllowedBlockSourceStateIds { get; set; } = [];

    /// <summary>
    /// Shoptet order status ID to assign when blocking an order.
    /// Configure actual value in user secrets / Azure App Config per environment.
    /// </summary>
    public int BlockedStatusId { get; set; }

    /// <summary>
    /// Shoptet order status ID representing the "Balí se" (being packed) state.
    /// The Balení packing screen warns the operator when a scanned order is in any other state.
    /// </summary>
    public int PackingStateId { get; set; } = 26;

    /// <summary>
    /// Shoptet order status ID assigned after the operator successfully scans and packs an order.
    /// Defaults to 52 ("Zabaleno").
    /// </summary>
    public int PackedStateId { get; set; } = 52;
}
