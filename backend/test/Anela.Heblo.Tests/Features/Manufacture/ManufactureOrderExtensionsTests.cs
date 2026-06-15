using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderExtensionsTests
{
    private static ManufactureOrder MakeOrder(int semiProductExpirationMonths, int productCount = 2)
    {
        var products = new List<ManufactureOrderProduct>();
        for (var i = 0; i < productCount; i++)
        {
            products.Add(new ManufactureOrderProduct
            {
                ProductCode = $"P{i}",
                ProductName = $"Product {i}",
                SemiProductCode = "SP"
            });
        }

        return new ManufactureOrder
        {
            OrderNumber = "",
            CreatedByUser = "",
            StateChangedByUser = "",
            SemiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = "SP",
                ProductName = "SemiProduct",
                ExpirationMonths = semiProductExpirationMonths
            },
            Products = products
        };
    }
}
