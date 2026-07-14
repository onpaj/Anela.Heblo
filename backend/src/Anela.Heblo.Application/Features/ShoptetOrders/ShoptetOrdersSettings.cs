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

    /// <summary>
    /// Shoptet order status ID representing the "Vyřizuje se" (being processed) state.
    /// These orders later transition into the packing state; the count is surfaced on the
    /// packing dashboard as an "incoming work" indicator.
    /// Defaults to -2 ("Vyřizuje se"), a global Shoptet system state.
    /// </summary>
    public int ProcessingStateId { get; set; } = -2;

    /// <summary>
    /// Shoptet order status IDs whose orders are polled by the auto-completion job.
    /// These are the "handed to carrier" states (70 "Předáno přepravci",
    /// 82 "SMS chlazené-Předáno dopravci"). An order in one of these states is moved to
    /// <see cref="CompletedStatusId"/> once any of its shipments reports "delivered".
    /// </summary>
    public int[] DeliveredCompletionSourceStateIds { get; set; } = [70, 82];

    /// <summary>
    /// Shoptet order status IDs polled by the auto-completion job when the
    /// "is-delivered-order-completion-test-source-enabled" feature flag is on —
    /// replaces <see cref="DeliveredCompletionSourceStateIds"/> entirely so the pipeline
    /// can be exercised on a controlled set of orders. Defaults to 73 ("Oprava-robot").
    /// </summary>
    public int[] DeliveredCompletionTestSourceStateIds { get; set; } = [73];

    /// <summary>
    /// Shoptet order status ID assigned when a delivered order is auto-completed.
    /// Defaults to -3 ("Vyřízena").
    /// </summary>
    public int CompletedStatusId { get; set; } = -3;
}
