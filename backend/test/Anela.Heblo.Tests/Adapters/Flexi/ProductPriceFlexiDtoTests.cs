using Anela.Heblo.Adapters.Flexi.Price;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

/// <summary>
/// Pins the <c>typszbdphk</c> vocabulary mapping. Flexi user query 41's definition is not
/// visible from here, so both the enum vocabulary the rest of this adapter uses
/// (<c>typSzbDph.dphZakl</c>, see FlexiInvoiceMappingProfile) and the Czech label vocabulary
/// the original switch was written against must both be recognised — and anything else must
/// come back as "not recognised" rather than as a fabricated 21.
/// </summary>
public class ProductPriceFlexiDtoTests
{
    private static ProductPriceFlexiDto Dto(string vatLevel) => new()
    {
        ProductId = 1,
        ProductCode = "A",
        Price = 100m,
        PurchasePrice = 0m,
        VatLevel = vatLevel,
        ProductType = "Zboží",
    };

    [Theory]
    [InlineData("typSzbDph.dphZakl", 21)]
    [InlineData("dphZakl", 21)]
    [InlineData("základní", 21)]
    [InlineData("zakladni", 21)]
    [InlineData("typSzbDph.dphSniz", 12)]
    [InlineData("dphSniz", 12)]
    [InlineData("snížená", 12)]
    [InlineData("snizena", 12)]
    [InlineData("typSzbDph.dphSniz2", 10)]
    [InlineData("dphSniz2", 10)]
    [InlineData("druhá snížená", 10)]
    [InlineData("druha snizena", 10)]
    [InlineData("typSzbDph.dphOsv", 0)]
    [InlineData("dphOsv", 0)]
    [InlineData("osvobozeno", 0)]
    public void recognises_both_vocabularies(string vatLevel, int expectedRate)
    {
        // Act & Assert
        Dto(vatLevel).VatRate.Should().Be(expectedRate);
    }

    [Theory]
    [InlineData("  DPHZAKL  ", 21)]
    [InlineData("TYPSZBDPH.DPHSNIZ2", 10)]
    [InlineData(" Snížená ", 12)]
    public void matches_case_insensitively_and_trims(string vatLevel, int expectedRate)
    {
        // Act & Assert
        Dto(vatLevel).VatRate.Should().Be(expectedRate);
    }

    [Theory]
    [InlineData("ovobozeno")] // the original typo — never a real Flexi value
    [InlineData("dphSniz3")]
    [InlineData("")]
    [InlineData("   ")]
    public void reports_an_unrecognised_band_as_null_rather_than_guessing(string vatLevel)
    {
        // Act & Assert
        Dto(vatLevel).VatRate.Should().BeNull();
    }

    [Fact]
    public void read_path_vat_still_falls_back_to_21_for_an_unrecognised_band()
    {
        // The comparison screen must behave exactly as it does today; only the write path
        // is allowed to refuse.
        Dto("dphSniz3").Vat.Should().Be(21m);
    }

    [Fact]
    public void read_path_vat_uses_the_recognised_band_when_there_is_one()
    {
        Dto("dphSniz").Vat.Should().Be(12m);
    }
}
