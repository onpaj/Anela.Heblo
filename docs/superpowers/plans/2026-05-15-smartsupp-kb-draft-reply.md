# Smartsupp KB AI Draft Reply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Smartsupp support agents generate a KB-grounded, conversation-styled reply draft from a topic hint or a "Generovat odpověď" button, previewed editably in the composer.

**Architecture:** A new `GenerateDraftReply` MediatR use case in the Smartsupp feature loads the conversation server-side, builds a transcript, retrieves grounding context from the KnowledgeBase module via the `SearchDocumentsRequest` mediator message, and calls `IChatClient` with a dedicated Czech prompt. The frontend reshapes the existing `KnowledgeBaseSuggestions` area into a trigger bar and gives `ChatComposer` an editable "AI draft" state with regenerate / discard / sources controls.

**Tech Stack:** .NET 8, MediatR, `Microsoft.Extensions.AI.IChatClient`, EF Core; React + TypeScript, `@tanstack/react-query`, Tailwind, lucide-react; xUnit + Moq + FluentAssertions (BE), Jest + Testing Library (FE).

**Spec:** `docs/superpowers/specs/2026-05-15-smartsupp-kb-draft-reply-design.md`

---

## File Structure

**Backend — create:**
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs` — also `DraftReplySource`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/ConversationTranscriptBuilder.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ConversationTranscriptBuilderTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`

**Backend — modify:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add 2 error codes
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` — accept `IConfiguration`, bind options
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs:92` — pass `configuration`
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — add endpoint

**Frontend — create:**
- `frontend/src/components/customer-support/smartsupp/draftReplyHints.ts`
- `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
- `frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx`
- `frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx`
- Tests for each of the above under `hooks/__tests__/` and `__tests__/`

**Frontend — modify:**
- `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx` — rewrite with states
- `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx` — rewrite

**Frontend — delete:**
- `frontend/src/components/customer-support/smartsupp/KnowledgeBaseSuggestions.tsx`
- `frontend/src/components/customer-support/smartsupp/hooks/useKnowledgeBaseSuggestions.ts`
- `frontend/src/components/customer-support/smartsupp/__tests__/KnowledgeBaseSuggestions.test.tsx`
- `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useKnowledgeBaseSuggestions.test.ts`

**Docs — modify:**
- `docs/features/smartsupp.md`

---

## Task 1: Add error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (Smartsupp 27XX block, after line 285)

- [ ] **Step 1: Add the two error codes**

In the `// Smartsupp module errors (27XX)` block, immediately after `SmartsuppConversationNotFound = 2701,`, add:

```csharp
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    SmartsuppDraftReplyAiUnavailable = 2702,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    SmartsuppConversationEmpty = 2703,
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(smartsupp): add draft-reply error codes"
```

---

## Task 2: Create `SmartsuppDraftReplyOptions`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

/// <summary>
/// Options for the Smartsupp AI draft-reply feature. Bound from the optional
/// "SmartsuppDraftReply" configuration section; defaults below are used when absent.
/// </summary>
public class SmartsuppDraftReplyOptions
{
    public const string SectionName = "SmartsuppDraftReply";

    /// <summary>
    /// System prompt for draft-reply generation. Placeholders:
    /// {transcript} — the role-labelled conversation transcript;
    /// {context}    — retrieved KnowledgeBase chunks;
    /// {topic}      — the selected topic hint, or "(neuvedeno)" when none.
    /// </summary>
    public string DraftReplySystemPrompt { get; set; } =
        """
        Jsi agent zákaznické podpory kosmetické firmy Anela. Tvým úkolem je
        napsat návrh odpovědi na poslední zprávu zákazníka v probíhající
        konverzaci.

        Styl odpovědi:
        - Napodob tón, míru formálnosti a délku předchozích zpráv označených
          "Agent:" v této konverzaci.
        - Pokud konverzace žádnou zprávu agenta neobsahuje, použij zdvořilý
          formální český styl (oslovení "Dobrý den").
        - Odpovídej vždy v češtině.

        Obsah odpovědi:
        - Vycházej výhradně z poskytnutého kontextu z databáze znalostí.
          Nevymýšlej informace, které v kontextu nejsou.
        - Pokud kontext neobsahuje relevantní informaci, napiš zdvořilou
          odpověď, že se zákazníkovi ozveš s upřesněním.
        - Zaměř se na téma: {topic}

        Kontext z databáze znalostí:
        {context}

        Probíhající konverzace:
        {transcript}

        Napiš pouze samotný text návrhu odpovědi, bez jakéhokoli úvodu.
        """;
}
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs
git commit -m "feat(smartsupp): add draft-reply options with system prompt"
```

---

## Task 3: Create request, response, and `DraftReplySource` contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs`

