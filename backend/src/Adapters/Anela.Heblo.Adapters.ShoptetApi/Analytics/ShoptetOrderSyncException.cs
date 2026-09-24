namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Raised when the Shoptet API answers in a way that cannot be trusted to be complete — an
/// unrecognised envelope, or a page series that stops before the paginator says it should.
///
/// It exists so those cases fail the run instead of looking like an empty result: both sync
/// services advance a cursor or watermark past whatever they were handed, so "nothing came back"
/// and "the read was truncated" have permanently different consequences.
/// </summary>
public sealed class ShoptetOrderSyncException : Exception
{
    public ShoptetOrderSyncException(string message) : base(message)
    {
    }
}
