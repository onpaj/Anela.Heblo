using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default);
}

public class PackingProductInfo
{
    public Cooling Cooling { get; init; }
    public int? WeightGrams { get; init; }
    public string? ImageUrl { get; init; }
}
