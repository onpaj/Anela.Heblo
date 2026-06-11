using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics;

public class CarrierExtensionsTests
{
    [Theory]
    [InlineData(Carriers.Zasilkovna, "Zásilkovna")]
    [InlineData(Carriers.PPL, "PPL")]
    [InlineData(Carriers.GLS, "GLS")]
    [InlineData(Carriers.Osobak, "Osobní odběr")]
    public void GetDisplayName_ReturnsCzechLabel(Carriers carrier, string expected)
    {
        carrier.GetDisplayName().Should().Be(expected);
    }
}
