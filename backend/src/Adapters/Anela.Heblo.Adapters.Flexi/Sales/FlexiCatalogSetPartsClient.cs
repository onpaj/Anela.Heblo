using Anela.Heblo.Domain.Features.Catalog.Sales;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Sales;

/// <summary>
/// Reads bundle composition from the FlexiBee "sady-a-komplety" evidence — the same source the
/// gift package screen uses. Note this is NOT the kusovnik (BoM); bundle composition is not there.
/// </summary>
public class FlexiCatalogSetPartsClient : ICatalogSetPartsClient
{
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILogger<FlexiCatalogSetPartsClient> _logger;

    public FlexiCatalogSetPartsClient(
        IProductSetsClient productSetsClient,
        ILogger<FlexiCatalogSetPartsClient> logger)
    {
        _productSetsClient = productSetsClient ?? throw new ArgumentNullException(nameof(productSetsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CatalogSetPart>> GetAsync(
        IEnumerable<string> setCodes,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<CatalogSetPart>();

        foreach (var setCode in setCodes.Distinct(StringComparer.Ordinal))
        {
            var setParts = await _productSetsClient.GetAsync(setCode, cancellationToken: cancellationToken);

            var components = setParts
                .Where(p => p.ProductList is { Count: > 0 })
                // Flexi rows can reference an archived product (blank code) or carry a
                // non-positive amount. Either would flow on into a synthetic sale record —
                // a blank code poisons the merge's product keying, a non-positive amount
                // produces zero or demand-reducing quantities. Drop both here.
                .Where(p => !string.IsNullOrWhiteSpace(p.Product?.Code) && p.Quantity > 0)
                .Select(p => new CatalogSetPart
                {
                    SetCode = setCode,
                    ComponentCode = p.Product.Code,
                    ComponentName = p.Product.Name,
                    Amount = p.Quantity,
                })
                .ToList();

            if (components.Count == 0)
            {
                _logger.LogWarning(
                    "Bundle {SetCode} has no components in Flexi — its sales will not be expanded onto any product.",
                    setCode);
                continue;
            }

            parts.AddRange(components);
        }

        return parts;
    }
}
