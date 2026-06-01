using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Configuration;

public class GetConfigurationHandlerTests
{
    private readonly Mock<IHostEnvironment> _environmentMock = new();
    private readonly Mock<ILogger<GetConfigurationHandler>> _loggerMock = new();

    [Fact]
    public async Task Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse()
    {
        // Arrange
        var configuredGroupId = "11111111-2222-3333-4444-555555555555";
        _environmentMock.SetupGet(e => e.EnvironmentName).Returns("Test");

        var configDict = new Dictionary<string, string>
        {
            { "ManufactureGroupId", configuredGroupId },
            { ConfigurationConstants.USE_MOCK_AUTH, "false" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var handler = new GetConfigurationHandler(config, _environmentMock.Object, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().Be(configuredGroupId);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdMissing_ReturnsNull()
    {
        // Arrange
        _environmentMock.SetupGet(e => e.EnvironmentName).Returns("Test");

        var configDict = new Dictionary<string, string>
        {
            { ConfigurationConstants.USE_MOCK_AUTH, "false" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var handler = new GetConfigurationHandler(config, _environmentMock.Object, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdEmpty_ReturnsNull()
    {
        // Arrange
        _environmentMock.SetupGet(e => e.EnvironmentName).Returns("Test");

        var configDict = new Dictionary<string, string>
        {
            { "ManufactureGroupId", string.Empty },
            { ConfigurationConstants.USE_MOCK_AUTH, "false" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var handler = new GetConfigurationHandler(config, _environmentMock.Object, _loggerMock.Object);

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().BeNull();
    }
}
