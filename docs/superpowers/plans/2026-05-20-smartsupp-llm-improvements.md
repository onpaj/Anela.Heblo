# Smartsupp LLM Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Personalize AI-generated draft signatures with the logged-in user's first name, and add an end-to-end send-message flow so replies can be sent from Heblo without leaving the app.

**Architecture:** Part 1 injects `ICurrentUserService` into `GenerateDraftReplyHandler` and adds an `{agent_name}` placeholder to the system prompt. Part 2 adds a new `SendMessage` MediatR vertical slice (request/validator/handler), extends `ISmartsuppApiClient`/`SmartsuppApiClient`, adds a controller endpoint, and wires the frontend Send button via a new `useSendMessage` React Query hook used directly inside `ChatComposer`.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit + Moq + FluentAssertions; React + TanStack Query, TypeScript, Vitest/Jest.

---

## File Map

### New files
| File | Responsibility |
|------|---------------|
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppNameHelper.cs` | `ExtractFirstName` utility, shared by both handlers |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageRequest.cs` | MediatR request with `ConversationId` + `Content` |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageResponse.cs` | Response envelope: `MessageId`, `CreatedAt` |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageValidator.cs` | FluentValidation: content required + max 4000 chars |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageHandler.cs` | Validates conversation exists, calls API client, maps result |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppNameHelperTests.cs` | Edge-case coverage for `ExtractFirstName` |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SendMessageHandlerTests.cs` | Handler unit tests: happy path, 404, API failure |
| `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts` | React Query mutation with optimistic update + rollback |
| `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts` | Hook unit tests |

### Modified files
| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs` | Add `{agent_name}` placeholder to default system prompt |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` | Inject `ICurrentUserService`, derive first name, replace placeholder |
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Add `SmartsuppSendMessageUnavailable = 2704` |
| `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs` | Add `SmartsuppSentMessageData` class + `SendMessageAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` | Implement `SendMessageAsync` |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` | Register `SendMessageValidator` + pipeline behavior |
| `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` | Add `POST /conversations/{id}/messages` endpoint |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` | Add `ICurrentUserService` mock, new agent-name tests |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs` | Add `SendMessageAsync` test cases |
| `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx` | Use `useSendMessage`, enable Send button, clear on success |
| `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx` | Update "disabled" expectation, add send tests |

---

