using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Rem.FlexiBeeSDK.Client.Clients.Products.Attributes;
using Rem.FlexiBeeSDK.Model.Products.Attributes;

namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

/// <summary>
/// Stores the "top product" flag as Flexi product attribute <see cref="FlexiProductAttributesQueryClient.TopProductAttributeId"/>
/// (TOPPRODUKT, boolean). Setting writes true; unsetting deletes the attribute row.
/// </summary>
public class FlexiTopProductWriter : ICatalogTopProductWriter
{
    private readonly IProductAttributeClient _attributeClient;

    public FlexiTopProductWriter(IProductAttributeClient attributeClient)
    {
        _attributeClient = attributeClient;
    }

    public async Task SetAsync(string productCode, bool isTopProduct, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new ArgumentException("A product code is required.", nameof(productCode));
        }

        var product = ProductReference.FromCode(productCode);
        var attributeId = FlexiProductAttributesQueryClient.TopProductAttributeId;

        var result = isTopProduct
            ? await _attributeClient.SetAsync(product, attributeId, ProductAttributeValue.FromBoolean(true), ct)
            : await _attributeClient.ClearAsync(product, attributeId, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Flexi rejected the top-product change for {productCode}: {result.StatusCode} {result.ErrorMessage}".TrimEnd());
        }
    }
}
