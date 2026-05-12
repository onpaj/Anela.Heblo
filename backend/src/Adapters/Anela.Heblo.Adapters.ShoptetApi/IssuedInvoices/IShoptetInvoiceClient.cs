using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;

public interface IShoptetInvoiceClient
{
    Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken ct = default);

    Task<ShoptetInvoiceDto?> GetInvoiceAsync(string code, CancellationToken ct = default);

    Task<string> GetInvoiceRawJsonAsync(string code, CancellationToken ct = default);
}
