namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackingOrderShippingSource
{
    /// <summary>
    /// Returns the e-shop shipping method GUID of the order, or null if the order does not exist
    /// or has no shipping method. One order-detail call to the e-shop; no catalog or cooling lookups.
    /// </summary>
    Task<string?> GetShippingMethodGuidAsync(string orderCode, CancellationToken ct = default);
}
