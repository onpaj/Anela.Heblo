namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackingOrderCountSource
{
    /// <summary>
    /// Returns the total count of orders currently in the configured packing state ("Balí se").
    /// </summary>
    Task<int> GetOrdersBeingPackedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the total count of orders currently in the configured processing state ("Vyřizuje se").
    /// </summary>
    Task<int> GetOrdersBeingProcessedCountAsync(CancellationToken ct = default);
}
