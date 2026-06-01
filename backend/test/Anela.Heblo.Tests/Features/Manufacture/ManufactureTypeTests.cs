using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureTypeTests
{
    [Fact]
    public void ManufactureType_ShouldHaveCorrectValues()
    {
        // Arrange & Act & Assert
        ((int)ManufactureType.MultiPhase).Should().Be(0, "MultiPhase should be 0 for backward compatibility");
        ((int)ManufactureType.SinglePhase).Should().Be(1, "SinglePhase should be 1");
    }

    [Fact]
    public void ManufactureType_DefaultValue_ShouldBeMultiPhase()
    {
        // Arrange
        var order = new ManufactureOrder();

        // Act & Assert
        order.ManufactureType.Should().Be(ManufactureType.MultiPhase, "Default should be MultiPhase for backward compatibility");
    }
}