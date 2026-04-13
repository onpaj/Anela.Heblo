namespace Anela.Heblo.Application.Features.Catalog.Stock;

/// <summary>
/// Abstraction for updating product stock quantities via the Shoptet REST API.
/// Placed in the Application layer so both adapters (REST and Playwright) can reference it
/// without creating a cross-adapter project dependency.
/// </summary>
public interface IShoptetStockClient
{
    /// <summary>
    /// Applies a relative stock quantity change for one product.
    /// Positive <paramref name="amountChange"/> increases stock (stock-up).
    /// Negative <paramref name="amountChange"/> decreases stock (e.g., ingredient consumption).
    /// </summary>
    Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default);
}
