using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ExpeditionAddressValidatorTests
{
    private static ExpeditionAddress Complete() => new()
    {
        FullName = "Jan Novák",
        Street = "Hlavní",
        HouseNumber = "12",
        City = "Praha",
        Zip = "11000",
    };

    [Fact]
    public void GetMissingFields_CompleteAddress_ReturnsEmpty()
    {
        ExpeditionAddressValidator.GetMissingFields(Complete()).Should().BeEmpty();
    }

    [Fact]
    public void GetMissingFields_NullAddress_ReturnsAllFiveFields()
    {
        ExpeditionAddressValidator.GetMissingFields(null)
            .Should().BeEquivalentTo("jméno příjemce", "ulice", "číslo popisné", "město", "PSČ");
    }

    [Fact]
    public void GetMissingFields_BlankZip_ReturnsZipOnly()
    {
        var addr = Complete();
        addr.Zip = "   ";
        ExpeditionAddressValidator.GetMissingFields(addr)
            .Should().ContainSingle().Which.Should().Be("PSČ");
    }

    [Fact]
    public void GetMissingFields_CompanyOnlyName_IsValid()
    {
        var addr = Complete();
        addr.FullName = null;
        addr.Company = "Anela s.r.o.";
        ExpeditionAddressValidator.GetMissingFields(addr).Should().BeEmpty();
    }

    [Fact]
    public void GetMissingFields_MissingStreetAndCity_ReturnsBoth()
    {
        var addr = Complete();
        addr.Street = null;
        addr.City = "";
        ExpeditionAddressValidator.GetMissingFields(addr)
            .Should().BeEquivalentTo("ulice", "město");
    }
}
