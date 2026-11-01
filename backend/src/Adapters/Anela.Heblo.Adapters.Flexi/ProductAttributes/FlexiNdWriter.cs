using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Rem.FlexiBeeSDK.Client.Clients.Products.Attributes;
using Rem.FlexiBeeSDK.Model.Products.Attributes;

namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

/// <summary>
/// Stores the ND (Nesmí dojít) flag as Flexi product attribute <see cref="FlexiProductAttributesQueryClient.NdAttributeId"/>
/// (ND, boolean; formerly TOPPRODUKT, matched by ID not name). Setting writes true; unsetting deletes the attribute row.
/// </summary>
public class FlexiNdWriter : ICatalogNdWriter
{
    private readonly IProductAttributeClient _attributeClient;

    public FlexiNdWriter(IProductAttributeClient attributeClient)
    {
        _attributeClient = attributeClient;
    }

    public async Task SetAsync(string productCode, bool isNd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new ArgumentException("A product code is required.", nameof(productCode));
        }

        var product = ProductReference.FromCode(productCode);
        var attributeId = FlexiProductAttributesQueryClient.NdAttributeId;

        var result = isNd
            ? await _attributeClient.SetAsync(product, attributeId, ProductAttributeValue.FromBoolean(true), ct)
            : await _attributeClient.ClearAsync(product, attributeId, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Flexi rejected the ND change for {productCode}: {result.StatusCode} {result.ErrorMessage}".TrimEnd());
        }
    }
}