- [ ] **Step 1: Create the request**

`GenerateDraftReplyRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyRequest : IRequest<GenerateDraftReplyResponse>
{
    /// <summary>Smartsupp conversation id. Set from the route by the controller.</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>Optional topic hint that steers KnowledgeBase retrieval and focus.</summary>
    public string? Topic { get; set; }
}
```

- [ ] **Step 2: Create the response and source DTO**

`GenerateDraftReplyResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyResponse : BaseResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<DraftReplySource> Sources { get; set; } = new();

    public GenerateDraftReplyResponse() { }
    public GenerateDraftReplyResponse(ErrorCodes errorCode) : base(errorCode) { }
}

/// <summary>Smartsupp-local mirror of a KnowledgeBase source chunk reference.</summary>
public class DraftReplySource
{
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyRequest.cs backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs
git commit -m "feat(smartsupp): add draft-reply request/response contracts"
```

---

## Task 4: `ConversationTranscriptBuilder` (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/ConversationTranscriptBuilder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ConversationTranscriptBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

`ConversationTranscriptBuilderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ConversationTranscriptBuilderTests
{
    private static SmartsuppMessage Msg(string id, SmartsuppMessageAuthorType type, string? content, int minuteOffset) =>
        new()
        {
            Id = id,
            ConversationId = "c1",
            AuthorType = type,
            Content = content,
            CreatedAt = new DateTime(2026, 5, 15, 10, minuteOffset, 0, DateTimeKind.Utc)
        };

    [Fact]
    public void Build_OrdersByCreatedAt_AndRoleLabelsLines()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m2", SmartsuppMessageAuthorType.Agent, "Dobrý den", 2),
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Mám dotaz", 1),
        };

        var result = ConversationTranscriptBuilder.Build(messages);

        result.Should().Be("Zákazník: Mám dotaz\nAgent: Dobrý den");
    }

    [Fact]
    public void Build_SkipsSystemTriggerAndEmptyMessages()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Ahoj", 1),
            Msg("m2", SmartsuppMessageAuthorType.System, "připojen agent", 2),
            Msg("m3", SmartsuppMessageAuthorType.Trigger, "uvítací zpráva", 3),
            Msg("m4", SmartsuppMessageAuthorType.Agent, "   ", 4),
            Msg("m5", SmartsuppMessageAuthorType.Bot, "Bot odpověď", 5),
        };

        var result = ConversationTranscriptBuilder.Build(messages);

        result.Should().Be("Zákazník: Ahoj\nBot: Bot odpověď");
    }

    [Fact]
    public void LastContactMessages_ReturnsLastThreeVisitorMessagesJoined()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "první", 1),
            Msg("m2", SmartsuppMessageAuthorType.Visitor, "druhá", 2),
            Msg("m3", SmartsuppMessageAuthorType.Agent, "agent", 3),
            Msg("m4", SmartsuppMessageAuthorType.Visitor, "třetí", 4),
            Msg("m5", SmartsuppMessageAuthorType.Visitor, "čtvrtá", 5),
        };

        var result = ConversationTranscriptBuilder.LastContactMessages(messages);

        result.Should().Be("druhá\ntřetí\nčtvrtá");
    }

    [Fact]
    public void LastContactMessages_ReturnsNull_WhenNoVisitorMessages()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Agent, "agent", 1),
        };

        ConversationTranscriptBuilder.LastContactMessages(messages).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ConversationTranscriptBuilderTests"`
Expected: FAIL — `ConversationTranscriptBuilder` does not exist.

- [ ] **Step 3: Implement `ConversationTranscriptBuilder`**

```csharp
using System.Text;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

/// <summary>
/// Builds a role-labelled, time-ordered text transcript from Smartsupp messages
/// and derives a retrieval query fallback from the customer's recent messages.
/// </summary>
public static class ConversationTranscriptBuilder
{
    private const int FallbackContactMessageCount = 3;

    public static string Build(IEnumerable<SmartsuppMessage> messages)
    {
        var builder = new StringBuilder();

        foreach (var message in messages.OrderBy(m => m.CreatedAt))
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            var label = LabelFor(message.AuthorType);
            if (label is null)
                continue;

            builder.AppendLine($"{label}: {message.Content.Trim()}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string? LastContactMessages(IEnumerable<SmartsuppMessage> messages)
    {
        var recent = messages
            .Where(m => m.AuthorType == SmartsuppMessageAuthorType.Visitor
                        && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.CreatedAt)
            .TakeLast(FallbackContactMessageCount)
            .Select(m => m.Content!.Trim())
            .ToList();

        return recent.Count == 0 ? null : string.Join("\n", recent);
    }

    private static string? LabelFor(SmartsuppMessageAuthorType type) => type switch
    {
        SmartsuppMessageAuthorType.Visitor => "Zákazník",
        SmartsuppMessageAuthorType.Agent => "Agent",
        SmartsuppMessageAuthorType.Bot => "Bot",
        _ => null,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ConversationTranscriptBuilderTests"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/ConversationTranscriptBuilder.cs backend/test/Anela.Heblo.Tests/Features/Smartsupp/ConversationTranscriptBuilderTests.cs
git commit -m "feat(smartsupp): add conversation transcript builder"
```

