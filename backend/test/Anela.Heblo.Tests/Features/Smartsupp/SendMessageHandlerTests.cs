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

    // ---- attachments ----

    private static SendMessageFile File(string name = "a.png", int size = 10, string? type = null) =>
        new() { FileName = name, ContentType = type, Content = new byte[size] };

    private static SendMessageRequest FilesRequest(string? content, params SendMessageFile[] files) =>
        new() { ConversationId = "conv1", Content = content, Files = files.ToList() };

    private void SetupAttachmentApi()
    {
        var n = 0;
        _apiClient.Setup(c => c.GetUploadTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                n++;
                return new SmartsuppUploadToken { Token = $"tok{n}", Url = $"https://files.smartsupp.com/u{n}" };
            });
        _apiClient.Setup(c => c.SendMessageAsync(
                "conv1", It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<SmartsuppOutgoingAttachment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSentMessageData
            {
                Id = "ms-att",
                CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
            });
    }

    private void VerifyNoApiCalls()
    {
        _apiClient.Verify(c => c.GetUploadTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        _apiClient.Verify(c => c.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _apiClient.Verify(c => c.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<SmartsuppOutgoingAttachment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithFiles_UploadsEachThenSendsOneMessageWithAllTokens()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupAttachmentApi();

        var result = await CreateHandler().Handle(
            FilesRequest("Text", File("a.png"), File("b.pdf")), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("ms-att");
        _apiClient.Verify(c => c.GetUploadTokenAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _apiClient.Verify(c => c.UploadFileAsync(
            "https://files.smartsupp.com/u1", "a.png", "image/png", It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.UploadFileAsync(
            "https://files.smartsupp.com/u2", "b.pdf", "application/pdf", It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.SendMessageAsync(
            "conv1", "Text", "agt-ondra",
            It.Is<IReadOnlyList<SmartsuppOutgoingAttachment>>(a =>
                a.Count == 2 && a[0].Token == "tok1" && a[1].Token == "tok2"
                && a[0].FileName == "a.png" && a[0].FileType == "image/png"
                && a[1].FileName == "b.pdf" && a[1].FileType == "application/pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithFilesAndNoText_SendsAttachmentOnlyMessage()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupAttachmentApi();

        var result = await CreateHandler().Handle(FilesRequest(null, File()), CancellationToken.None);

        result.Success.Should().BeTrue();
        _apiClient.Verify(c => c.SendMessageAsync(
            "conv1", null, "agt-ondra",
            It.Is<IReadOnlyList<SmartsuppOutgoingAttachment>>(a => a.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(System.Text.Json.JsonException))]
    public async Task Handle_WhenAnUploadFails_SendsNothing_ReturnsAttachmentUploadFailed(Type exceptionType)
    {
        SetupConversation();
        SetupCurrentUser();
        SetupAttachmentApi();
        _apiClient.Setup(c => c.UploadFileAsync(
                "https://files.smartsupp.com/u2", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "boom")!);

        var result = await CreateHandler().Handle(
            FilesRequest("Text", File("a.png"), File("b.png")), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentUploadFailed);
        _apiClient.Verify(c => c.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<SmartsuppOutgoingAttachment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsSixFiles_WithAttachmentInvalid()
    {
        SetupConversation();
        SetupCurrentUser();
        var files = Enumerable.Range(0, 6).Select(i => File($"f{i}.png")).ToArray();

        var result = await CreateHandler().Handle(FilesRequest("Text", files), CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentInvalid);
        VerifyNoApiCalls();
    }

    [Fact]
    public async Task Handle_RejectsDisallowedExtension_WithAttachmentInvalid()
    {
        SetupConversation();
        SetupCurrentUser();

        var result = await CreateHandler().Handle(
            FilesRequest("Text", File("a.png"), File("evil.exe")), CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentInvalid);
        VerifyNoApiCalls();
    }

    [Fact]
    public async Task Handle_RejectsEmptyFile_WithAttachmentInvalid()
    {
        SetupConversation();
        SetupCurrentUser();

        var result = await CreateHandler().Handle(
            FilesRequest("Text", File("a.png", size: 0)), CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentInvalid);
        VerifyNoApiCalls();
    }

    [Fact]
    public async Task Handle_RejectsFileOver10MB_WithAttachmentTooLarge()
    {
        SetupConversation();
        SetupCurrentUser();

        var result = await CreateHandler().Handle(
            FilesRequest("Text", File("a.png", size: (int)SmartsuppAttachmentPolicy.MaxFileBytes + 1)),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentTooLarge);
        VerifyNoApiCalls();
    }

    [Fact]
    public async Task Handle_WithFiles_ChecksConversationAndAgentMappingFirst()
    {
        SetupConversation(exists: false);
        SetupCurrentUser();

        var result = await CreateHandler().Handle(FilesRequest("Text", File()), CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
        VerifyNoApiCalls();
    }

    [Fact]
    public async Task Handle_WithFiles_RecordsDraftSentText()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupAttachmentApi();
        var draftLogId = Guid.NewGuid();
        var request = FilesRequest("Upravený návrh", File());
        request.DraftLogId = draftLogId;

        await CreateHandler().Handle(request, CancellationToken.None);

        _interactionLog.Verify(r => r.UpdateSentAsync(
            draftLogId, "Upravený návrh", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAttachmentSendFails_ReturnsSendMessageUnavailable()
    {
        SetupConversation();
        SetupCurrentUser();
        SetupAttachmentApi();
        _apiClient.Setup(c => c.SendMessageAsync(
                "conv1", It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<SmartsuppOutgoingAttachment>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var result = await CreateHandler().Handle(FilesRequest("Text", File()), CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppSendMessageUnavailable);
    }
}
