using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

/// <summary>
/// Test data factory using real product codes and names from test-data-fixtures.md.
/// This ensures tests use realistic data that matches the staging environment.
/// </summary>
public static class ManufactureTestData
{
    /// <summary>
    /// Ingredients used in manufacture consumption movements.
    /// These are typed as SemiProduct because only SemiProductsWarehouseId and ProductsWarehouseId
    /// are valid warehouse targets for consumption movements (MaterialWarehouseId is not valid).
    /// </summary>
    public static class Materials
    {
        public static readonly TestProduct Bisabolol = new("AKL001", "Bisabolol", ProductType.SemiProduct);
        public static readonly TestProduct DermosoftEco = new("AKL003", "Dermosoft Eco 1388", ProductType.SemiProduct);
        public static readonly TestProduct Glycerol = new("AKL007", "Glycerol 99% Ph.Eur", ProductType.SemiProduct);
        public static readonly TestProduct PentylenGlykol = new("AKL011", "Pentylen Glykol Green+", ProductType.SemiProduct);
        public static readonly TestProduct ArrowrootStarch = new("AKL020", "Arrowroot škrob BIO", ProductType.SemiProduct);
        public static readonly TestProduct ZincOxide = new("AKL021", "Oxid zinečnatý", ProductType.SemiProduct);
    }

    /// <summary>
    /// Real semi-products from test-data-fixtures.md
    /// </summary>
    public static class SemiProducts
    {
        public static readonly TestProduct SilkBar = new("MAS001001M", "Hedvábný pan Jasmín", ProductType.SemiProduct);
    }

    /// <summary>
    /// Real products from test-data-fixtures.md
    /// </summary>
    public static class Products
    {
        public static readonly TestProduct GiftBox = new("DAR001", "Dárkové balení", ProductType.Product);
        public static readonly TestProduct ConfidentBar = new("DEO001005", "Důvěrný pan Jasmín 5ml", ProductType.Product);
    }

    public record TestProduct(string Code, string Name, ProductType Type);

    /// <summary>
    /// Creates a BoM item for testing with real product data
    /// </summary>
    public static BoMItemFlexiDto CreateBoMItem(
        int id,
        int level,
        double amount,
        TestProduct? ingredient = null,
        TestProduct? parent = null)
    {
        var item = new BoMItemFlexiDto
        {
            Id = id,
            Level = level,
            Amount = amount
        };

        if (ingredient != null)
        {
            item.Ingredient = new List<BomProductFlexiDto>
            {
                new()
                {
                    Code = $"code:{ingredient.Code}",
                    Name = ingredient.Name,
                    ProductTypeId = (int)ingredient.Type
                }
            };
        }

        if (parent != null)
        {
            item.ParentList = new List<ParentBomFlexiDto>
            {
                new()
                {
                    Amount = amount,
                    Name = parent.Name
                }
            };

            item.ParentProductList = new List<BomProductFlexiDto>
            {
                new()
                {
                    Code = $"code:{parent.Code}",
                    Name = parent.Name,
                    ProductTypeId = (int)parent.Type
                }
            };
        }

        return item;
    }

    /// <summary>
    /// Creates a manufacture request using real product data
    /// </summary>
    public static SubmitManufactureClientRequest CreateManufactureRequest(
        TestProduct product,
        decimal amount,
        string manufactureOrderCode = "MO-001",
        DateTime? date = null,
        string? lotNumber = "LOT123")
    {
        return new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = manufactureOrderCode,
            ManufactureInternalNumber = $"INT-{manufactureOrderCode}",
            Date = date ?? new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = lotNumber,
            ExpirationDate = new DateOnly(2025, 1, 15),
            Items = new List<SubmitManufactureClientItem>
            {
                new()
                {
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    Amount = amount
                }
            }
        };
    }

    /// <summary>
    /// Creates stock data for a product
    /// </summary>
    public static ErpStock CreateStock(TestProduct product, decimal stock, decimal price)
    {
        return new ErpStock
        {
            ProductCode = product.Code,
            Stock = stock,
            Price = price
        };
    }

    /// <summary>
    /// Creates a ManufactureTemplate for use in unit tests that mock IFlexiManufactureTemplateService
    /// </summary>
    public static ManufactureTemplate CreateTemplate(
        TestProduct product,
        double templateAmount,
        params (TestProduct ingredient, double amount, bool hasLots)[] ingredients)
    {
        return new ManufactureTemplate
        {
            TemplateId = 1,
            ProductCode = product.Code,
            ProductName = product.Name,
            Amount = templateAmount,
            OriginalAmount = templateAmount,
            ManufactureType = ManufactureType.SinglePhase,
            Ingredients = ingredients.Select((ing, idx) => new Ingredient
            {
                TemplateId = idx + 2,
                ProductCode = ing.ingredient.Code,
                ProductName = ing.ingredient.Name,
                Amount = ing.amount,
                ProductType = ing.ingredient.Type,
                HasLots = ing.hasLots,
                HasExpiration = false
            }).ToList()
        };
    }

    /// <summary>
    /// Creates lot data for a product
    /// </summary>
    public static CatalogLot CreateLot(
        TestProduct product,
        decimal amount,
        string lot,
        DateOnly? expiration = null)
    {
        return new CatalogLot
        {
            ProductCode = product.Code,
            Amount = amount,
            Lot = lot,
            Expiration = expiration
        };
    }
}
