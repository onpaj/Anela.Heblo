using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IManufactureNameBuilder
{
    string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
}

internal sealed class ManufactureNameBuilder : IManufactureNameBuilder
{
    private const int ProductCodePrefixLength = 6;
    private const int MaxManufactureNameLength = 40;

    private readonly IProductNameFormatter _nameFormatter;

    public ManufactureNameBuilder(IProductNameFormatter nameFormatter)
    {
        _nameFormatter = nameFormatter ?? throw new ArgumentNullException(nameof(nameFormatter));
    }

    public string Build(UpdateManufactureOrderDto order, ErpManufactureType type)
    {
        ArgumentNullException.ThrowIfNull(order.SemiProduct, nameof(order.SemiProduct));
        var semiCode = order.SemiProduct.ProductCode;
        var shortName = _nameFormatter.ShortProductName(order.SemiProduct.ProductName);
        var prefix = SafeTake(semiCode, ProductCodePrefixLength);

        string name;
        if (type == ErpManufactureType.Product)
        {
            if (order.Products.All(p => p.ProductCode == semiCode))
            {
                name = semiCode;
            }
            else
            {
                name = $"{prefix} {shortName}";
            }
        }
        else
        {
            name = $"{prefix}M {shortName}";
        }

        return SafeTake(name, MaxManufactureNameLength);
    }

    private static string SafeTake(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
