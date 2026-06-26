using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Settings;

public class GetManufactureSettingsHandlerTests
{
    private readonly Mock<ILogger<GetManufactureSettingsHandler>> _loggerMock = new();

    private GetManufactureSettingsHandler CreateHandler(string? manufactureGroupId)
    {
        var options = Options.Create(new ManufactureErpOptions
        {
            ManufactureGroupId = manufactureGroupId
        });
        return new GetManufactureSettingsHandler(options, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse()
    {
        // Arrange
        var configuredGroupId = "11111111-2222-3333-4444-555555555555";
        var handler = CreateHandler(configuredGroupId);

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
        var handler = CreateHandler(manufactureGroupId: null);

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
        var handler = CreateHandler(manufactureGroupId: string.Empty);

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdWhitespace_ReturnsNull()
    {
        // Arrange
        var handler = CreateHandler(manufactureGroupId: "   ");

        // Act
        var response = await handler.Handle(new GetManufactureSettingsRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public void Constructor_WhenOptionsNull_ThrowsArgumentNullException()
    {
        // Arrange
        Action act = () => new GetManufactureSettingsHandler(null!, _loggerMock.Object);

        // Act / Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_WhenLoggerNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(new ManufactureErpOptions());
        Action act = () => new GetManufactureSettingsHandler(options, null!);

        // Act / Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
