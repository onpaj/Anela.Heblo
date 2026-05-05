using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class LeafletToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<LeafletTools>> _logger = new();

    private LeafletTools CreateTools() => new(_mediator.Object, _logger.Object);

    [Fact]
    public async Task GenerateLeaflet_returns_serialized_response_on_success()
    {
        // Arrange
        var expected = new GenerateLeafletResponse { Content = "# Bisabolol\n\nVelký text..." };

        _mediator
            .Setup(m => m.Send(
                It.Is<GenerateLeafletRequest>(r =>
                    r.Topic == "Bisabolol pro citlivou pleť" &&
                    r.Audience == AudienceType.EndConsumer &&
                    r.Length == LeafletLength.Short),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await CreateTools().GenerateLeaflet(
            "Bisabolol pro citlivou pleť",
            "EndConsumer",
            "Short");

        // Assert
        var deserialized = JsonSerializer.Deserialize<GenerateLeafletResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Equal("# Bisabolol\n\nVelký text...", deserialized!.Content);
    }

    [Fact]
    public async Task GenerateLeaflet_throws_McpException_on_invalid_audience()
    {
        // Act & Assert
        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "Marketers", "Short"));
    }

    [Fact]
    public async Task GenerateLeaflet_throws_McpException_on_invalid_length()
    {
        // Act & Assert
        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "B2B", "VeryLong"));
    }

    [Fact]
    public async Task GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException()
    {
        // Arrange
        const string emptyRetrievalMessage = "No relevant documents were found for the given topic.";

        _mediator
            .Setup(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmptyRetrievalException(emptyRetrievalMessage));

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "B2B", "Medium"));

        // Assert
        Assert.Equal(emptyRetrievalMessage, exception.Message);
    }

    [Fact]
    public async Task GenerateLeaflet_wraps_unexpected_exception_with_generic_message()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("internal db crash"));

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "EndConsumer", "Long"));

        // Assert
        Assert.DoesNotContain("internal db crash", exception.Message);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
