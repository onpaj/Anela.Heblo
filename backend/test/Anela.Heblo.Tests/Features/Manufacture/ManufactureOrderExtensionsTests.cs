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

    [Theory]
    [InlineData(2024, 1, 1, "01202401")]
    [InlineData(2024, 5, 29, "22202405")]
    [InlineData(2024, 12, 30, "01202412")] // note: calendar year used, not ISO year — locks current behavior (NFR-4)
    [InlineData(2024, 12, 29, "52202412")] // note: Sunday — exercises dayNum == 0 → 7 branch
    [InlineData(2020, 12, 31, "53202012")] // note: ISO week 53 case
    [InlineData(2024, 2, 29, "09202402")]
    [InlineData(2024, 9, 15, "37202409")]
    public void GetDefaultLot_ReturnsExpectedString_ForRepresentativeDates(int year, int month, int day, string expected)
    {
        // Arrange
        var manufactureDate = new DateTime(year, month, day);

        // Act
        var result = ManufactureOrderExtensions.GetDefaultLot(manufactureDate);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(2024, 1, 1)]
    [InlineData(2024, 5, 29)]
    [InlineData(2024, 12, 30)]
    [InlineData(2024, 12, 29)]
    [InlineData(2020, 12, 31)]
    [InlineData(2024, 2, 29)]
    [InlineData(2024, 9, 15)]
    public void GetDefaultLot_ReturnsEightDigitString_ForRepresentativeDates(int year, int month, int day)
    {
        // Arrange
        var manufactureDate = new DateTime(year, month, day);

        // Act
        var result = ManufactureOrderExtensions.GetDefaultLot(manufactureDate);

        // Assert
        result.Should().HaveLength(8);
        result.Should().MatchRegex("^[0-9]{8}$");
        result.Substring(2, 4).Should().Be(year.ToString());
        result.Substring(6, 2).Should().Be(month.ToString("D2"));
    }
}
