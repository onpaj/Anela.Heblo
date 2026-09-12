using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.Flexi.Price;

/// <summary>
/// Supplies the VAT rate the write path divides by to turn a with-VAT price into Flexi's
/// <c>cenaZakl</c>.
///
/// It reports only rates Flexi's own VAT band positively identified. It deliberately does
/// NOT recover a rate arithmetically from the with/without-VAT pair: both of those numbers
/// are computed by this adapter from the very band in question, so any rate recovered from
/// them is Heblo's own assumption handed back dressed as a measurement — and a wrong one
/// goes straight into a live ERP price with nothing anywhere reporting it. A product whose
/// band was not recognised is omitted, which makes <c>SetProductPriceHandler</c> refuse the
/// write outright.
/// </summary>
public class FlexiProductVatRateProvider : IProductVatRateProvider
{
    private readonly IProductPriceErpClient _erpClient;

    public FlexiProductVatRateProvider(IProductPriceErpClient erpClient)
    {
        _erpClient = erpClient;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetVatRatesAsync(CancellationToken ct)
    {
        var prices = await _erpClient.GetAllAsync(forceReload: false, ct);

        // The PriceWithoutVat > 0 condition predates this provider's band-aware form and is
        // kept deliberately: relaxing it would newly enable live writes for items Flexi
        // currently prices at zero, which is beyond the scope of the VAT fix.
        return prices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode)
                        && p.PriceWithoutVat > 0
                        && p.VatRate.HasValue)
            .GroupBy(p => p.ProductCode)
            .ToDictionary(
                g => g.Key,
                g => g.First().VatRate!.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}
