# Invert Smartsupp → KnowledgeBase Dependency in GenerateDraftReplyHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `GenerateDraftReplyHandler`'s (Smartsupp module) direct dependency on KnowledgeBase's `SearchDocumentsRequest`/`SearchDocumentsResponse` MediatR contract by introducing a Smartsupp-owned `ISmartsuppKnowledgeSource` abstraction implemented by a KnowledgeBase-owned adapter, and lock the boundary in with a machine-enforced `ModuleBoundariesTests` rule.

**Architecture:** Mirror the already-shipped `IArticleKnowledgeSource` / `KnowledgeBaseArticleKnowledgeSource` pattern exactly: Smartsupp defines a narrow `SearchAsync(string query, int topK, CancellationToken)` interface + DTO in its own `Contracts/` folder; KnowledgeBase implements it via an `internal sealed` adapter in `KnowledgeBase/Infrastructure/` that delegates to the existing `SearchDocumentsRequest`/`SearchDocumentsHandler` MediatR flow unchanged; KnowledgeBase's DI module registers the binding (provider owns the registration). `GenerateDraftReplyHandler` is rewired to consume the new interface instead of `IMediator`, and a new empty-allowlist `Smartsupp -> KnowledgeBase` rule is added to `ModuleBoundariesTests` to make the boundary CI-enforced.

**Tech Stack:** .NET 8, MediatR, Moq, xUnit, FluentAssertions.

---

### task: define-smartsupp-knowledge-source-contract-and-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs:44-47`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs`

