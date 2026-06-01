using Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class CloseConversationHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ILogger<CloseConversationHandler>> _logger = new();

    private CloseConversationHandler CreateHandler() =>
        new(_repo.Object, _apiClient.Object, _logger.Object);

    private void SetupConversation(bool exists = true) =>
        _repo.Setup(r => r.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists
                ? new SmartsuppConversation { Id = "conv-1", Status = SmartsuppConversationStatus.Open, Messages = [] }
                : null);

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenApiCloses()
    {
        // Arrange
        SetupConversation();
        _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(
            new CloseConversationRequest { ConversationId = "conv-1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _apiClient.Verify(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsConversationNotFound_WhenConversationMissing()
    {
        // Arrange
        SetupConversation(exists: false);

        // Act
        var result = await CreateHandler().Handle(
            new CloseConversationRequest { ConversationId = "conv-1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
        _apiClient.Verify(a => a.CloseConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnavailable_WhenApiThrowsHttpRequestException()
    {
        // Arrange
        SetupConversation();
        _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable", null,
                System.Net.HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await CreateHandler().Handle(
            new CloseConversationRequest { ConversationId = "conv-1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppCloseConversationUnavailable);
    }

    [Fact]
    public async Task Handle_DoesNotReturnUnavailable_When4xxFromApi()
    {
        // A 4xx from Smartsupp indicates a contract bug, not a transient unavailability.
        // It must propagate instead of being masked as SmartsuppCloseConversationUnavailable.
        SetupConversation();
        _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp API 422", null,
                System.Net.HttpStatusCode.UnprocessableEntity));

        var act = () => CreateHandler().Handle(
            new CloseConversationRequest { ConversationId = "conv-1" },
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Handle_ReturnsUnavailable_WhenApiThrowsTimeout()
    {
        // Arrange
        SetupConversation();
        _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Request timed out"));

        // Act
        var result = await CreateHandler().Handle(
            new CloseConversationRequest { ConversationId = "conv-1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppCloseConversationUnavailable);
    }
}
