using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderExtensionsTests
{
    // ── GetDefaultLot ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2024, 1, 1, "0124")] // Monday, week 1 — tests week zero-padding
    [InlineData(2024, 6, 3, "2324")] // Monday, week 23 — mid-year
    [InlineData(2023, 12, 31, "5223")] // Sunday, week 52 — year-end boundary
    [InlineData(2024, 12, 30, "0124")] // Monday, ISO week 1 of next year but calendar year stays 2024
    [InlineData(2024, 1, 7, "0124")] // Sunday, still week 1 (same Thursday as Jan 4)
    public void GetDefaultLot_ReturnsExpectedWwYyString(
        int year, int month, int day, string expected)
    {
        var result = ManufactureOrderExtensions.GetDefaultLot(new DateTime(year, month, day));

        result.Should().Be(expected);
    }

    // ── GetDefaultExpiration ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2024, 1, 15, 12, 2025, 2, 28)]  // normal — last day of month after +12
    [InlineData(2024, 11, 15, 1, 2025, 1, 31)]  // year-boundary: Dec → Jan
    [InlineData(2023, 1, 15, 1, 2023, 3, 31)]   // passes through non-leap Feb — used to clamp to Mar 28
    [InlineData(2024, 1, 15, 1, 2024, 3, 31)]   // passes through leap Feb — used to clamp to Mar 29
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

    // ── GetDefaultExpiration (DateOnly, planned-date anchored) ────────────────

    [Theory]
    [InlineData(2026, 11, 2, 24, 2028, 12, 31)]  // MO-2026-493/494: planned 2 Nov 2026, 24 months
    [InlineData(2026, 10, 30, 30, 2029, 5, 31)]  // 30-day source month must not clamp May to the 30th
    [InlineData(2026, 3, 31, 1, 2026, 5, 31)]    // 31st of month — no day overflow/clamp
    [InlineData(2026, 11, 30, 0, 2026, 12, 31)]  // Nov 30 + 1 → Dec 31, not Dec 30
    [InlineData(2026, 1, 31, 0, 2026, 2, 28)]    // lands in non-leap February
    [InlineData(2027, 12, 15, 1, 2028, 2, 29)]   // lands in leap February
    [InlineData(2026, 12, 1, 12, 2028, 1, 31)]   // year boundary
    public void GetDefaultExpiration_FromPlannedDate_ReturnsLastDayOfPlannedMonthPlusMonthsPlusOne(
        int pYear, int pMonth, int pDay, int months,
        int eYear, int eMonth, int eDay)
    {
        var result = ManufactureOrderExtensions.GetDefaultExpiration(new DateOnly(pYear, pMonth, pDay), months);

        result.Should().Be(new DateOnly(eYear, eMonth, eDay));
    }

    [Theory]
    [InlineData(2026, 11, 2, "4526")]
    [InlineData(2027, 1, 1, "5327")] // ISO week 53 of 2026, calendar year 2027
    public void GetDefaultLot_FromPlannedDate_ReturnsIsoWeekAndTwoDigitYear(
        int year, int month, int day, string expected)
    {
        var result = ManufactureOrderExtensions.GetDefaultLot(new DateOnly(year, month, day));

        result.Should().Be(expected);
    }

    // ── ApplyDefaultLotAndExpiration / CanRecomputeLotAndExpiration ───────────

    [Fact]
    public void ApplyDefaultLotAndExpiration_UsesPlannedDate_AndPropagatesToProducts()
    {
        var order = new ManufactureOrder
        {
            PlannedDate = new DateOnly(2026, 11, 2),
            SemiProduct = new ManufactureOrderSemiProduct { ExpirationMonths = 24 },
        };
        order.Products.Add(new ManufactureOrderProduct());
        order.Products.Add(new ManufactureOrderProduct());

        order.ApplyDefaultLotAndExpiration();

        order.SemiProduct!.LotNumber.Should().Be("4526");
        order.SemiProduct.ExpirationDate.Should().Be(new DateOnly(2028, 12, 31));
        order.Products.Should().AllSatisfy(p =>
        {
            p.LotNumber.Should().Be("4526");
            p.ExpirationDate.Should().Be(new DateOnly(2028, 12, 31));
        });
    }

    [Fact]
    public void ApplyDefaultLotAndExpiration_WithoutSemiProduct_DoesNothing()
    {
        var order = new ManufactureOrder { PlannedDate = new DateOnly(2026, 11, 2), SemiProduct = null };
        order.Products.Add(new ManufactureOrderProduct { LotNumber = "keep" });

        order.ApplyDefaultLotAndExpiration();

        order.Products.Single().LotNumber.Should().Be("keep");
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, true)]
    [InlineData(ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Cancelled, false)]
    public void CanRecomputeLotAndExpiration_OnlyBeforeSemiProductIsConfirmed(
        ManufactureOrderState state, bool expected)
    {
        var order = new ManufactureOrder();
        order.InitializeState(state, new DateTime(2026, 11, 1), "test");

        order.CanRecomputeLotAndExpiration().Should().Be(expected);
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
