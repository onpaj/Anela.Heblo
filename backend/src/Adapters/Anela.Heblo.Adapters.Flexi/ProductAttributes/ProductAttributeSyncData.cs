using Anela.Heblo.Data;

namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

public class ProductAttributeSyncData : SyncData
{
    public ProductAttributeSyncData(IList<ProductAttributesFlexiDto> attributes) : base("Product attributes (Abra)", attributes.Count, 0, 100)
    {
    }
}