This task adds the new Smartsupp-owned contract, the KnowledgeBase-owned adapter that implements it, and the DI wiring — with no changes yet to `GenerateDraftReplyHandler` (that's the next task). It is structurally a copy of the existing `IArticleKnowledgeSource` / `KnowledgeBaseArticleKnowledgeSource` pair (see `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs` and `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs`), extended with a `DocumentId` field.

- [ ] **Step 1: Write the failing adapter test**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseSmartsuppKnowledgeSourceTests
{
    private readonly Mock<IMediator> _mediator = new();

    private KnowledgeBaseSmartsuppKnowledgeSource CreateAdapter() =>
        new(_mediator.Object);

    [Fact]
    public async Task SearchAsync_DispatchesSearchDocumentsRequest_WithCorrectQueryAndTopK()
    {
        // Arrange
        var adapter = CreateAdapter();
        const string query = "test query";
        const int topK = 5;
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync(query, topK, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<SearchDocumentsRequest>(r =>
                r.Query == query &&
                r.TopK == topK),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MapsFiveFields_FromChunkResultToSmartsuppKnowledgeChunk()
    {
        // Arrange
        var adapter = CreateAdapter();
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        const string sourceFilename = "doc.pdf";
        const string content = "content text";
        const double score = 0.95;
        const string sourcePath = "/some/path";

        var chunkResult = new ChunkResult
        {
            ChunkId = chunkId,
            DocumentId = documentId,
            SourceFilename = sourceFilename,
            Content = content,
            Score = score,
            SourcePath = sourcePath,
        };

        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [chunkResult] });

        // Act
        var result = await adapter.SearchAsync("query", 3, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].ChunkId.Should().Be(chunkId);
        result[0].DocumentId.Should().Be(documentId);
        result[0].SourceFilename.Should().Be(sourceFilename);
        result[0].Content.Should().Be(content);
        result[0].Score.Should().Be(score);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoChunks()
    {
        // Arrange
        var adapter = CreateAdapter();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        var result = await adapter.SearchAsync("query", 5, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellationToken()
    {
        // Arrange
        var adapter = CreateAdapter();
        var cts = new CancellationTokenSource();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync("query", 5, cts.Token);

        // Assert
        _mediator.Verify(m => m.Send(
            It.IsAny<SearchDocumentsRequest>(),
            cts.Token), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseSmartsuppKnowledgeSourceTests"`
Expected: Build FAILS — `KnowledgeBaseSmartsuppKnowledgeSource` and `Anela.Heblo.Application.Features.Smartsupp.Contracts` do not exist yet (CS0246).

- [ ] **Step 3: Create the `ISmartsuppKnowledgeSource` contract**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

/// <summary>
/// Smartsupp-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// Structurally mirrors <c>IArticleKnowledgeSource</c> (string-query shape) — not
/// <c>ILeafletKnowledgeSource</c> (embedding-vector shape) — because
/// <c>GenerateDraftReplyHandler</c> builds a plain-text retrieval query and never
/// computes an embedding itself.
/// </summary>
public interface ISmartsuppKnowledgeSource
{
    Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class SmartsuppKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

- [ ] **Step 4: Create the `KnowledgeBaseSmartsuppKnowledgeSource` adapter**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseSmartsuppKnowledgeSource : ISmartsuppKnowledgeSource
{
    private readonly IMediator _mediator;

    public KnowledgeBaseSmartsuppKnowledgeSource(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new SearchDocumentsRequest { Query = query, TopK = topK }, cancellationToken);

        return response.Chunks
            .Select(c => new SmartsuppKnowledgeChunk
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                SourceFilename = c.SourceFilename,
                Content = c.Content,
                Score = c.Score,
            })
            .ToArray();
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseSmartsuppKnowledgeSourceTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Register the DI binding in `KnowledgeBaseModule`**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, the current lines 44-47 read:

```csharp
        // Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter.
        // Scoped to match existing Article contract bindings above.
        services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();

```

Replace with (adding the new registration immediately after, and add the `using` for the Smartsupp contracts namespace at the top of the file alongside the existing `using Anela.Heblo.Application.Features.Article.Contracts;` on line 1):

```csharp
        // Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter.
        // Scoped to match existing Article contract bindings above.
        services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();

        // Cross-module contract: KnowledgeBase implements Smartsupp's ISmartsuppKnowledgeSource via adapter.
        // Same provider-owned-DI pattern as the Leaflet/Article bindings above.
        services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>();

```

And change line 1 of the file from:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
```

to:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
```

- [ ] **Step 7: Build to verify DI wiring compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs
git commit -m "feat: add ISmartsuppKnowledgeSource contract and KnowledgeBase adapter"
```

---

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

### task: add-smartsupp-knowledgebase-boundary-rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:27-28` (new allowlist)
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:401-403` (new `TheoryData` entry)

This task adds a CI-enforced rule that Smartsupp code must never reference KnowledgeBase-owned namespaces directly. It depends on the previous two tasks already being applied (the handler is already migrated), so the new theory case will pass immediately once added — this is what proves the fix is complete and locks it in against future regressions. Before the previous two tasks were applied, this exact rule (with `GenerateDraftReplyHandler.cs` still importing `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments`) would have failed, which is what makes it a meaningful regression guard rather than a no-op.

- [ ] **Step 1: Add the `SmartsuppKnowledgeBaseAllowlist` allowlist**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, the current lines 26-28 read:

```csharp
    // Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

```

Replace with:

```csharp
    // Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Smartsupp -> KnowledgeBase. Empty — GenerateDraftReplyHandler now consumes
    // the Smartsupp-owned ISmartsuppKnowledgeSource contract; the KnowledgeBase adapter
    // (KnowledgeBaseSmartsuppKnowledgeSource) lives in KnowledgeBase.Infrastructure.
    private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);

```

- [ ] **Step 2: Add the `Smartsupp -> KnowledgeBase` rule to `Rules()`**

In the same file, the current lines 392-403 read:

```csharp
        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

Replace with:

```csharp
        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Smartsupp -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Smartsupp",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: SmartsuppKnowledgeBaseAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

- [ ] **Step 3: Run the architecture test suite to verify the new rule passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS for every `Consumer_types_should_not_reference_provider_owned_namespaces` theory case, including the new `Smartsupp -> KnowledgeBase` case (zero violations found, since `GenerateDraftReplyHandler.cs` no longer references any `Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, or `Anela.Heblo.Persistence.KnowledgeBase` type after the previous task).

- [ ] **Step 4: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: PASS — no regressions in any other module's tests.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce Smartsupp -> KnowledgeBase module boundary"
```
