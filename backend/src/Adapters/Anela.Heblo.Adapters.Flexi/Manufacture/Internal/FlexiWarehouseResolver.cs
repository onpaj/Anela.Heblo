using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal static class FlexiWarehouseResolver
{
    public static int ForProductType(ProductType type) => type switch
    {
        ProductType.Material => FlexiStockClient.MaterialWarehouseId,
        ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
        ProductType.Product or ProductType.Goods => FlexiStockClient.ProductsWarehouseId,
        _ => FlexiStockClient.MaterialWarehouseId
    };
}
