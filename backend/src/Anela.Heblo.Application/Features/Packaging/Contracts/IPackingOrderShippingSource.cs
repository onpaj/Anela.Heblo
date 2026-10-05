namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackingOrderShippingSource
{
    /// <summary>
    /// Returns the e-shop shipping method GUID of the order, or null if the order does not exist
    /// or has no shipping method. Reads only the order header — no catalog lookups.
    /// </summary>
    Task<string?> GetShippingMethodGuidAsync(string orderCode, CancellationToken ct = default);
}
