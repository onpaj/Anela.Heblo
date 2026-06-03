using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Settings;

public class GetManufactureSettingsHandlerTests
{
    private readonly Mock<ILogger<GetManufactureSettingsHandler>> _loggerMock = new();

    [Fact]
    public async Task Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse()
    {
        // Arrange
        var configuredGroupId = "11111111-2222-3333-4444-555555555555";
        var configDict = new Dictionary<string, string?>
        {
            { ManufactureConfigurationKeys.GroupId, configuredGroupId }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        var handler = new GetManufactureSettingsHandler(config, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().Be(configuredGroupId);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdMissing_ReturnsNull()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var handler = new GetManufactureSettingsHandler(config, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdEmpty_ReturnsNull()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { ManufactureConfigurationKeys.GroupId, string.Empty }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        var handler = new GetManufactureSettingsHandler(config, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }
}
