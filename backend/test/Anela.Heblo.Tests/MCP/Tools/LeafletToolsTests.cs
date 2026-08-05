using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class LeafletToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Marketing_Leaflet, AccessLevel.Read);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<LeafletTools>> _logger = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public LeafletToolsTests()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(true);
    }

    private LeafletTools CreateTools() => new(_mediator.Object, _logger.Object, _currentUserService.Object);

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
        var deserialized = JsonSerializer.Deserialize<GenerateLeafletResponse>(result, McpJsonOptions.Default);
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
    public async Task GenerateLeaflet_throws_McpException_on_LeafletEmptyRetrieval_response()
    {
        // Arrange
        var errorResponse = new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval,
            new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } });

        _mediator
            .Setup(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "B2B", "Medium"));

        // Assert
        Assert.Equal("Knowledge Base does not yet cover this topic; try a broader phrasing", exception.Message);
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

    [Fact]
    public async Task GenerateLeaflet_throws_Forbidden_AndSkipsMediator_WhenUserLacksReadRole()
    {
        // Arrange
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().GenerateLeaflet("Some topic", "EndConsumer", "Short"));

        // Assert
        Assert.Contains("FORBIDDEN", exception.Message);
        Assert.Contains(ReadRole, exception.Message);
        _mediator.Verify(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
