namespace Anela.Heblo.Domain.Features.ProductPricing;

public enum PriceSyncAction
{
    /// <summary>Heblo and the remote both match the last pushed value. Do nothing.</summary>
    None,

    /// <summary>Only Heblo moved. Push the new price.</summary>
    Push,

    /// <summary>The remote moved since Heblo last pushed. A human must decide.</summary>
    Conflict,

    /// <summary>Nothing has ever been pushed for this product/target. Adopt the remote value.</summary>
    Seed,

    /// <summary>The product does not exist in the remote system. Never create it.</summary>
    MissingRemote,
}
