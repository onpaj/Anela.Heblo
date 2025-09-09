using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureRepository : IManufactureRepository
{
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;

    public FlexiManufactureRepository(
        IBoMClient bomClient,
        IProductSetsClient productSetsClient
        )
    {
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
    }

    public async Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        var bom = await _bomClient.GetAsync(id, cancellationToken);

        var header = bom.SingleOrDefault(s => s.Level == 1) ?? throw new ApplicationException(message: $"No BoM header for product {id} found");
        var ingredients = bom.Where(w => w.Level != 1);

        return new ManufactureTemplate()
        {
            TemplateId = header.Id,
            ProductCode = header.IngredientCode.RemoveCodePrefix(),
            ProductName = header.IngredientFullName,
            Amount = header.Amount,
            OriginalAmount = header.Amount,
            Ingredients = ingredients.Select(s =>
            {
                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = s.IngredientCode.RemoveCodePrefix(),
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                };
            }).ToList(),
        };
    }

    public async Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken)
    {
        var templates = await _bomClient.GetByIngredientAsync(ingredientCode, cancellationToken);

        return templates
                .Select(s => new ManufactureTemplate()
                {
                    ProductCode = s.ParentCode!.RemoveCodePrefix(),
                    ProductName = s.ParentFullName!,
                    Amount = s.Amount,
                    TemplateId = s.Id,
                    BatchSize = s.Parent?.Amount ?? 0,
                })
        .Where(w => w.ProductCode != ingredientCode)
        .ToList();
    }

    public async Task<List<ProductPart>> GetSetParts(string setProductCode, CancellationToken cancellationToken = default)
    {
        var setParts = await _productSetsClient.GetAsync(setProductCode, cancellationToken);
        
        return setParts
            .Select(s => new ProductPart()
            {
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                Amount = s.Quantity,
            })
            .ToList();
    }
}