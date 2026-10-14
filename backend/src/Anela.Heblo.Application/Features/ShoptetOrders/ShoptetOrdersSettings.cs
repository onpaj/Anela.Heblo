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

    /// <summary>
    /// The missing-invoice job only considers orders created at or after this instant
    /// (orders in <see cref="CompletedStatusId"/> with a non-zero total and no invoice get one).
    /// </summary>
    public DateTimeOffset MissingInvoiceOrdersCreatedFrom { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

    /// <summary>
    /// Pause between two orders (each one an invoice check plus a POST), keeping the job under Shoptet's
    /// ~4 req/s budget (shoptet-api.md §3.14) that it shares with packing and expedition.
    /// </summary>
    public int MissingInvoiceCreationDelayMs { get; set; } = 500;

    /// <summary>
    /// Upper bound on invoices created in one run; the rest wait for the next night. Shoptet invoices
    /// cannot be deleted through the API, so this caps the damage of a misread invoice listing.
    /// </summary>
    public int MissingInvoiceMaxPerRun { get; set; } = 300;

    /// <summary>
    /// Shoptet order states in which the double-shot swap may rewrite order lines (pre-picking only).
    /// Empty = use DoubleShotOrderSwapService.DefaultEligibleStatusIds
    /// (-1 Nová, 3 Čeká se na platbu, 32 Zaplaceno, -2 Vyřizuje se, 35 Poznámka, 73 Oprava-robot).
    /// Kept empty by default on purpose: the config binder merges arrays index-wise, so a non-empty
    /// default would leak into a shorter override.
    /// </summary>
    public int[] DoubleShotSwapEligibleStatusIds { get; set; } = [];

    /// <summary>
    /// How far back (by creation time) the double-shot swap looks for orders containing a V variant.
    /// </summary>
    public int DoubleShotSwapLookbackDays { get; set; } = 14;

    /// <summary>
    /// Shoptet status a third-party add-on puts new double-shot V orders into, so no other automation
    /// touches them before the swap. Heblo swaps those orders, then moves them to
    /// <see cref="DoubleShotSwapReleaseStatusId"/>. Null = no hold status (set per environment: the
    /// test store has no such status).
    /// </summary>
    public int? DoubleShotSwapHoldStatusId { get; set; }

    /// <summary>
    /// Status a held order is moved to once it holds no V line. Defaults to -1 ("Nová").
    /// </summary>
    public int DoubleShotSwapReleaseStatusId { get; set; } = -1;
}