---

## Task 5: `GenerateDraftReplyHandler` (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

`GenerateDraftReplyHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
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

    private GenerateDraftReplyHandler CreateHandler(SmartsuppDraftReplyOptions? options = null) =>
        new(_repo.Object, _mediator.Object, _chatClient.Object,
            Options.Create(options ?? new SmartsuppDraftReplyOptions()), _logger.Object);

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
        SetupConversation(ConversationWith(
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
        SetupSearch(Chunk("Obsah dokumentu o dopravě.", "doprava.pdf"));
        SetupChat("Dobrý den, balíky odesíláme do 24 hodin.");

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Answer.Should().Be("Dobrý den, balíky odesíláme do 24 hodin.");
        result.Sources.Should().ContainSingle();
        result.Sources[0].Filename.Should().Be("doprava.pdf");
        result.Sources[0].Excerpt.Should().Be("Obsah dokumentu o dopravě.");
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

        var result = await CreateHandler().Handle(
            new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyAiUnavailable);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests"`
Expected: FAIL — `GenerateDraftReplyHandler` does not exist.

- [ ] **Step 3: Implement `GenerateDraftReplyHandler`**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
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
    private const string NoContextPlaceholder = "(žádný relevantní kontext nebyl nalezen)";
    private const string NoTopicPlaceholder = "(neuvedeno)";

    private readonly ISmartsuppRepository _repository;
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly SmartsuppDraftReplyOptions _options;
    private readonly ILogger<GenerateDraftReplyHandler> _logger;

    public GenerateDraftReplyHandler(
        ISmartsuppRepository repository,
        IMediator mediator,
        IChatClient chatClient,
        IOptions<SmartsuppDraftReplyOptions> options,
        ILogger<GenerateDraftReplyHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
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

        var transcript = ConversationTranscriptBuilder.Build(conversation.Messages);

        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = retrievalQuery, TopK = RetrievalTopK },
            cancellationToken);

        var context = searchResult.Chunks.Count != 0
            ? string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content))
            : NoContextPlaceholder;

        var systemPrompt = _options.DraftReplySystemPrompt
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
                                       or TaskCanceledException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "AI service unavailable while generating Smartsupp draft reply");
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppDraftReplyAiUnavailable);
        }

        return new GenerateDraftReplyResponse
        {
            Answer = response.Text ?? string.Empty,
            Sources = searchResult.Chunks.Select(c => new DraftReplySource
            {
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(MaxExcerptLength, c.Content.Length)],
                Score = c.Score,
            }).ToList(),
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests"`
Expected: PASS — all tests (4 theory cases + 6 facts).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs
git commit -m "feat(smartsupp): add GenerateDraftReply handler"
```

---

## Task 6: Register options in the Smartsupp module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs:92`

- [ ] **Step 1: Update `SmartsuppModule` to accept `IConfiguration` and bind options**

In `SmartsuppModule.cs`, add these `using` lines with the existing usings:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Microsoft.Extensions.Configuration;
```

Change the method signature:

```csharp
    public static IServiceCollection AddSmartsuppModule(this IServiceCollection services, IConfiguration configuration)
```

Immediately after `services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();`, add:

```csharp
        services.AddOptions<SmartsuppDraftReplyOptions>()
            .Bind(configuration.GetSection(SmartsuppDraftReplyOptions.SectionName));
```

- [ ] **Step 2: Update the call site in `ApplicationModule.cs`**

Change line 92 from `services.AddSmartsuppModule();` to:

```csharp
        services.AddSmartsuppModule(configuration);
```

(`configuration` is already in scope in `ApplicationModule` — it is passed to `AddKnowledgeBaseModule(configuration)` on line 82.)

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds. The MediatR handler `GenerateDraftReplyHandler` is auto-registered by the existing assembly scan.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat(smartsupp): bind draft-reply options in module"
```

