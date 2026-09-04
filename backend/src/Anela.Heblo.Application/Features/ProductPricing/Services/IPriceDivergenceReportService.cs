namespace Anela.Heblo.Application.Features.ProductPricing.Services;

/// <summary>
/// Read-only comparison of Shoptet, Flexi and Heblo master retail prices. Never writes to
/// any of the three — see <see cref="PriceDivergenceReportService"/> for the constructor
/// dependencies, which deliberately exclude every write-capable interface member
/// (<c>IEshopPriceListClient.SetPriceWithVatAsync</c>, <c>IErpPriceWriter</c>,
/// <c>IProductPriceRepository.UpsertAsync</c>/<c>UpsertSyncStateAsync</c>/<c>SaveChangesAsync</c>).
/// </summary>
public interface IPriceDivergenceReportService
{
    Task<PriceDivergenceReportResult> BuildReportAsync(CancellationToken ct);
}