## Task 1: SmartsuppNameHelper — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppNameHelper.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppNameHelperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppNameHelperTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppNameHelperTests
{
    [Theory]
    [InlineData("Ondřej Pajgrt", "Ondřej")]
    [InlineData("Jana Nováková", "Jana")]
    [InlineData("Jana", "Jana")]
    [InlineData("", "Anela")]
    [InlineData(null, "Anela")]
    [InlineData("Unknown User", "Anela")]
    [InlineData("Anonymous", "Anela")]
    [InlineData("   ", "Anela")]
    public void ExtractFirstName_ReturnsExpected(string? input, string expected)
    {
        SmartsuppNameHelper.ExtractFirstName(input).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test — expect FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai
dotnet test backend/test/Anela.Heblo.Tests --filter "SmartsuppNameHelperTests" --no-build 2>&1 | tail -5
```

Expected: compilation error — `SmartsuppNameHelper` does not exist.

- [ ] **Step 3: Implement the helper**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppNameHelper.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp;

public static class SmartsuppNameHelper
{
    private static readonly HashSet<string> FallbackNames =
        new(StringComparer.OrdinalIgnoreCase) { "Unknown User", "Anonymous" };

    public static string ExtractFirstName(string? fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || FallbackNames.Contains(trimmed))
            return "Anela";

        var firstName = trimmed.Split(' ')[0];
        return firstName.Length == 0 ? "Anela" : firstName;
    }
}
```

- [ ] **Step 4: Build and run test — expect PASS**

```bash
dotnet build backend/src/Anela.Heblo.Application --no-incremental -q
dotnet test backend/test/Anela.Heblo.Tests --filter "SmartsuppNameHelperTests" -v minimal
```

Expected: `8 passed`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppNameHelper.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppNameHelperTests.cs
git commit -m "feat(smartsupp): add SmartsuppNameHelper.ExtractFirstName utility"
```

---

## Task 2: Personalize agent name in draft-reply prompt

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`

- [ ] **Step 1: Add ICurrentUserService mock and write failing tests**

In `GenerateDraftReplyHandlerTests.cs`, add these fields and helpers after the existing mock declarations (line 20):

```csharp
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private void SetupCurrentUser(string name = "Ondřej Pajgrt") =>
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", name, "ondra@anela.cz", true));
```

Update `CreateHandler` to pass `_currentUserService.Object`:

```csharp
    private GenerateDraftReplyHandler CreateHandler(SmartsuppDraftReplyOptions? options = null) =>
        new(_repo.Object, _mediator.Object, _chatClient.Object,
            Options.Create(options ?? new SmartsuppDraftReplyOptions()),
            _currentUserService.Object,
            _logger.Object);
```

Also add the missing `using` directives at the top of the test file:

```csharp
using Anela.Heblo.Domain.Features.Users;
```

Add two new test methods at the end of the class:

```csharp
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
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "GenerateDraftReplyHandlerTests" --no-build 2>&1 | tail -5
```

Expected: compilation error — constructor does not accept `ICurrentUserService`.

- [ ] **Step 3: Update SmartsuppDraftReplyOptions.cs — add {agent_name} to prompt**

Replace the `DraftReplySystemPrompt` default value in `SmartsuppDraftReplyOptions.cs`. Replace the existing property body (lines 17–44) with:

```csharp
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

        Identita agenta:
        - Tvoje jméno je {agent_name}. Pokud odpověď podepisuješ, podepiš se
          vždy jako {agent_name}.
        - Jména, která se objevují v kontextu z databáze znalostí (např. autoři
          dokumentů), nejsou tvoje jméno a nesmíš se jimi podepisovat ani je
          v odpovědi uvádět jako autora.

        Obsah odpovědi:
        - Vycházej výhradně z poskytnutého kontextu z databáze znalostí.
          Nevymýšlej informace, které v kontextu nejsou.
        - Pokud kontext neobsahuje relevantní informaci, napiš zdvořilou
          odpověď, že se zákazníkům ozveš s upřesněním.
        - Zaměř se na téma: {topic}

        Kontext z databáze znalostí:
        {context}

        Probíhající konverzace:
        {transcript}

        Napiš pouze samotný text návrhu odpovědi, bez jakéhokoli úvodu.
        """;
```

- [ ] **Step 4: Update GenerateDraftReplyHandler.cs — inject ICurrentUserService and replace placeholder**

Add using directive at the top of `GenerateDraftReplyHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Users;
```

Add field and constructor parameter. Replace the class declaration and constructor:

```csharp
public class GenerateDraftReplyHandler
    : IRequestHandler<GenerateDraftReplyRequest, GenerateDraftReplyResponse>
{
    private const int RetrievalTopK = 5;
    private const int MaxExcerptLength = 200;
    private const int MaxRetrievalQueryLength = 2000;
    private const string NoContextPlaceholder = "(žádný relevantní kontext nebyl nalezen)";
    private const string NoTopicPlaceholder = "(neuvedeno)";

    private readonly ISmartsuppRepository _repository;
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly SmartsuppDraftReplyOptions _options;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GenerateDraftReplyHandler> _logger;

    public GenerateDraftReplyHandler(
        ISmartsuppRepository repository,
        IMediator mediator,
        IChatClient chatClient,
        IOptions<SmartsuppDraftReplyOptions> options,
        ICurrentUserService currentUserService,
        ILogger<GenerateDraftReplyHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
        _currentUserService = currentUserService;
        _logger = logger;
    }
```

In the `Handle` method, replace the `systemPrompt` line:

```csharp
        var agentName = SmartsuppNameHelper.ExtractFirstName(_currentUserService.GetCurrentUser().Name);

        var systemPrompt = _options.DraftReplySystemPrompt
            .Replace("{agent_name}", agentName)
            .Replace("{transcript}", transcript)
            .Replace("{context}", context)
            .Replace("{topic}", topic ?? NoTopicPlaceholder);
```

- [ ] **Step 5: Build and run tests — expect PASS**

```bash
dotnet build backend/src/Anela.Heblo.Application -q
dotnet test backend/test/Anela.Heblo.Tests --filter "GenerateDraftReplyHandlerTests" -v minimal
```

Expected: all existing tests + 2 new tests pass (total 12 tests).

- [ ] **Step 6: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests -v minimal 2>&1 | tail -5
```

Expected: no failures.

- [ ] **Step 7: Format and commit**

```bash
dotnet format backend/src/Anela.Heblo.Application
dotnet format backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/SmartsuppDraftReplyOptions.cs \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs
git commit -m "feat(smartsupp): personalize draft-reply signature with logged-in user's first name"
```

---

## Task 3: Error code + domain type for SendMessage

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`

- [ ] **Step 1: Add error code**

In `ErrorCodes.cs`, after `SmartsuppConversationEmpty = 2703,` add:

```csharp
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    SmartsuppSendMessageUnavailable = 2704,
```

- [ ] **Step 2: Add SmartsuppSentMessageData and extend ISmartsuppApiClient**

In `ISmartsuppApiClient.cs`, add a new class after `SmartsuppContactData`:

```csharp
public class SmartsuppSentMessageData
{
    public string Id { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
```

Add a new method to the `ISmartsuppApiClient` interface:

```csharp
    /// <summary>
    /// Sends a text message in a conversation on behalf of an agent.
    /// </summary>
    /// <remarks>
    /// VERIFY the exact request shape against https://docs.smartsupp.com/rest-api
    /// before relying on this implementation. The POST /v2/conversations/{id}/messages
    /// endpoint body and auth requirements must be confirmed against the live docs.
    /// </remarks>
    Task<SmartsuppSentMessageData> SendMessageAsync(
        string conversationId,
        string content,
        string? agentName,
        CancellationToken cancellationToken);
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
dotnet build backend -q
```

Expected: no errors (the interface now has a new method; `SmartsuppApiClient` does not implement it yet — this will be a compile error. Fix by adding a stub or implement in the next task).

> Note: The build will fail because `SmartsuppApiClient` does not yet implement `SendMessageAsync`. Proceed to Task 4 immediately. If you want to commit, stub the method first:
>
> In `SmartsuppApiClient.cs` add at the end of the class (before the closing brace):
> ```csharp
> public Task<SmartsuppSentMessageData> SendMessageAsync(
>     string conversationId, string content, string? agentName,
>     CancellationToken cancellationToken) => throw new NotImplementedException();
> ```
> Then commit:
> ```bash
> git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
>         backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs \
>         backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs
> git commit -m "feat(smartsupp): add SmartsuppSendMessageUnavailable error code and SendMessageAsync interface"
> ```

---

## Task 4: Implement SmartsuppApiClient.SendMessageAsync

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`

- [ ] **Step 1: Write failing API client test**

> **IMPORTANT:** Before writing code, verify the Smartsupp v2 API endpoint for sending messages.
> Open https://docs.smartsupp.com/rest-api and find `POST /v2/conversations/{id}/messages`.
> Confirm: the request body shape, the auth method (Bearer token), and the response shape.
> The implementation below uses the most likely shape; adjust if the docs differ.

Add these test cases to `SmartsuppApiClientTests.cs` at the end of the class:

```csharp
    [Fact]
    public async Task SendMessageAsync_ReturnsMessageData_WhenApiResponds()
    {
        // Arrange — response shape must match the actual Smartsupp v2 POST /conversations/{id}/messages response
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "msNewMessage123",
            created_at = "2026-05-20T10:00:00Z",
            type = "message",
            sub_type = "agent",
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("conversations/conv123/messages")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.SendMessageAsync("conv123", "Dobrý den!", "Ondřej", CancellationToken.None);

        // Assert
        result.Id.Should().Be("msNewMessage123");
        result.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsHttpRequestException_OnErrorResponse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        var act = () => client.SendMessageAsync("conv123", "Text", null, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
```

- [ ] **Step 2: Run test — expect FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "SendMessageAsync" --no-build 2>&1 | tail -5
```

Expected: `NotImplementedException` (or compile error if stub not added in Task 3).

- [ ] **Step 3: Implement SendMessageAsync in SmartsuppApiClient.cs**

Remove the stub (if added in Task 3) and add the full implementation.

Add private inner classes at the end of `SmartsuppApiClient.cs` (before the closing brace), in the "API request shapes" section:

```csharp
    private sealed class SendMessageApiRequest
    {
        public SendMessageApiContent Content { get; init; } = null!;
        public SendMessageApiAgent? Agent { get; init; }
    }

    private sealed class SendMessageApiContent
    {
        public string Type { get; init; } = "text";
        public string Text { get; init; } = null!;
    }

    private sealed class SendMessageApiAgent
    {
        public string? Name { get; init; }
    }

    private sealed class SendMessageApiResponse
    {
        public string? Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }
```

Add the method implementation (replace the stub or add after `GetConversationAsync`):

```csharp
    public async Task<SmartsuppSentMessageData> SendMessageAsync(
        string conversationId,
        string content,
        string? agentName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        var body = new SendMessageApiRequest
        {
            Content = new SendMessageApiContent { Text = content },
            Agent = agentName is not null ? new SendMessageApiAgent { Name = agentName } : null,
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.BaseUrl}conversations/{conversationId}/messages");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp send message failed {Status}: {Body}",
                    response.StatusCode, errorBody);
                var ex = new HttpRequestException(
                    $"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SendMessageApiResponse>(raw, JsonOptions);

            return new SmartsuppSentMessageData
            {
                Id = result?.Id ?? string.Empty,
                CreatedAt = result?.CreatedAt ?? DateTime.UtcNow,
            };
        }, cancellationToken);
    }
```

- [ ] **Step 4: Build and run tests — expect PASS**

```bash
dotnet build backend -q
dotnet test backend/test/Anela.Heblo.Tests --filter "SendMessageAsync" -v minimal
```

Expected: 2 new tests pass.

- [ ] **Step 5: Commit**

```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp
git add backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs \
        backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs \
        backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs
git commit -m "feat(smartsupp): implement SendMessageAsync on SmartsuppApiClient"
```

---

## Task 5: SendMessage use case — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageValidator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SendMessageHandlerTests.cs`

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SendMessageHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SendMessageHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<ILogger<SendMessageHandler>> _logger = new();

    private SendMessageHandler CreateHandler() =>
        new(_repo.Object, _apiClient.Object, _currentUserService.Object, _logger.Object);

    private void SetupConversation(bool exists = true) =>
        _repo.Setup(r => r.GetConversationAsync("conv1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists
                ? new SmartsuppConversation { Id = "conv1", Status = SmartsuppConversationStatus.Open, Messages = [] }
                : null);

    private void SetupCurrentUser(string name = "Ondřej Pajgrt") =>
        _currentUserService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("1", name, "ondra@anela.cz", true));

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
    public async Task Handle_PassesAgentFirstNameToApiClient()
    {
        SetupConversation();
        SetupCurrentUser("Jana Nováková");
        SetupApiSuccess();

        await CreateHandler().Handle(
            new SendMessageRequest { ConversationId = "conv1", Content = "Text" },
            CancellationToken.None);

        _apiClient.Verify(c => c.SendMessageAsync(
            "conv1", "Text", "Jana", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "SendMessageHandlerTests" --no-build 2>&1 | tail -5
```

Expected: compilation error — types do not exist yet.

- [ ] **Step 3: Create SendMessageRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageRequest : IRequest<SendMessageResponse>
{
    public string ConversationId { get; set; } = null!;
    public string Content { get; set; } = null!;
}
```

- [ ] **Step 4: Create SendMessageResponse.cs**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageResponse : BaseResponse
{
    public string MessageId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public SendMessageResponse() { }
    public SendMessageResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create SendMessageValidator.cs**

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageValidator : AbstractValidator<SendMessageRequest>
{
    private const int MaxContentLength = 4000;

    public SendMessageValidator()
    {
        RuleFor(r => r.ConversationId).NotEmpty();
        RuleFor(r => r.Content)
            .NotEmpty()
            .MaximumLength(MaxContentLength);
    }
}
```

- [ ] **Step 6: Create SendMessageHandler.cs**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageHandler : IRequestHandler<SendMessageRequest, SendMessageResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ICurrentUserService currentUserService,
        ILogger<SendMessageHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new SendMessageResponse(ErrorCodes.SmartsuppConversationNotFound);

        var agentName = SmartsuppNameHelper.ExtractFirstName(_currentUserService.GetCurrentUser().Name);

        SmartsuppSentMessageData sent;
        try
        {
            sent = await _apiClient.SendMessageAsync(
                request.ConversationId, request.Content, agentName, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       or ObjectDisposedException
                                       || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
        {
            _logger.LogWarning(ex, "Smartsupp API unavailable while sending message to {ConversationId}",
                request.ConversationId);
            return new SendMessageResponse(ErrorCodes.SmartsuppSendMessageUnavailable);
        }

        return new SendMessageResponse
        {
            MessageId = sent.Id,
            CreatedAt = sent.CreatedAt,
        };
    }
}
```

- [ ] **Step 7: Build and run tests — expect PASS**

```bash
dotnet build backend -q
dotnet test backend/test/Anela.Heblo.Tests --filter "SendMessageHandlerTests" -v minimal
```

Expected: 4 tests pass.

- [ ] **Step 8: Format and commit**

```bash
dotnet format backend/src/Anela.Heblo.Application
dotnet format backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/ \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SendMessageHandlerTests.cs
git commit -m "feat(smartsupp): add SendMessage use case (request/validator/handler)"
```

---

## Task 6: Controller endpoint + module registration

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`

- [ ] **Step 1: Add using directives and endpoint to SmartsuppController.cs**

Add the using import at the top of the file:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
```

Add a new request body class after `GenerateDraftReplyBody`:

```csharp
public sealed class SendMessageBody
{
    public string Content { get; set; } = string.Empty;
}
```

Add the new action method inside `SmartsuppController`, after `GenerateDraftReply`:

```csharp
    [HttpPost("conversations/{conversationId}/messages")]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(
        string conversationId,
        [FromBody] SendMessageBody body,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest { ConversationId = conversationId, Content = body.Content };
        var result = await _mediator.Send(request, cancellationToken);
        return HandleResponse(result);
    }
```

- [ ] **Step 2: Register validator + pipeline in SmartsuppModule.cs**

Add using directives at the top of `SmartsuppModule.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
```

Add registrations in `AddSmartsuppModule`, after the existing `ListConversationsValidator` lines:

```csharp
        services.AddScoped<IValidator<SendMessageRequest>, SendMessageValidator>();
        services.AddScoped<IPipelineBehavior<SendMessageRequest, SendMessageResponse>,
            ValidationBehavior<SendMessageRequest, SendMessageResponse>>();
```

- [ ] **Step 3: Build the full solution**

```bash
dotnet build backend -q
```

Expected: no errors.

- [ ] **Step 4: Run all backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests -v minimal 2>&1 | tail -5
```

Expected: all tests pass.

- [ ] **Step 5: Format and commit**

```bash
dotnet format backend
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs
git commit -m "feat(smartsupp): add POST /api/smartsupp/conversations/{id}/messages endpoint"
```

---

## Task 7: Frontend — useSendMessage hook (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`
- Create: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts`

> After completing Tasks 4–6, run `npm run build` inside `frontend/` to regenerate the OpenAPI TypeScript client. The new `POST /api/smartsupp/conversations/{id}/messages` endpoint becomes available, but we call it via `http.fetch` directly (like the existing hooks) to use absolute URLs.

- [ ] **Step 1: Write the failing hook tests**

Create `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts`:

```typescript
import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useSendMessage } from "../useSendMessage";
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
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockFetch.mockReset();
  jest.restoreAllMocks();
});

describe("useSendMessage", () => {
  it("calls the correct endpoint and returns messageId on success", async () => {
    setApiResponse(200, { success: true, messageId: "ms123", createdAt: "2026-05-20T10:00:00Z" });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Dobrý den!"));

    await waitFor(() => expect(result.current.justSent).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      "http://api.test/api/smartsupp/conversations/conv1/messages",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ content: "Dobrý den!" }),
      }),
    );
  });

  it("sets error message on API failure", async () => {
    setApiResponse(503, { success: false, errorCode: "SmartsuppSendMessageUnavailable" });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo|nedostupn/i);
  });

  it("does nothing when conversationId is null", async () => {
    const { result } = renderHook(() => useSendMessage(null), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("isPending is true while request is in flight", async () => {
    let resolvePromise!: (v: unknown) => void;
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      baseUrl: "http://api.test",
      http: {
        fetch: () => new Promise((res) => { resolvePromise = res; }),
      },
    });

    const { result } = renderHook(() => useSendMessage("conv1"), { wrapper });
    act(() => result.current.send("Text"));

    await waitFor(() => expect(result.current.isPending).toBe(true));
    resolvePromise({ ok: true, status: 200, json: async () => ({ success: true }) });
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai/frontend
npx jest hooks/__tests__/useSendMessage --no-coverage 2>&1 | tail -5
```

Expected: module not found error.

- [ ] **Step 3: Implement useSendMessage.ts**

Create `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`:

```typescript
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import { SMARTSUPP_QUERY_KEYS, type GetConversationResponse, type MessageDto } from "../../../../api/hooks/useSmartsupp";

interface SendMessageApiResponse {
  success: boolean;
  errorCode?: string;
  messageId?: string;
  createdAt?: string;
}

const SEND_ERROR_MESSAGES: Record<string, string> = {
  SmartsuppSendMessageUnavailable: "Odeslání zprávy selhalo. Zkuste to prosím znovu.",
  SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
};

function messageForSendError(code?: string): string {
  if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code];
  return "Nepodařilo se odeslat zprávu.";
}

interface UseSendMessageResult {
  send: (content: string) => void;
  isPending: boolean;
  error: string | null;
  justSent: boolean;
  clearSent: () => void;
}

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  const mutation = useMutation<void, Error, string>({
    mutationFn: async (content) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      const apiClient = getAuthenticatedApiClient();
      const baseUrl = (apiClient as unknown as { baseUrl: string }).baseUrl;
      const http = (apiClient as unknown as {
        http: { fetch: (url: string, init: RequestInit) => Promise<Response> };
      }).http;

      const response = await http.fetch(
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/messages`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ content }),
        },
      );

      const data = (await response.json()) as SendMessageApiResponse;
      if (!response.ok || !data.success) {
        throw new Error(messageForSendError(data?.errorCode));
      }
    },
    onMutate: async (content) => {
      if (!conversationId) return;
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticMsg: MessageDto = {
        id: `optimistic-${Date.now()}`,
        authorType: "agent",
        content,
        createdAt: new Date().toISOString(),
        isFirstReply: false,
      };
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (old) => (old ? { ...old, messages: [...old.messages, optimisticMsg] } : old),
      );
      return { previous };
    },
    onError: (_err, _content, context) => {
      const ctx = context as { previous?: GetConversationResponse } | undefined;
      if (ctx?.previous !== undefined && conversationId) {
        queryClient.setQueryData(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          ctx.previous,
        );
      }
    },
    onSettled: () => {
      if (conversationId) {
        queryClient.invalidateQueries({
          queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        });
      }
    },
  });

  return {
    send: (content: string) => mutation.mutate(content),
    isPending: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    justSent: mutation.isSuccess,
    clearSent: mutation.reset,
  };
}
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
npx jest hooks/__tests__/useSendMessage --no-coverage 2>&1 | tail -8
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai
git add frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts \
        frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts
git commit -m "feat(smartsupp): add useSendMessage React Query mutation hook"
```

---

## Task 8: Frontend — Enable Send button in ChatComposer

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx`

- [ ] **Step 1: Write the failing test updates**

In `ChatComposer.test.tsx`:

1. Add a mock for `useSendMessage` at the top of the file (after the existing `draftReplyHook` mock):

```typescript
import * as sendMessageHook from "../hooks/useSendMessage";

const sendFn = jest.fn();
const clearSent = jest.fn();

function mockSendHook(overrides: Partial<ReturnType<typeof sendMessageHook.useSendMessage>>) {
  jest.spyOn(sendMessageHook, "useSendMessage").mockReturnValue({
    send: sendFn,
    isPending: false,
    error: null,
    justSent: false,
    clearSent,
    ...overrides,
  });
}
```

2. Update `beforeEach` to reset the new mocks AND set up a default idle `useSendMessage` spy so all existing tests still work without needing to call `mockSendHook` individually. `mockSendHook({})` must be called AFTER `jest.restoreAllMocks()` so it survives the restore:

```typescript
beforeEach(() => {
  generate.mockReset();
  reset.mockReset();
  sendFn.mockReset();
  clearSent.mockReset();
  jest.restoreAllMocks();
  mockSendHook({}); // default: idle, non-pending, no error
});
```

3. Update the test "renders an empty textarea and a disabled Send button" — the Send button should now be **enabled** only when content is non-empty. Update the test name and assertion:

```typescript
  it("renders an empty textarea and a disabled Send button when draft is empty", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    expect(screen.getByRole("button", { name: /odeslat/i })).toBeDisabled();
  });
```

3. Add new test cases at the end of the `describe` block:

```typescript
  it("enables Send button when draft is non-empty", () => {
    mockHook({});
    mockSendHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "Odpověď" },
    });
    expect(screen.getByRole("button", { name: /odeslat/i })).not.toBeDisabled();
  });

  it("calls send with the draft content when Send is clicked", () => {
    mockHook({});
    mockSendHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "Odpověď zákazníkovi" },
    });
    fireEvent.click(screen.getByRole("button", { name: /odeslat/i }));
    expect(sendFn).toHaveBeenCalledWith("Odpověď zákazníkovi");
  });

  it("disables Send button and shows sending state while isPending", () => {
    mockHook({});
    mockSendHook({ isPending: true });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" initialDraft="Text" />);
    const btn = screen.getByRole("button", { name: /odeslat|odesílám/i });
    expect(btn).toBeDisabled();
  });

  it("clears draft and calls onDraftChange after successful send", async () => {
    const onDraftChange = jest.fn();
    mockHook({});
    mockSendHook({ justSent: true, clearSent });
    render(
      <ChatComposer
        conversationId="c1"
        lastContactMessage="Dobrý den"
        initialDraft="Text k odeslání"
        onDraftChange={onDraftChange}
      />,
    );
    await waitFor(() => {
      const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
      expect(textarea.value).toBe("");
    });
    expect(onDraftChange).toHaveBeenLastCalledWith("");
    expect(clearSent).toHaveBeenCalled();
  });
