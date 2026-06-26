namespace Anela.Heblo.Application.Features.ShoptetCustomers;

public interface IShoptetCustomerClient
{
    /// <summary>
    /// Fetches the Shoptet customer by their GUID (shoptet_guid or shoptet_user_guid from conversation variables).
    /// Returns null if the customer does not exist or the API returns 404.
    /// </summary>
    Task<ShoptetCustomerInfoDto?> GetCustomerByGuidAsync(string guid, CancellationToken ct = default);
}
