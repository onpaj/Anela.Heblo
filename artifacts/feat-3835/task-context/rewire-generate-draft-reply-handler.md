### task: rewire-generate-draft-reply-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` (full file, 128 lines)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` (full file, 325 lines)

This task depends on `ISmartsuppKnowledgeSource` and `KnowledgeBaseSmartsuppKnowledgeSource` already existing (previous task). It swaps `GenerateDraftReplyHandler`'s dependency on `IMediator`/`SearchDocumentsRequest` for the new `ISmartsuppKnowledgeSource`, and updates the existing unit tests to mock the new interface instead of `IMediator`. No new test scenarios are added — every existing assertion is preserved, only the mock surface changes.

- [ ] **Step 1: Update the test file to target the new contract (this will not compile yet)**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GenerateDraftReplyHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppKnowledgeSource> _knowledgeSource = new();
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<ILogger<GenerateDraftReplyHandler>> _logger = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly RagInteractionRecorder _recorder = new();

    public GenerateDraftReplyHandlerTests()
    {
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("default", "Test Agent", "test@test.com", true));
    }

    private void SetupCurrentUser(string name = "Ondřej Pajgrt") =>
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", name, "ondra@anela.cz", true));

    private GenerateDraftReplyHandler CreateHandler(SmartsuppDraftReplyOptions? options = null) =>
        new(_repo.Object, _knowledgeSource.Object, _chatClient.Object,
            Options.Create(options ?? new SmartsuppDraftReplyOptions()),
            _currentUserService.Object,
            _recorder,
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

    private void SetupSearch(params SmartsuppKnowledgeChunk[] chunks) =>
        _knowledgeSource.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks.ToList());

    private string? _capturedQuery;
    private int? _capturedTopK;
    private void CaptureSearch() =>
        _knowledgeSource.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, CancellationToken>((query, topK, _) =>
            {
                _capturedQuery = query;
                _capturedTopK = topK;
            })
            .ReturnsAsync(new List<SmartsuppKnowledgeChunk>());

    private IEnumerable<ChatMessage>? _capturedChat;
    private void SetupChat(string answer = "Návrh odpovědi") =>
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => _capturedChat = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, answer)]));

    private static SmartsuppKnowledgeChunk Chunk(string content, string filename) =>
        new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Content = content,
            Score = 0.9,
            SourceFilename = filename,
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

        _capturedQuery.Should().Be("Reklamace");
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

        _capturedQuery.Should().Be("Chci vrátit zboží");
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

        _capturedQuery!.Length.Should().Be(2000);
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

    [Fact]
    public async Task Handle_RecordsInteractionForEvalLog_OnSuccess()
    {
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(Chunk("Obsah o dopravě.", "doprava.pdf"));
        SetupChat("Dobrý den, balíky odesíláme do 24 hodin.");

        await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

        _recorder.HasInteraction.Should().BeTrue();
        _recorder.Feature.Should().Be(RagFeature.SmartsuppDraftReply);
        _recorder.Answer.Should().Be("Dobrý den, balíky odesíláme do 24 hodin.");
        _recorder.ConversationId.Should().Be("c1");
        _recorder.Topic.Should().Be("Doprava");
        _recorder.SystemPrompt.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to build**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests"`
Expected: Build FAILS — `GenerateDraftReplyHandler`'s constructor still takes `IMediator`, not `ISmartsuppKnowledgeSource` (CS1503/CS7036 on the `CreateHandler` call).

- [ ] **Step 3: Rewire `GenerateDraftReplyHandler` to consume `ISmartsuppKnowledgeSource`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyHandler
    : IRequestHandler<GenerateDraftReplyRequest, GenerateDraftReplyResponse>
{
    private const int RetrievalTopK = 5;
    private const int MaxExcerptLength = 200;

    // Keep the retrieval query within SearchDocumentsRequest's MaxLength(2000) constraint.
    private const int MaxRetrievalQueryLength = 2000;
    private const string NoContextPlaceholder = "(žádný relevantní kontext nebyl nalezen)";
    private const string NoTopicPlaceholder = "(neuvedeno)";

    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppKnowledgeSource _knowledgeSource;
    private readonly IChatClient _chatClient;
    private readonly SmartsuppDraftReplyOptions _options;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRagInteractionRecorder _recorder;
    private readonly ILogger<GenerateDraftReplyHandler> _logger;

    public GenerateDraftReplyHandler(
        ISmartsuppRepository repository,
        ISmartsuppKnowledgeSource knowledgeSource,
        IChatClient chatClient,
        IOptions<SmartsuppDraftReplyOptions> options,
        ICurrentUserService currentUserService,
        IRagInteractionRecorder recorder,
        ILogger<GenerateDraftReplyHandler> logger)
    {
        _repository = repository;
        _knowledgeSource = knowledgeSource;
        _chatClient = chatClient;
        _options = options.Value;
        _currentUserService = currentUserService;
        _recorder = recorder;
        _logger = logger;
    }

    public async Task<GenerateDraftReplyResponse> Handle(
        GenerateDraftReplyRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppConversationNotFound);

        var topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        var retrievalQuery = topic
            ?? ConversationTranscriptBuilder.LastContactMessages(conversation.Messages);
        if (string.IsNullOrWhiteSpace(retrievalQuery))
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppConversationEmpty);

        if (retrievalQuery.Length > MaxRetrievalQueryLength)
            retrievalQuery = retrievalQuery[..MaxRetrievalQueryLength];

        var transcript = ConversationTranscriptBuilder.Build(conversation.Messages);

        var chunks = await _knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken);

        var context = chunks.Count != 0
            ? string.Join("\n\n---\n\n", chunks.Select(c => c.Content))
            : NoContextPlaceholder;

        var agentName = SmartsuppNameHelper.ExtractFirstName(_currentUserService.GetCurrentUser().Name);

        var systemPrompt = _options.DraftReplySystemPrompt
            .Replace("{agent_name}", agentName)
            .Replace("{transcript}", transcript)
            .Replace("{context}", context)
            .Replace("{topic}", topic ?? NoTopicPlaceholder);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, "Napiš návrh odpovědi agenta na poslední zprávu zákazníka."),
        };

        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       or ObjectDisposedException
                                       || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
        {
            _logger.LogWarning(ex, "AI service unavailable while generating Smartsupp draft reply");
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppDraftReplyAiUnavailable);
        }

        var answer = response.Text ?? string.Empty;

        _recorder.RecordInteraction(
            RagFeature.SmartsuppDraftReply,
            retrievalQuery,
            systemPrompt,
            answer,
            conversationId: request.ConversationId,
            topic: topic);

        return new GenerateDraftReplyResponse
        {
            Answer = answer,
            Sources = chunks.Select(c => new DraftReplySource
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(MaxExcerptLength, c.Content.Length)],
                Score = c.Score,
            }).ToList(),
        };
    }
}
```

Note: `using MediatR;` is retained because `IRequestHandler<,>` still comes from MediatR — only the `IMediator` field/constructor dependency and the `SearchDocumentsRequest` dispatch are removed.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests"`
Expected: PASS (16 tests: 12 `[Fact]` + 4 `[Theory]` inline cases).

- [ ] **Step 5: Build the full solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs
git commit -m "refactor: rewire GenerateDraftReplyHandler onto ISmartsuppKnowledgeSource"
```

---
