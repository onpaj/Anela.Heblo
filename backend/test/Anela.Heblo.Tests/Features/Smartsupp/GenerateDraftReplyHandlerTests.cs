using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GenerateDraftReplyHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<ILogger<GenerateDraftReplyHandler>> _logger = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public GenerateDraftReplyHandlerTests()
    {
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("default", "Test Agent", "test@test.com", true));
    }

    private void SetupCurrentUser(string name = "Ondřej Pajgrt") =>
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", name, "ondra@anela.cz", true));

    private GenerateDraftReplyHandler CreateHandler(SmartsuppDraftReplyOptions? options = null) =>
        new(_repo.Object, _mediator.Object, _chatClient.Object,
            Options.Create(options ?? new SmartsuppDraftReplyOptions()),
            _currentUserService.Object,
            _logger.Object);

    private static SmartsuppMessage Msg(string id, SmartsuppMessageAuthorType type, string content, int minute) =>
        new()
        {
            Id = id,
            ConversationId = "c1",
            AuthorType = type,
            Content = content,
            CreatedAt = new DateTime(2026, 5, 15, 10, minute, 0, DateTimeKind.Utc)
        };

    private static SmartsuppConversation ConversationWith(params SmartsuppMessage[] messages) =>
        new()
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            Messages = messages.ToList()
        };

    private void SetupConversation(SmartsuppConversation? conversation) =>
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);

    private void SetupSearch(params ChunkResult[] chunks) =>
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = chunks.ToList() });

    private SearchDocumentsRequest? _capturedSearch;
    private void CaptureSearch() =>
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SearchDocumentsResponse>, CancellationToken>((r, _) => _capturedSearch = (SearchDocumentsRequest)r)
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = new List<ChunkResult>() });

    private IEnumerable<ChatMessage>? _capturedChat;
    private void SetupChat(string answer = "Návrh odpovědi") =>
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => _capturedChat = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, answer)]));

    private static ChunkResult Chunk(string content, string filename) =>
        new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Content = content,
            Score = 0.9,
            SourceFilename = filename,
            SourcePath = "/" + filename
        };

    [Fact]
    public async Task Handle_ReturnsConversationNotFound_WhenConversationMissing()
    {
        SetupConversation(null);

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsConversationEmpty_WhenNoTopicAndNoContactMessage()
    {
        SetupConversation(ConversationWith(Msg("m1", SmartsuppMessageAuthorType.Agent, "Dobrý den", 1)));

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = null }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationEmpty);
    }

    [Fact]
    public async Task Handle_UsesTopicAsRetrievalQuery_WhenTopicProvided()
    {
        SetupConversation(ConversationWith(Msg("m1", SmartsuppMessageAuthorType.Agent, "Dobrý den", 1)));
        CaptureSearch();
        SetupChat();

        await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Reklamace" }, CancellationToken.None);

        _capturedSearch!.Query.Should().Be("Reklamace");
    }

    [Fact]
    public async Task Handle_FallsBackToLastContactMessages_WhenNoTopic()
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Chci vrátit zboží", 1)));
        CaptureSearch();
        SetupChat();

        await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = null }, CancellationToken.None);

        _capturedSearch!.Query.Should().Be("Chci vrátit zboží");
    }

    [Fact]
    public async Task Handle_InjectsTranscriptAndContextIntoSystemPrompt()
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Mám dotaz na reklamaci", 1)));
        SetupSearch(Chunk("Reklamaci lze uplatnit do 14 dnů.", "reklamace.pdf"));
        SetupChat();

        var options = new SmartsuppDraftReplyOptions
        {
            DraftReplySystemPrompt = "Téma: {topic}\nKontext: {context}\nPřepis: {transcript}"
        };

        await CreateHandler(options).Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Reklamace" }, CancellationToken.None);

        var systemMessage = _capturedChat!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain("Téma: Reklamace");
        systemMessage.Should().Contain("Reklamaci lze uplatnit do 14 dnů.");
        systemMessage.Should().Contain("Zákazník: Mám dotaz na reklamaci");
    }

    [Fact]
    public async Task Handle_ReturnsAnswerAndMappedSources_OnSuccess()
    {
        var chunk = Chunk("Obsah dokumentu o dopravě.", "doprava.pdf");
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(chunk);
        SetupChat("Dobrý den, balíky odesíláme do 24 hodin.");

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Answer.Should().Be("Dobrý den, balíky odesíláme do 24 hodin.");
        result.Sources.Should().ContainSingle();
        result.Sources[0].Filename.Should().Be("doprava.pdf");
        result.Sources[0].Excerpt.Should().Be("Obsah dokumentu o dopravě.");
        result.Sources[0].ChunkId.Should().Be(chunk.ChunkId);
    }

    [Fact]
    public async Task Handle_StillGenerates_WhenNoKbChunksFound()
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(); // no chunks
        SetupChat("Dobrý den, ozvu se vám s upřesněním.");

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Answer.Should().Be("Dobrý den, ozvu se vám s upřesněním.");
        result.Sources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(ObjectDisposedException))]
    public async Task Handle_ReturnsAiUnavailable_WhenChatClientThrowsTransient(Type exceptionType)
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(Chunk("obsah", "doc.pdf"));

        var exception = (Exception)Activator.CreateInstance(exceptionType, "simulated failure")!;
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // A transient cancellation/timeout from the chat client carries its own
        // (or no) token — never the caller's — so the handler treats it as a
        // service failure rather than caller-initiated cancellation.
        using var cts = new CancellationTokenSource();
        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyAiUnavailable);
    }

    [Fact]
    public async Task Handle_RethrowsCancellation_WhenCallerCancels()
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(Chunk("obsah", "doc.pdf"));

        using var cts = new CancellationTokenSource();
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("caller cancelled", null, cts.Token));

        var act = () => CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, cts.Token);

        // Caller-initiated cancellation must propagate, not be masked as a 503.
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task Handle_TruncatesRetrievalQuery_ToSearchDocumentsMaxLength()
    {
        var longMessage = new string('a', 5000);
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, longMessage, 1)));
        CaptureSearch();
        SetupChat();

        await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = null }, CancellationToken.None);

        _capturedSearch!.Query.Length.Should().Be(2000);
    }

    [Fact]
    public async Task Handle_InjectsAgentFirstNameIntoSystemPrompt()
    {
        SetupCurrentUser("Ondřej Pajgrt");
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch();
        SetupChat();

        var options = new SmartsuppDraftReplyOptions
        {
            DraftReplySystemPrompt = "Jméno: {agent_name}. Téma: {topic}. Kontext: {context}. Přepis: {transcript}"
        };

        await CreateHandler(options).Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Test" }, CancellationToken.None);

        var systemMessage = _capturedChat!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain("Jméno: Ondřej");
        systemMessage.Should().NotContain("{agent_name}");
    }

    [Fact]
    public async Task Handle_FallsBackToAnela_WhenUserNameIsUnknown()
    {
        SetupCurrentUser("Unknown User");
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch();
        SetupChat();

        var options = new SmartsuppDraftReplyOptions
        {
            DraftReplySystemPrompt = "Jméno: {agent_name}. Téma: {topic}. Kontext: {context}. Přepis: {transcript}"
        };

        await CreateHandler(options).Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Test" }, CancellationToken.None);

        var systemMessage = _capturedChat!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain("Jméno: Anela");
    }
}
