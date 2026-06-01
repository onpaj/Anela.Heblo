using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class CarrierCoolingSettingTests
{
    [Fact]
    public void Constructor_StoresCoolingText_WhenProvided()
    {
        var setting = new CarrierCoolingSetting(
            Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1", "MRAZ");

        setting.CoolingText.Should().Be("MRAZ");
    }

    [Fact]
    public void Constructor_DefaultsCoolingTextToNull_WhenOmitted()
    {
        var setting = new CarrierCoolingSetting(
            Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1");

        setting.CoolingText.Should().BeNull();
    }
}