---

## Task 7: Add the controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`

- [ ] **Step 1: Add the `using` and the endpoint**

Add to the usings:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
```

Add this action after the `GetConversation` action:

```csharp
    [HttpPost("conversations/{id}/draft-reply")]
    [ProducesResponseType(typeof(GenerateDraftReplyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GenerateDraftReplyResponse>> GenerateDraftReply(
        string id,
        [FromBody] GenerateDraftReplyRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new GenerateDraftReplyRequest();
        request.ConversationId = id;
        var result = await _mediator.Send(request, cancellationToken);
        return HandleResponse(result);
    }
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Verify the full backend test suite still passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~Smartsupp"`
Expected: PASS — all Smartsupp tests including the new ones.

- [ ] **Step 4: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs
git commit -m "feat(smartsupp): add draft-reply API endpoint"
```

---

## Task 8: Frontend — hint constants

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/draftReplyHints.ts`

- [ ] **Step 1: Create the hint list**

```typescript
export interface DraftReplyHint {
  id: string;
  label: string;
}

/**
 * Predefined topic hints for AI draft-reply generation. The label is sent
 * verbatim to the backend as the retrieval topic.
 */
export const DRAFT_REPLY_HINTS: DraftReplyHint[] = [
  { id: "vymena", label: "Výměna zboží" },
  { id: "reklamace", label: "Reklamace" },
  { id: "doprava", label: "Doprava" },
  { id: "platba", label: "Platba" },
  { id: "vraceni", label: "Vrácení zboží" },
];
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/draftReplyHints.ts
git commit -m "feat(smartsupp): add draft-reply topic hint constants"
```

---

## Task 9: Frontend — `useGenerateDraftReply` hook (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
- Test: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts`

- [ ] **Step 1: Write the failing test**

`useGenerateDraftReply.test.ts`:

```typescript
import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGenerateDraftReply } from "../useGenerateDraftReply";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();

function setApiResponse(status: number, body: unknown): void {
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: "http://api.test",
    http: { fetch: mockFetch },
  });
  mockFetch.mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  });
}

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockFetch.mockReset();
});