```

- [ ] **Step 2: Run tests — expect some FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai/frontend
npx jest __tests__/ChatComposer --no-coverage 2>&1 | tail -10
```

Expected: failures on the new tests (Send button still disabled in implementation).

- [ ] **Step 3: Update ChatComposer.tsx**

Replace the entire file content. Key changes:
1. Import `useSendMessage`
2. Add `useEffect` for `justSent`
3. Add `handleSend` function
4. Update the Send button JSX

```typescript
import { useEffect, useState } from "react";
import { Loader2, Maximize2, Minimize2, Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";
import { useSendMessage } from "./hooks/useSendMessage";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
  initialDraft?: string;
  onDraftChange?: (draft: string) => void;
}

const MAX_CHARS = 4000;

function ChatComposer({ conversationId, lastContactMessage, initialDraft, onDraftChange }: ChatComposerProps) {
  const [draft, setDraft] = useState(initialDraft ?? "");
  const [isAiDraft, setIsAiDraft] = useState(false);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);
  const [pendingTopic, setPendingTopic] = useState<{ topic: string | undefined } | null>(null);
  const [isExpanded, setIsExpanded] = useState(false);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);
  const { send, isPending: isSending, justSent, clearSent } = useSendMessage(conversationId);

  useEffect(() => {
    if (result) {
      const answer = result.answer.slice(0, MAX_CHARS);
      setDraft(answer);
      setSources(result.sources);
      setIsAiDraft(true);
      onDraftChange?.(answer);
      reset();
    }
  }, [result, reset, onDraftChange]);

  useEffect(() => {
    if (justSent) {
      setDraft("");
      setSources([]);
      setIsAiDraft(false);
      setLastTopic(undefined);
      setPendingTopic(null);
      onDraftChange?.("");
      clearSent();
    }
  }, [justSent, clearSent, onDraftChange]);

  const canGenerateWithoutTopic =
    lastContactMessage !== null && lastContactMessage.trim() !== "";

  const requestGeneration = (topic?: string) => {
    if (draft.trim() !== "" && !isAiDraft) {
      setPendingTopic({ topic });
      return;
    }
    setLastTopic(topic);
    generate(topic);
  };

  const confirmOverwrite = () => {
    if (pendingTopic === null) return;
    setLastTopic(pendingTopic.topic);
    generate(pendingTopic.topic);
    setPendingTopic(null);
  };

  const cancelOverwrite = () => setPendingTopic(null);

  const handleDraftChange = (value: string) => {
    const trimmed = value.slice(0, MAX_CHARS);
    setDraft(trimmed);
    onDraftChange?.(trimmed);
    if (isAiDraft) {
      setIsAiDraft(false);
    }
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setIsAiDraft(false);
    setLastTopic(undefined);
    setPendingTopic(null);
    onDraftChange?.("");
  };

  const handleSend = () => {
    if (!draft.trim() || isSending) return;
    send(draft);
  };

  const isBusy = isLoading || isSending;

  return (
    <div className="flex flex-col">
      <DraftReplyTriggerBar
        disabled={isBusy}
        canGenerateWithoutTopic={canGenerateWithoutTopic}
        error={error}
        onGenerate={requestGeneration}
      />
      {pendingTopic !== null && (
        <div className="flex items-center justify-between border-t border-amber-200 bg-amber-50 px-4 py-2 text-xs">
          <span className="text-amber-800">Přepsat rozepsanou odpověď?</span>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={confirmOverwrite}
              className="font-medium text-amber-800 hover:text-amber-900"
            >
              Přepsat
            </button>
            <button
              type="button"
              onClick={cancelOverwrite}
              className="text-gray-500 hover:text-gray-700"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
      <div className="flex flex-col gap-2 border-t border-gray-200 bg-white p-3">
        {isAiDraft && (
          <DraftReplyToolbar
            sources={sources}
            disabled={isBusy}
            onRegenerate={() => generate(lastTopic)}
            onDiscard={handleDiscard}
          />
        )}
        <div className="relative">
          <textarea
            value={draft}
            disabled={isBusy}
            onChange={(e) => handleDraftChange(e.target.value)}
            placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
            rows={isExpanded ? 14 : 5}
            className="w-full resize-none rounded-md border border-gray-200 py-2 pl-3 pr-9 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50"
          />
          <button
            type="button"
            onClick={() => setIsExpanded((v) => !v)}
            aria-label={isExpanded ? "Zmenšit" : "Zvětšit"}
            title={isExpanded ? "Zmenšit" : "Zvětšit"}
            className="absolute right-2 top-2 rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            {isExpanded ? (
              <Minimize2 className="h-4 w-4" />
            ) : (
              <Maximize2 className="h-4 w-4" />
            )}
          </button>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-xs text-gray-400">
            {draft.length} / {MAX_CHARS}
          </span>
          <button
            type="button"
            onClick={handleSend}
            disabled={!draft.trim() || isBusy}
            aria-label="Odeslat"
            className="inline-flex items-center gap-2 rounded-md bg-blue-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-600 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isSending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Send className="h-4 w-4" />
            )}
            {isSending ? "Odesílám…" : "Odeslat"}
          </button>
        </div>
      </div>
    </div>
  );
}

export default ChatComposer;
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
npx jest __tests__/ChatComposer --no-coverage 2>&1 | tail -10
```

