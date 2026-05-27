using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal static class ManufactureTemplateCloner
{
    public static ManufactureTemplate Clone(ManufactureTemplate source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ManufactureTemplate
        {
            TemplateId = source.TemplateId,
            ProductCode = source.ProductCode,
            ProductName = source.ProductName,
            Amount = source.Amount,
            OriginalAmount = source.OriginalAmount,
            BatchSize = source.BatchSize,
            ManufactureType = source.ManufactureType,
            Ingredients = source.Ingredients
                .Select(i => new Ingredient
                {
                    TemplateId = i.TemplateId,
                    ProductCode = i.ProductCode,
                    ProductName = i.ProductName,
                    Amount = i.Amount,
                    OriginalAmount = i.OriginalAmount,
                    Price = i.Price,
                    ProductType = i.ProductType,
                    HasLots = i.HasLots,
                    HasExpiration = i.HasExpiration,
                    Order = i.Order
                })
                .ToList()
        };
    }
}
