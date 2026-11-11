namespace Anela.Heblo.Domain.Features.Catalog.Eshop;

public interface IEshopProductClient
{
    /// <summary>Sweeps all e-shop products and returns one entry per variant.</summary>
    Task<IReadOnlyList<EshopProduct>> ListAsync(CancellationToken ct);
}
