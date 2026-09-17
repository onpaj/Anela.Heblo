namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IOrderStatusReader
{
    /// <summary>
    /// Returns the order's current Shoptet status id.
    /// Mirrors IEshopOrderClient.GetOrderStatusIdAsync; may throw HttpRequestException with
    /// StatusCode == HttpStatusCode.NotFound when the order does not exist — callers depend on this
    /// exact exception shape (see PrintExpeditionOrderHandler's 404 handling). Implementations must
    /// let it propagate unmodified.
    /// </summary>
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
}