Expected: all tests pass (original + new).

- [ ] **Step 5: Run the full frontend test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai/frontend
npm run test -- --no-coverage 2>&1 | tail -10
```

Expected: no new failures.

- [ ] **Step 6: Frontend build and lint**

```bash
npm run build
npm run lint
```

Expected: no errors. (A fresh TypeScript build also re-generates the OpenAPI client from the backend OpenAPI spec if the build pipeline triggers it; if not, it's fine — the hook calls the API directly.)

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/shanghai
git add frontend/src/components/customer-support/smartsupp/ChatComposer.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx
git commit -m "feat(smartsupp): enable Send button in ChatComposer and wire useSendMessage"
```

---

## Final validation (per CLAUDE.md)

- [ ] `dotnet build` — full solution, no errors
- [ ] `dotnet format` — no format drift
- [ ] `dotnet test backend/test/Anela.Heblo.Tests` — all tests pass
- [ ] `npm run build` (frontend) — no errors
- [ ] `npm run lint` (frontend) — no warnings
- [ ] Manual smoke test on staging Smartsupp data:
  1. Log in as a real user (not Unknown User)
  2. Open a Smartsupp conversation
  3. Generate a draft reply — verify it signs with the logged-in user's first name, NOT "Janka"
  4. Type or edit the draft
  5. Click Send — verify optimistic message appears immediately
  6. After refresh — verify message is in Smartsupp dashboard
  7. Try on a conversation that no longer exists in Smartsupp — verify graceful error

---

## Implementation order recap

1. Task 1: `SmartsuppNameHelper` + tests
2. Task 2: Draft-reply handler + prompt personalization
3. Task 3: Error code + domain interface
4. Task 4: API client implementation + tests
5. Task 5: `SendMessage` use case + tests
6. Task 6: Controller endpoint + DI registration
7. Task 7: `useSendMessage` hook + tests
8. Task 8: `ChatComposer` wire-up + tests
