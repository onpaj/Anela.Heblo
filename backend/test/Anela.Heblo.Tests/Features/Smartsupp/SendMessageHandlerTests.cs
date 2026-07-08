using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SendMessageHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IRagInteractionLogRepository> _interactionLog = new();
    private readonly Mock<ILogger<SendMessageHandler>> _logger = new();
    private readonly SmartsuppSendMessageOptions _options = new()
    {
        AgentMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ondra@anela.cz"] = "agt-ondra",
        },
    };

    private SendMessageHandler CreateHandler() =>
        new(_repo.Object, _apiClient.Object, _currentUserService.Object,
            _interactionLog.Object, Options.Create(_options), _logger.Object);

    private void SetupConversation(bool exists = true) =>
        _repo.Setup(r => r.GetConversationAsync("conv1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists
                ? new SmartsuppConversation { Id = "conv1", Status = SmartsuppConversationStatus.Open, Messages = [] }
                : null);

    private void SetupCurrentUser(string email = "ondra@anela.cz", string name = "Ondřej Pajgrt") =>
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", name, email, true));

    private void SetupApiSuccess(string msgId = "ms123") =>
        _apiClient.Setup(c => c.SendMessageAsync(
                "conv1", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSentMessageData
            {
                Id = msgId,
                CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc)
            });

    [Fact]
    public async Task Handle_ReturnsSuccess_WithMessageId_OnHappyPath()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupApiSuccess("ms-abc");

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Dobrý den!" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("ms-abc");
    }

    [Fact]
    public async Task Handle_ResolvesAgentIdFromAgentMap_AndPassesItToApiClient()
    {
        SetupConversation();
        SetupCurrentUser(email: "ondra@anela.cz");
        SetupApiSuccess();

        await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Text" },
            CancellationToken.None);

        _apiClient.Verify(c => c.SendMessageAsync(
            "conv1", "Text", "agt-ondra", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LooksUpAgentIdCaseInsensitively()
    {
        SetupConversation();
        SetupCurrentUser(email: "ONDRA@anela.CZ");
        SetupApiSuccess();

        await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Text" },
            CancellationToken.None);

        _apiClient.Verify(c => c.SendMessageAsync(
            "conv1", "Text", "agt-ondra", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsAgentMappingNotFound_WhenCurrentUserIsNotInMap()
    {
        SetupConversation();
        SetupCurrentUser(email: "unmapped@anela.cz");

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Text" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAgentMappingNotFound);
        _apiClient.Verify(c => c.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsAgentMappingNotFound_WhenCurrentUserHasNoEmail()
    {
        SetupConversation();
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", "Anonymous", null, true));

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Text" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAgentMappingNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsConversationNotFound_WhenConversationMissing()
    {
        SetupConversation(exists: false);
        SetupCurrentUser();

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Dobrý den!" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
        _apiClient.Verify(c => c.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSendMessageUnavailable_WhenApiThrows()
    {
        SetupConversation();
        SetupCurrentUser();
        _apiClient.Setup(c => c.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error", null,
                System.Net.HttpStatusCode.ServiceUnavailable));

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Dobrý den!" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppSendMessageUnavailable);
    }

    [Fact]
    public async Task Handle_RecordsSentAnswer_WhenDraftLogIdProvided()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupApiSuccess();
        var draftLogId = Guid.NewGuid();

        var result = await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Upravený návrh", DraftLogId = draftLogId },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _interactionLog.Verify(r => r.UpdateSentAsync(
            draftLogId, "Upravený návrh", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotRecordSentAnswer_WhenNoDraftLogId()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupApiSuccess();

        await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Ručně psaná zpráva" },
            CancellationToken.None);

        _interactionLog.Verify(r => r.UpdateSentAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
