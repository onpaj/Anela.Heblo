using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderExtensionsTests
{
    // ── GetDefaultLot ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2024, 1, 1, "01202401")] // Monday, week 1 — tests week and month zero-padding
    [InlineData(2024, 6, 3, "23202406")] // Monday, week 23 — mid-year
    [InlineData(2023, 12, 31, "52202312")] // Sunday, week 52 — year-end boundary
    [InlineData(2024, 12, 30, "01202412")] // Monday, ISO week 1 of next year but calendar year stays 2024
    [InlineData(2024, 1, 7, "01202401")] // Sunday, still week 1 (same Thursday as Jan 4)
    public void GetDefaultLot_ReturnsExpectedWwYyyyMmString(
        int year, int month, int day, string expected)
    {
        var result = ManufactureOrderExtensions.GetDefaultLot(new DateTime(year, month, day));

        result.Should().Be(expected);
    }

    // ── GetDefaultExpiration ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2024, 1, 15, 12, 2025, 2, 28)]  // normal — last day of month after +12
    [InlineData(2024, 11, 15, 1, 2025, 1, 31)]  // year-boundary: Dec → Jan
    [InlineData(2023, 1, 15, 1, 2023, 3, 28)]   // non-leap February
    [InlineData(2024, 1, 15, 1, 2024, 3, 29)]   // leap-year February (2024-02-29 + 1 month)
    [InlineData(2024, 2, 15, 1, 2024, 4, 30)]   // 31-day month Mar → Apr (30 days)
    public void GetDefaultExpiration_ReturnsLastDayOfCorrectMonth(
        int mYear, int mMonth, int mDay, int months,
        int eYear, int eMonth, int eDay)
    {
        var manufactureDate = new DateTime(mYear, mMonth, mDay);
        var expected = new DateOnly(eYear, eMonth, eDay);

        var result = ManufactureOrderExtensions.GetDefaultExpiration(manufactureDate, months);

        result.Should().Be(expected);
    }

    // ── SetDefaultLot — entity write-back ─────────────────────────────────────

    [Fact]
    public void SetDefaultLot_OnSemiProduct_WritesLotNumber()
    {
        var semiProduct = new ManufactureOrderSemiProduct();
        var date = new DateTime(2024, 5, 6); // Monday, week 19

        semiProduct.SetDefaultLot(date);

        semiProduct.LotNumber.Should().Be(ManufactureOrderExtensions.GetDefaultLot(date));
    }

    [Fact]
    public void SetDefaultLot_OnProduct_WritesLotNumber()
    {
        var product = new ManufactureOrderProduct();
        var date = new DateTime(2024, 5, 6);

        product.SetDefaultLot(date);

        product.LotNumber.Should().Be(ManufactureOrderExtensions.GetDefaultLot(date));
    }

    [Fact]
    public void SetDefaultLot_OnOrder_WritesSemiProductAndAllProducts()
    {
        var order = new ManufactureOrder
        {
            SemiProduct = new ManufactureOrderSemiProduct(),
            Products = new List<ManufactureOrderProduct>
            {
                new ManufactureOrderProduct(),
                new ManufactureOrderProduct()
            }
        };
        var date = new DateTime(2024, 5, 6);
        var expected = ManufactureOrderExtensions.GetDefaultLot(date);

        order.SetDefaultLot(date);

        order.SemiProduct.LotNumber.Should().Be(expected);
        order.Products.Should().AllSatisfy(p => p.LotNumber.Should().Be(expected));
    }

    // ── SetDefaultExpiration — entity write-back ──────────────────────────────

    [Fact]
    public void SetDefaultExpiration_OnSemiProduct_WritesExpirationDate()
    {
        var semiProduct = new ManufactureOrderSemiProduct { ExpirationMonths = 12 };
        var date = new DateTime(2024, 1, 15);

        semiProduct.SetDefaultExpiration(date);

        semiProduct.ExpirationDate.Should().Be(
            ManufactureOrderExtensions.GetDefaultExpiration(date, 12));
    }

    [Fact]
    public void SetDefaultExpiration_OnProduct_WritesExpirationDate()
    {
        var product = new ManufactureOrderProduct();
        var date = new DateTime(2024, 1, 15);

        product.SetDefaultExpiration(date, expirationMonths: 12);

        product.ExpirationDate.Should().Be(
            ManufactureOrderExtensions.GetDefaultExpiration(date, 12));
    }

    [Fact]
    public void SetDefaultExpiration_OnOrder_WritesSemiProductAndAllProducts()
    {
        var order = new ManufactureOrder
        {
            SemiProduct = new ManufactureOrderSemiProduct { ExpirationMonths = 24 },
            Products = new List<ManufactureOrderProduct>
            {
                new ManufactureOrderProduct(),
                new ManufactureOrderProduct()
            }
        };
        var date = new DateTime(2024, 3, 10);
        var expected = ManufactureOrderExtensions.GetDefaultExpiration(date, 24);

        order.SetDefaultExpiration(date);

        order.SemiProduct.ExpirationDate.Should().Be(expected);
        order.Products.Should().AllSatisfy(p => p.ExpirationDate.Should().Be(expected));
    }
}
