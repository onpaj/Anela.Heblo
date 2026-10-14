using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Shoptet order lifecycle operations needed only for integration-test setup/teardown
/// (creating and deleting real test orders in the live Shoptet store, and looking them
/// up by test-seed prefix). Not part of the Application-layer contract — Application
/// handlers must never depend on this interface.
/// </summary>
public interface IShoptetOrderTestClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);
}