describe("useGenerateDraftReply", () => {
  it("returns answer and sources on success", async () => {
    setApiResponse(200, {
      success: true,
      answer: "Dobrý den, balíky odesíláme do 24 hodin.",
      sources: [{ documentId: "d1", filename: "doprava.pdf", excerpt: "...", score: 0.9 }],
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Doprava"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(result.current.result!.answer).toMatch(/balíky odesíláme/);
    expect(result.current.result!.sources).toHaveLength(1);
  });

  it("posts the topic in the request body", async () => {
    setApiResponse(200, { success: true, answer: "x", sources: [] });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Reklamace"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(mockFetch).toHaveBeenCalledWith(
      "http://api.test/api/smartsupp/conversations/c1/draft-reply",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ topic: "Reklamace" }) }),
    );
  });

  it("surfaces a Czech message for a known error code", async () => {
    setApiResponse(503, { success: false, errorCode: "SmartsuppDraftReplyAiUnavailable" });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/nedostupná/i);
  });

  it("surfaces a generic message for an unknown failure", async () => {
    setApiResponse(500, { success: false });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo se/i);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx jest useGenerateDraftReply`
Expected: FAIL — module `../useGenerateDraftReply` not found.

- [ ] **Step 3: Implement the hook**

```typescript
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";

export interface DraftReplySource {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface DraftReplyResult {
  answer: string;
  sources: DraftReplySource[];
}

interface GenerateDraftReplyApiResponse {
  success: boolean;
  errorCode?: string;
  answer?: string;
  sources?: DraftReplySource[];
}

const ERROR_MESSAGES: Record<string, string> = {
  SmartsuppDraftReplyAiUnavailable:
    "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
  SmartsuppConversationEmpty: "Konverzace neobsahuje zprávu zákazníka.",
  SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
};

function messageForError(code?: string): string {
  if (code && ERROR_MESSAGES[code]) {
    return ERROR_MESSAGES[code];
  }
  return "Nepodařilo se vygenerovat odpověď.";
}

interface UseGenerateDraftReplyResult {
  generate: (topic?: string) => void;
  isLoading: boolean;
  error: string | null;
  result: DraftReplyResult | null;
  reset: () => void;
}

export function useGenerateDraftReply(
  conversationId: string | null,
): UseGenerateDraftReplyResult {
  const mutation = useMutation<DraftReplyResult, Error, string | undefined>({
    mutationFn: async (topic) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      const apiClient = getAuthenticatedApiClient();
      const baseUrl = (apiClient as unknown as { baseUrl: string }).baseUrl;
      const http = (apiClient as unknown as {
        http: { fetch: (url: string, init: RequestInit) => Promise<Response> };
      }).http;

      const response = await http.fetch(
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/draft-reply`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ topic: topic ?? null }),
        },
      );

      const data = (await response.json()) as GenerateDraftReplyApiResponse;
      if (!response.ok || !data.success) {
        throw new Error(messageForError(data?.errorCode));
      }

      return { answer: data.answer ?? "", sources: data.sources ?? [] };
    },
  });

  return {
    generate: (topic?: string) => mutation.mutate(topic),
    isLoading: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    result: mutation.data ?? null,
    reset: mutation.reset,
  };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx jest useGenerateDraftReply`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts
git commit -m "feat(smartsupp): add useGenerateDraftReply hook"
```

---

## Task 10: Frontend — `DraftReplyTriggerBar` (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx`
- Test: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyTriggerBar.test.tsx`

- [ ] **Step 1: Write the failing test**

`DraftReplyTriggerBar.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import DraftReplyTriggerBar from "../DraftReplyTriggerBar";

describe("DraftReplyTriggerBar", () => {
  it("renders all topic hint pills and the generate button", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByText("Reklamace")).toBeInTheDocument();
    expect(screen.getByText("Výměna zboží")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeInTheDocument();
  });

  it("calls onGenerate with the hint label when a pill is clicked", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByText("Reklamace"));
    expect(onGenerate).toHaveBeenCalledWith("Reklamace");
  });

  it("calls onGenerate with undefined when the generate button is clicked", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /generovat odpověď/i }));
    expect(onGenerate).toHaveBeenCalledWith(undefined);
  });

  it("disables the generate button when canGenerateWithoutTopic is false", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={false}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("disables every control while disabled is true", () => {
    render(
      <DraftReplyTriggerBar
        disabled={true}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByText("Reklamace").closest("button")).toBeDisabled();
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("shows an error message when error is provided", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error="AI služba je nedostupná."
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByText("AI služba je nedostupná.")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx jest DraftReplyTriggerBar`
Expected: FAIL — module `../DraftReplyTriggerBar` not found.

- [ ] **Step 3: Implement `DraftReplyTriggerBar`**

```typescript
import { Sparkles } from "lucide-react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";

interface DraftReplyTriggerBarProps {
  disabled: boolean;
  canGenerateWithoutTopic: boolean;
  error: string | null;
  onGenerate: (topic?: string) => void;
}

function DraftReplyTriggerBar({
  disabled,
  canGenerateWithoutTopic,
  error,
  onGenerate,
}: DraftReplyTriggerBarProps) {
  return (
    <div className="border-t border-gray-100 bg-gray-50 px-4 py-2">
      <div className="flex flex-wrap items-center gap-2">
        {DRAFT_REPLY_HINTS.map((hint) => (
          <button
            key={hint.id}
            type="button"
            disabled={disabled}
            onClick={() => onGenerate(hint.label)}
            className="inline-flex items-center rounded-full px-3 py-1 text-xs bg-white border border-gray-200 text-gray-700 hover:bg-blue-50 hover:border-blue-300 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {hint.label}
          </button>
        ))}
        <button
          type="button"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={
            canGenerateWithoutTopic
              ? undefined
              : "Konverzace neobsahuje zprávu zákazníka"
          }
          className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Sparkles className="w-3.5 h-3.5" />
          Generovat odpověď
        </button>
      </div>
      {error && <p className="mt-1.5 text-xs text-red-600">{error}</p>}
    </div>
  );
}

export default DraftReplyTriggerBar;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx jest DraftReplyTriggerBar`
Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyTriggerBar.test.tsx
git commit -m "feat(smartsupp): add draft-reply trigger bar"
```

---

## Task 11: Frontend — `DraftReplyToolbar` (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx`
- Test: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx`

- [ ] **Step 1: Write the failing test**

`DraftReplyToolbar.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import DraftReplyToolbar from "../DraftReplyToolbar";
import type { DraftReplySource } from "../hooks/useGenerateDraftReply";

const sources: DraftReplySource[] = [
  { documentId: "d1", filename: "reklamace.pdf", excerpt: "...", score: 0.9 },
];

describe("DraftReplyToolbar", () => {
  it("calls onRegenerate when the regenerate button is clicked", () => {
    const onRegenerate = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={onRegenerate} onDiscard={jest.fn()} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /regenerovat/i }));
    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });

  it("calls onDiscard when the discard button is clicked", () => {
    const onDiscard = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={onDiscard} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    expect(onDiscard).toHaveBeenCalledTimes(1);
  });

  it("reveals source filenames in a tooltip on hover", () => {
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    fireEvent.mouseEnter(screen.getByRole("button", { name: /zdroje/i }));
    expect(screen.getByRole("tooltip")).toHaveTextContent("reklamace.pdf");
  });

  it("does not render the sources control when there are no sources", () => {
    render(
      <DraftReplyToolbar sources={[]} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    expect(screen.queryByRole("button", { name: /zdroje/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx jest DraftReplyToolbar`
Expected: FAIL — module `../DraftReplyToolbar` not found.

- [ ] **Step 3: Implement `DraftReplyToolbar`**

```typescript
import { useState } from "react";
import { RefreshCw, X, Info } from "lucide-react";
import type { DraftReplySource } from "./hooks/useGenerateDraftReply";

interface DraftReplyToolbarProps {
  sources: DraftReplySource[];
  onRegenerate: () => void;
  onDiscard: () => void;
}

function DraftReplyToolbar({ sources, onRegenerate, onDiscard }: DraftReplyToolbarProps) {
  const [showSources, setShowSources] = useState(false);

  return (
    <div className="flex items-center gap-3 text-xs">
      <span className="font-medium text-blue-600">Návrh od AI</span>
      <button
        type="button"
        onClick={onRegenerate}
        className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
      >
        <RefreshCw className="w-3.5 h-3.5" />
        Regenerovat
      </button>
      <button
        type="button"
        onClick={onDiscard}
        className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
      >
        <X className="w-3.5 h-3.5" />
        Zahodit
      </button>
      {sources.length > 0 && (
        <div className="relative">
          <button
            type="button"
            aria-label="Zdroje"
            onMouseEnter={() => setShowSources(true)}
            onMouseLeave={() => setShowSources(false)}
            onFocus={() => setShowSources(true)}
            onBlur={() => setShowSources(false)}
            className="inline-flex items-center text-gray-400 hover:text-gray-600"
          >
            <Info className="w-3.5 h-3.5" />
          </button>
          {showSources && (
            <div
              role="tooltip"
              className="absolute bottom-full left-0 mb-1 w-64 rounded-md bg-gray-800 p-2 text-white shadow-lg z-10"
            >
              <p className="mb-1 font-medium">Zdroje z databáze znalostí:</p>
              <ul className="list-inside list-disc space-y-0.5">
                {sources.map((source, index) => (
                  <li key={`${source.documentId}-${index}`}>{source.filename}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default DraftReplyToolbar;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx jest DraftReplyToolbar`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx
git commit -m "feat(smartsupp): add draft-reply toolbar"
```

---

## Task 12: Frontend — rewrite `ChatComposer` with AI-draft states (TDD)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx`
- Modify (rewrite): `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx`

- [ ] **Step 1: Rewrite the test**

Replace the entire contents of `ChatComposer.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ChatComposer from "../ChatComposer";
import * as draftReplyHook from "../hooks/useGenerateDraftReply";

const generate = jest.fn();
const reset = jest.fn();

function mockHook(overrides: Partial<ReturnType<typeof draftReplyHook.useGenerateDraftReply>>) {
  jest.spyOn(draftReplyHook, "useGenerateDraftReply").mockReturnValue({
    generate,
    isLoading: false,
    error: null,
    result: null,
    reset,
    ...overrides,
  });
}

beforeEach(() => {
  generate.mockReset();
  reset.mockReset();
  jest.restoreAllMocks();
});

describe("ChatComposer", () => {
  it("renders an empty textarea and a disabled Send button", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    expect(screen.getByRole("button", { name: /odeslat/i })).toBeDisabled();
  });

  it("calls generate with the hint label when a topic pill is clicked", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.click(screen.getByText("Reklamace"));
    expect(generate).toHaveBeenCalledWith("Reklamace");
  });

  it("disables the generate button when there is no contact message", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("places the generated answer into the textarea and shows the AI toolbar", async () => {
    mockHook({
      result: {
        answer: "Dobrý den, reklamaci vyřídíme do 14 dnů.",
        sources: [{ documentId: "d1", filename: "reklamace.pdf", excerpt: "...", score: 0.9 }],
      },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    await waitFor(() => {
      const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
      expect(textarea.value).toMatch(/reklamaci vyřídíme/);
    });
    expect(screen.getByText("Návrh od AI")).toBeInTheDocument();
  });

  it("clears the draft and hides the toolbar on discard", async () => {
    mockHook({
      result: { answer: "Vygenerovaná odpověď", sources: [] },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    await waitFor(() => expect(screen.getByText("Návrh od AI")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    expect(screen.queryByText("Návrh od AI")).not.toBeInTheDocument();
  });

  it("hides the AI toolbar once the agent edits the generated draft", async () => {
    mockHook({
      result: { answer: "Vygenerovaná odpověď", sources: [] },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    await waitFor(() => expect(screen.getByText("Návrh od AI")).toBeInTheDocument());
    const textarea = screen.getByPlaceholderText(/napište odpověď/i);
    fireEvent.change(textarea, { target: { value: "Ručně upravený text" } });
    expect(screen.queryByText("Návrh od AI")).not.toBeInTheDocument();
  });

  it("shows the hook error in the trigger bar", () => {
    mockHook({ error: "AI služba je nedostupná." });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    expect(screen.getByText("AI služba je nedostupná.")).toBeInTheDocument();
  });

  it("displays a character counter", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "hello" },
    });
    expect(screen.getByText(/5 \//)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx jest ChatComposer`
Expected: FAIL — the old `ChatComposer` does not import `useGenerateDraftReply` and has no AI-draft state.

- [ ] **Step 3: Rewrite `ChatComposer`**

Replace the entire contents of `ChatComposer.tsx`:

```typescript
import { useEffect, useState } from "react";
import { Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
}

const MAX_CHARS = 4000;

function ChatComposer({ conversationId, lastContactMessage }: ChatComposerProps) {
  const [draft, setDraft] = useState("");
  const [isAiDraft, setIsAiDraft] = useState(false);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);

  // Move a freshly generated answer into the composer as an editable AI draft.
  useEffect(() => {
    if (result) {
      setDraft(result.answer.slice(0, MAX_CHARS));
      setSources(result.sources);
      setIsAiDraft(true);
      reset();
    }
  }, [result, reset]);

  const canGenerateWithoutTopic =
    lastContactMessage !== null && lastContactMessage.trim() !== "";

  const requestGeneration = (topic?: string) => {
    if (draft.trim() !== "" && !isAiDraft) {
      const confirmed = window.confirm(
        "Přepsat rozepsanou odpověď vygenerovaným návrhem?",
      );
      if (!confirmed) {
        return;
      }
    }
    setLastTopic(topic);
    generate(topic);
  };

  const handleDraftChange = (value: string) => {
    setDraft(value.slice(0, MAX_CHARS));
    if (isAiDraft) {
      setIsAiDraft(false);
    }
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setIsAiDraft(false);
    setLastTopic(undefined);
  };

  return (
    <div className="flex flex-col">
      <DraftReplyTriggerBar
        disabled={isLoading}
        canGenerateWithoutTopic={canGenerateWithoutTopic}
        error={error}
        onGenerate={requestGeneration}
      />
      <div className="flex flex-col gap-2 border-t border-gray-200 bg-white p-3">
        {isAiDraft && (
          <DraftReplyToolbar
            sources={sources}
            onRegenerate={() => generate(lastTopic)}
            onDiscard={handleDiscard}
          />
        )}
        <textarea
          value={draft}
          disabled={isLoading}
          onChange={(e) => handleDraftChange(e.target.value)}
          placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
          rows={3}
          className="w-full resize-none rounded-md border border-gray-200 px-3 py-2 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50"
        />
        <div className="flex items-center justify-between">
          <span className="text-xs text-gray-400">
            {draft.length} / {MAX_CHARS}
          </span>
          <button
            type="button"
            disabled
            title="Odpovídání bude přidáno později"
            aria-label="Odeslat"
            className="inline-flex cursor-not-allowed items-center gap-2 rounded-md bg-blue-500 px-3 py-1.5 text-sm font-medium text-white opacity-50"
          >
            <Send className="h-4 w-4" />
            Odeslat
          </button>
        </div>
      </div>
    </div>
  );
}

export default ChatComposer;
```

> **Note on Regenerate:** the toolbar's Regenerate calls `generate(lastTopic)` directly (not `requestGeneration`) because an unedited AI draft should be replaced without a confirm prompt. Editing the draft flips `isAiDraft` to `false`, which hides the toolbar — so Regenerate is only reachable on a pristine AI draft.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx jest ChatComposer`
Expected: PASS — 8 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ChatComposer.tsx frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx
git commit -m "feat(smartsupp): rewrite ChatComposer with AI draft-reply states"
```

---

## Task 13: Remove the obsolete static suggestions

**Files:**
- Delete: `frontend/src/components/customer-support/smartsupp/KnowledgeBaseSuggestions.tsx`
- Delete: `frontend/src/components/customer-support/smartsupp/hooks/useKnowledgeBaseSuggestions.ts`
- Delete: `frontend/src/components/customer-support/smartsupp/__tests__/KnowledgeBaseSuggestions.test.tsx`
- Delete: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useKnowledgeBaseSuggestions.test.ts`

- [ ] **Step 1: Confirm nothing else imports the removed modules**

Run: `cd frontend && grep -rn "KnowledgeBaseSuggestions\|useKnowledgeBaseSuggestions" src/`
Expected: only the four files listed above. If any other file references them, stop and report.

- [ ] **Step 2: Delete the files**

```bash
git rm frontend/src/components/customer-support/smartsupp/KnowledgeBaseSuggestions.tsx \
       frontend/src/components/customer-support/smartsupp/hooks/useKnowledgeBaseSuggestions.ts \
       frontend/src/components/customer-support/smartsupp/__tests__/KnowledgeBaseSuggestions.test.tsx \
       frontend/src/components/customer-support/smartsupp/hooks/__tests__/useKnowledgeBaseSuggestions.test.ts
```

- [ ] **Step 3: Run the full Smartsupp frontend test suite**

Run: `cd frontend && npx jest customer-support/smartsupp`
Expected: PASS — all remaining Smartsupp tests (`ChatComposer`, `DraftReplyTriggerBar`, `DraftReplyToolbar`, `useGenerateDraftReply`, `ConversationDetail`, `SmartsuppChatsPage`, etc.).

- [ ] **Step 4: Commit**

```bash
git commit -m "chore(smartsupp): remove obsolete static KB suggestions"
```

---

## Task 14: Update feature documentation

**Files:**
- Modify: `docs/features/smartsupp.md`

- [ ] **Step 1: Add the feature to the "Hlavní funkce" list**

In the `### Hlavní funkce` bullet list, add:

```markdown
- **AI návrh odpovědi** — agent vygeneruje návrh odpovědi z celé konverzace a databáze znalostí (KnowledgeBase RAG), s volitelným tématem
```

- [ ] **Step 2: Add the endpoint to the protected-endpoints table**

In the `### Konverzace (chráněné — vyžaduje přihlášení)` table, add a row:

```markdown
| `POST` | `/api/smartsupp/conversations/{id}/draft-reply` | Vygeneruje AI návrh odpovědi (`{ "topic": "Reklamace" }` — volitelné) |
```

- [ ] **Step 3: Add a configuration note**

After the existing `## Konfigurace` table, add:

```markdown
### AI návrh odpovědi

Systémový prompt pro generování návrhů odpovědí má výchozí hodnotu v kódu
(`SmartsuppDraftReplyOptions`). Lze ho přepsat volitelnou sekcí
`SmartsuppDraftReply:DraftReplySystemPrompt` v `appsettings.json`.

Retrieval kontextu probíhá přes KnowledgeBase modul (`SearchDocumentsRequest`).
Dotaz se odvodí z tématu (`topic`), nebo — pokud téma chybí — z posledních
zpráv zákazníka.
```

- [ ] **Step 4: Commit**

```bash
git add docs/features/smartsupp.md
git commit -m "docs(smartsupp): document AI draft-reply feature"
```

---

## Task 15: Full validation

- [ ] **Step 1: Backend build, format, tests**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test --filter "FullyQualifiedName~Smartsupp"`
Expected: build succeeds, no format changes, all Smartsupp tests pass.

- [ ] **Step 2: Frontend build, lint, tests**

Run: `cd frontend && npm run build && npm run lint && npx jest customer-support/smartsupp`
Expected: build succeeds, lint clean, all Smartsupp tests pass.

- [ ] **Step 3: Final commit if formatting changed anything**

```bash
git add -A && git commit -m "chore(smartsupp): apply formatting" || echo "nothing to commit"
```

---

## Notes / deviations from the spec

- **No controller integration test.** The existing `GetConversation`/`ListConversations` endpoints have no controller-level integration tests (only the webhook controller does). The `draft-reply` action is a 4-line passthrough identical in shape to `GetConversation`; its behaviour is fully covered by `GenerateDraftReplyHandlerTests`. Adding a `WebApplicationFactory` harness only for this endpoint would be inconsistent with the codebase. The spec's "controller test" item is therefore satisfied by the handler tests plus the build step.
- **E2E deferred** — per the spec, sending is not wired, so a full flow cannot complete.
- **`appsettings.json` is not modified** — `SmartsuppDraftReplyOptions` carries its default prompt in code, and `.Bind` on a missing section is a no-op. The section is documented as optional.
