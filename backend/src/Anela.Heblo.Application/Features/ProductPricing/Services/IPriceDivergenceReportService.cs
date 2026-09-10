namespace Anela.Heblo.Application.Features.ProductPricing.Services;

/// <summary>
/// Read-only comparison of Shoptet and Flexi retail prices. Never writes to either — see
/// <see cref="PriceDivergenceReportService"/> for the constructor dependencies, which
/// deliberately exclude every write-capable interface member
/// (<c>IEshopPriceListClient.SetPriceWithVatAsync</c>, <c>IErpPriceWriter</c>).
/// </summary>
public interface IPriceDivergenceReportService
{
    Task<PriceDivergenceReportResult> BuildReportAsync(CancellationToken ct);
}
