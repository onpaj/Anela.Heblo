# Meeting Task Extraction Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix silent extraction failure in MeetingTasks so that transcripts ingested via Plaud actually produce proposed tasks and matched user emails.

**Architecture:** The root cause is `ClaudeMeetingTaskExtractor` sharing a global `IChatClient` capped at 1024 output tokens, causing truncated JSON that is silently swallowed. The fix has four prongs: (1) give the extractor its own token budget via per-call `ChatOptions`, (2) register a separate raw `IChatClient` that bypasses `PostAnswerEnrichmentMiddleware`, (3) upgrade `ReimportMeetingTranscript` to re-extract pending tasks so existing broken transcripts can be repaired, and (4) fix user resolution for "Andy" and compound names.

**Tech Stack:** .NET 8, C#, xUnit + FluentAssertions + Moq, Microsoft.Extensions.AI (M.E.AI) `IChatClient`, EF Core 8, `AnthropicChatClient` custom HTTP client

---

## File Map

| File | Change |
|------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs` | Honor `options.MaxOutputTokens` in `BuildRequestBody` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` | Register keyed raw `IChatClient` for extraction |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` | Pass large token budget; log Error (not Warning); distinguish JSON vs API failure |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` | Wire `IMeetingTaskExtractor` to keyed raw client via factory |
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` | Add `ReplacePendingTasksAsync` |
| `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` | Implement `ReplacePendingTasksAsync` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` | Inject extractor + directory; re-extract + re-resolve pending tasks |
| `backend/src/Anela.Heblo.API/meeting-users.json` | Add "Andy" alias to Andrea Pajgrt Bartošová |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs` | Split compound assignees on `&`/`,` |
| `backend/test/Anela.Heblo.Tests/Adapters/Anthropic/AnthropicChatClientTests.cs` | Tests for per-call `MaxOutputTokens` |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs` | Tests for token budget passed; error-level logging; JSON vs API failure distinction |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs` | Update existing + add re-extraction tests |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingUserDirectoryTests.cs` | Tests for compound assignee splitting |

---

## Task 1: Honor `ChatOptions.MaxOutputTokens` in `AnthropicChatClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:130-149`
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/Anthropic/AnthropicChatClientTests.cs`

- [ ] **Step 1.1: Write the failing tests**

Add two tests after the existing `AnthropicOptions_DefaultHttpTimeoutSeconds_Is60` test in `AnthropicChatClientTests.cs`:

```csharp
[Fact]
public async Task GetResponseAsync_WithMaxOutputTokensOption_OverridesDefaultMaxTokens()
{
    HttpRequestMessage? capturedRequest = null;
    var handlerMock = new Mock<HttpMessageHandler>();
    handlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildSuccessJson("ok"))
        });

    var client = CreateClient(handler: handlerMock.Object);
    var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };
    var options = new ChatOptions { MaxOutputTokens = 8192 };

    await client.GetResponseAsync(messages, options);

    capturedRequest.Should().NotBeNull();
    var body = await capturedRequest!.Content!.ReadAsStringAsync();
    var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(8192);
}

[Fact]
public async Task GetResponseAsync_WithoutMaxOutputTokensOption_UsesDefaultMaxTokens()
{
    HttpRequestMessage? capturedRequest = null;
    var handlerMock = new Mock<HttpMessageHandler>();
    handlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildSuccessJson("ok"))
        });

    var client = CreateClient(handler: handlerMock.Object);
    var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

    await client.GetResponseAsync(messages);

    var body = await capturedRequest!.Content!.ReadAsStringAsync();
    var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1024);
}
```

Also add `using System.Text.Json;` to the test file's using directives if not already present.

- [ ] **Step 1.2: Run tests to verify they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/douala-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AnthropicChatClientTests.GetResponseAsync_WithMaxOutputTokensOption_OverridesDefaultMaxTokens|AnthropicChatClientTests.GetResponseAsync_WithoutMaxOutputTokensOption_UsesDefaultMaxTokens" \
  --no-build 2>&1 | tail -20
```

Expected: both tests fail (the behavior doesn't exist yet).

- [ ] **Step 1.3: Implement the fix in `AnthropicChatClient.cs`**

Replace the `BuildRequestBody` private method and add `maxTokensOverride` extraction in `GetResponseAsync`:

In `GetResponseAsync`, after `var model = options?.ModelId ?? _options.Model;`, add:
```csharp
var maxTokensOverride = options?.MaxOutputTokens;
```

Then change the call from:
```csharp
var requestBody = BuildRequestBody(model, systemMessage, userMessages);
```
to:
```csharp
var requestBody = BuildRequestBody(model, systemMessage, userMessages, maxTokensOverride);
```

Replace `BuildRequestBody` with:
```csharp
private object BuildRequestBody(string model, string? systemMessage, object[] userMessages, int? maxTokensOverride = null)
{
    var maxTokens = maxTokensOverride ?? _options.MaxTokens;
    if (systemMessage is not null)
    {
        return new
        {
            model,
            max_tokens = maxTokens,
            system = systemMessage,
            messages = userMessages
        };
    }

    return new
    {
        model,
        max_tokens = maxTokens,
        messages = userMessages
    };
}
```

- [ ] **Step 1.4: Run the new tests to verify they pass**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AnthropicChatClientTests" \
  --no-build 2>&1 | tail -20
```

Expected: all `AnthropicChatClientTests` tests pass.

- [ ] **Step 1.5: Build to verify no regressions**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 1.6: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/douala-v1
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/Anthropic/AnthropicChatClientTests.cs
git commit -m "feat(anthropic): honor ChatOptions.MaxOutputTokens per call"
```

---

## Task 2: Register raw keyed `IChatClient` for meeting extraction

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`

- [ ] **Step 2.1: Add the keyed raw client registration**

In `AnthropicAdapterServiceCollectionExtensions.cs`, add a public constant for the key and register the raw client after the existing enriched client registration:

Add at the class level:
```csharp
public const string MeetingExtractionClientKey = "meeting-extractor";
```

After the `.Use((inner, sp) => new PostAnswerEnrichmentMiddleware(...))` chain ends, add:
```csharp
services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
    new AnthropicChatClient(
        sp.GetRequiredService<IOptions<AnthropicOptions>>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<AnthropicChatClient>>()));
```

The full updated method body becomes:

```csharp
public static IServiceCollection AddAnthropicAdapter(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AnthropicOptions>(opts =>
    {
        opts.ApiKey = configuration["Anthropic:ApiKey"] ?? "";
        opts.Model = configuration["KnowledgeBase:ChatModel"] ?? opts.Model;
        opts.MaxTokens = configuration.GetValue("KnowledgeBase:ChatMaxTokens", opts.MaxTokens);
        opts.HttpTimeoutSeconds = configuration.GetValue("Anthropic:HttpTimeoutSeconds", opts.HttpTimeoutSeconds);
    });

    services.AddHttpClient("Anthropic", (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
    });

    services.AddChatClient(sp =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()))
        .UseLogging()
        .Use((inner, sp) => new PostAnswerEnrichmentMiddleware(inner, sp.GetRequiredService<IProductEnrichmentCache>()));

    services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
        new AnthropicChatClient(
            sp.GetRequiredService<IOptions<AnthropicOptions>>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<AnthropicChatClient>>()));

    return services;
}
```

- [ ] **Step 2.2: Build to verify it compiles**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2.3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs
git commit -m "feat(anthropic): register keyed raw IChatClient for meeting extraction"
```

---

## Task 3: Update `ClaudeMeetingTaskExtractor` — token budget, error logging, tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`

- [ ] **Step 3.1: Write failing tests for token budget and error logging**

Add the following tests to `ClaudeMeetingTaskExtractorTests.cs`:

```csharp
[Fact]
public async Task ExtractAsync_PassesMaxOutputTokens8192ToChatClient()
{
    ChatOptions? capturedOptions = null;
    _mockChatClient
        .Setup(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "[]")]));

    await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

    capturedOptions.Should().NotBeNull();
    capturedOptions!.MaxOutputTokens.Should().Be(8192);
}

[Fact]
public async Task ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty()
{
    _mockChatClient
        .Setup(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "not-valid-json{{{")]));

    var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

    result.Should().BeEmpty();
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("malformed JSON")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task ExtractAsync_WhenApiThrows_LogsErrorAndReturnsEmpty()
{
    _mockChatClient
        .Setup(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("API error"));

    var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

    result.Should().BeEmpty();
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("extraction failed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

Also add `using Microsoft.Extensions.Logging;` if not already present.

Note: the existing test `ExtractAsync_WhenChatClientThrows_ReturnsEmptyList` will still pass (the exception is still caught and returns empty), but it won't check log level. The new `ExtractAsync_WhenApiThrows_LogsErrorAndReturnsEmpty` explicitly verifies `LogLevel.Error`.

- [ ] **Step 3.2: Run new tests to verify they fail**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests.ExtractAsync_PassesMaxOutputTokens8192|ClaudeMeetingTaskExtractorTests.ExtractAsync_WhenJsonInvalid_LogsError|ClaudeMeetingTaskExtractorTests.ExtractAsync_WhenApiThrows_LogsError" \
  --no-build 2>&1 | tail -20
```

Expected: all three fail.

- [ ] **Step 3.3: Update `ClaudeMeetingTaskExtractor.cs`**

Replace the entire `ExtractAsync` method body:

```csharp
public async Task<List<ExtractedTask>> ExtractAsync(
    string summary,
    string transcript,
    CancellationToken ct = default)
{
    var messages = new[]
    {
        new ChatMessage(ChatRole.System, BuildSystemPrompt()),
        new ChatMessage(ChatRole.User, $"Souhrn: {summary}\n\nTranskript: {transcript}")
    };

    var chatOptions = new ChatOptions { MaxOutputTokens = 8192 };

    try
    {
        var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
        var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

        try
        {
            var result = JsonSerializer.Deserialize<List<ExtractedTask>>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new List<ExtractedTask>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks");
            return new List<ExtractedTask>();
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Meeting task extraction failed — transcript will be imported without tasks");
        return new List<ExtractedTask>();
    }
}
```

- [ ] **Step 3.4: Update `MeetingTasksModule.cs` to wire the keyed raw client**

Replace:
```csharp
services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
```

With:
```csharp
services.AddScoped<IMeetingTaskExtractor>(sp =>
    new ClaudeMeetingTaskExtractor(
        sp.GetRequiredKeyedService<IChatClient>(
            Anela.Heblo.Adapters.Anthropic.AnthropicAdapterServiceCollectionExtensions.MeetingExtractionClientKey),
        sp.GetRequiredService<IMeetingUserDirectory>(),
        sp.GetRequiredService<ILogger<ClaudeMeetingTaskExtractor>>()));
```

Add the required using at the top of the file:
```csharp
using Microsoft.Extensions.Logging;
```

Note: `MeetingTasksModule` is in the Application layer; it references the Adapters assembly only for the DI key constant. This is acceptable because `MeetingTasksModule` is explicitly the composition root for this module.

- [ ] **Step 3.5: Run all ClaudeMeetingTaskExtractor tests to verify they pass**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests" \
  --no-build 2>&1 | tail -20
```

Expected: all 8 tests pass (5 existing + 3 new).

- [ ] **Step 3.6: Build to verify**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10
```

- [ ] **Step 3.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
git commit -m "feat(meeting-tasks): bypass enrichment middleware and use 8192-token budget for extraction"
```

---

## Task 4: Add `ReplacePendingTasksAsync` to domain + persistence

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`

- [ ] **Step 4.1: Add the interface method**

In `IMeetingTranscriptRepository.cs`, add after `SetAccessAsync`:

```csharp
/// <summary>
/// Removes all pending (non-approved/non-rejected) tasks from the transcript and
/// replaces them with the supplied list. Approved and rejected tasks are preserved.
/// </summary>
Task ReplacePendingTasksAsync(
    MeetingTranscript transcript,
    IReadOnlyList<ProposedTask> newTasks,
    CancellationToken ct = default);
```

- [ ] **Step 4.2: Implement in `MeetingTranscriptRepository.cs`**

Add the implementation after `SetAccessAsync`:

```csharp
public async Task ReplacePendingTasksAsync(
    MeetingTranscript transcript,
    IReadOnlyList<ProposedTask> newTasks,
    CancellationToken ct = default)
{
    var pending = transcript.Tasks.Where(t => t.Status == ProposedTaskStatus.Pending).ToList();
    _context.ProposedTasks.RemoveRange(pending);
    foreach (var t in pending)
        transcript.Tasks.Remove(t);

    await _context.ProposedTasks.AddRangeAsync(newTasks, ct);
    transcript.Tasks.AddRange(newTasks);
}
```

Add the required using at the top of the file if not already present:
```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
```

- [ ] **Step 4.3: Build to verify no compilation errors**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4.4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs
git commit -m "feat(meeting-tasks): add ReplacePendingTasksAsync to transcript repository"
```

---

## Task 5: Re-extract + re-resolve in `ReimportMeetingTranscriptHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`

- [ ] **Step 5.1: Write new failing tests and update existing constructor calls**

`ReimportMeetingTranscriptHandlerTests.cs` needs two changes:

**A) Update constructor to include extractor + directory mocks in ALL tests:**

Replace the constructor and field declarations with:

```csharp
public sealed class ReimportMeetingTranscriptHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IPlaudClient> _mockPlaudClient;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<IMeetingTaskExtractor> _mockExtractor;
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
    private readonly Mock<ILogger<ReimportMeetingTranscriptHandler>> _mockLogger;
    private readonly ReimportMeetingTranscriptHandler _handler;

    public ReimportMeetingTranscriptHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockPlaudClient = new Mock<IPlaudClient>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockExtractor = new Mock<IMeetingTaskExtractor>();
        _mockDirectory = new Mock<IMeetingUserDirectory>();
        _mockLogger = new Mock<ILogger<ReimportMeetingTranscriptHandler>>();

        _mockAccessGuard.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);

        _handler = new ReimportMeetingTranscriptHandler(
            _mockRepository.Object,
            _mockPlaudClient.Object,
            _mockAccessGuard.Object,
            _mockExtractor.Object,
            _mockDirectory.Object,
            _mockLogger.Object);
    }
```

Add these two usings if not already present:
```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
```

**B) In `Handle_WhenGenerated_RefreshesSummaryTranscriptAndSubject` and `Handle_WhenHeadlineIsEmpty_PreservesExistingSubject`, add extractor and repository mocks** for the new calls:

In `Handle_WhenGenerated_RefreshesSummaryTranscriptAndSubject`, add before the `// Act` comment:
```csharp
_mockExtractor
    .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new List<ExtractedTask>());

_mockRepository
    .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

In `Handle_WhenHeadlineIsEmpty_PreservesExistingSubject`, add before the `// Act` comment:
```csharp
_mockExtractor
    .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new List<ExtractedTask>());

_mockRepository
    .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

**C) Add new tests for re-extraction behavior:**

```csharp
[Fact]
public async Task Handle_WhenGenerated_ReExtractsPendingTasksAndPreservesApproved()
{
    // Arrange
    var id = Guid.NewGuid();
    var pendingTask = new ProposedTask
    {
        Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Old Pending",
        Status = ProposedTaskStatus.Pending
    };
    var approvedTask = new ProposedTask
    {
        Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Already Approved",
        Status = ProposedTaskStatus.Approved
    };
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_reextract",
        Subject = "Subject",
        Summary = "Old summary",
        RawTranscript = "Old transcript",
        Tasks = new List<ProposedTask> { pendingTask, approvedTask }
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_reextract", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_reextract", It.IsAny<CancellationToken>()))
        .ReturnsAsync("New transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_reextract", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("New Headline", "New summary"));

    var extractedTasks = new List<ExtractedTask>
    {
        new("New Task 1", "Description 1", "Ondra", null, "ondra@anela.cz")
    };
    _mockExtractor
        .Setup(e => e.ExtractAsync("New summary", "New transcript", It.IsAny<CancellationToken>()))
        .ReturnsAsync(extractedTasks);

    IReadOnlyList<ProposedTask>? capturedNewTasks = null;
    _mockRepository
        .Setup(r => r.ReplacePendingTasksAsync(entity, It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
        .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
        .Returns(Task.CompletedTask);
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    capturedNewTasks.Should().HaveCount(1);
    capturedNewTasks!.Single().Title.Should().Be("New Task 1");
    capturedNewTasks.Single().AssigneeEmail.Should().Be("ondra@anela.cz");
    capturedNewTasks.Single().Status.Should().Be(ProposedTaskStatus.Pending);
    capturedNewTasks.Single().IsManuallyAdded.Should().BeFalse();
}

[Fact]
public async Task Handle_WhenExtractionReturnsEmpty_ReplacePendingCalledWithEmptyList()
{
    // Arrange
    var id = Guid.NewGuid();
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_empty_extract",
        Subject = "Subject",
        Summary = "Old summary",
        RawTranscript = "Old transcript",
        Tasks = new List<ProposedTask>()
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
        .ReturnsAsync("Transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
    _mockExtractor
        .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ExtractedTask>());

    IReadOnlyList<ProposedTask>? capturedNewTasks = null;
    _mockRepository
        .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
        .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
        .Returns(Task.CompletedTask);
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    capturedNewTasks.Should().NotBeNull();
    capturedNewTasks.Should().BeEmpty();
}

[Fact]
public async Task Handle_WhenExtractedTaskHasNoEmail_ResolvesFromDirectory()
{
    // Arrange
    var id = Guid.NewGuid();
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_resolve",
        Subject = "Subject",
        Summary = "Old",
        RawTranscript = "Old",
        Tasks = new List<ProposedTask>()
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_resolve", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_resolve", It.IsAny<CancellationToken>()))
        .ReturnsAsync("Transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_resolve", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
    _mockExtractor
        .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ExtractedTask>
        {
            new("Task", "Desc", "Andy", null, AssigneeEmail: null)
        });
    _mockDirectory
        .Setup(d => d.Resolve("Andy"))
        .Returns(new MeetingUser("andrea@anela.cz", "Andrea Pajgrt Bartošová", new[] { "Andy" }));

    IReadOnlyList<ProposedTask>? capturedNewTasks = null;
    _mockRepository
        .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
        .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
        .Returns(Task.CompletedTask);
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    capturedNewTasks!.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
}
```

- [ ] **Step 5.2: Run tests to verify the new ones fail and the existing ones fail (constructor mismatch)**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests" \
  --no-build 2>&1 | tail -30
```

Expected: compilation error or test failures due to constructor mismatch and missing mocks.

- [ ] **Step 5.3: Implement the updated handler**

Replace `ReimportMeetingTranscriptHandler.cs` entirely with:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public sealed class ReimportMeetingTranscriptHandler
    : IRequestHandler<ReimportMeetingTranscriptRequest, ReimportMeetingTranscriptResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<ReimportMeetingTranscriptHandler> _logger;

    public ReimportMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingAccessGuard accessGuard,
        IMeetingTaskExtractor extractor,
        IMeetingUserDirectory userDirectory,
        ILogger<ReimportMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _accessGuard = accessGuard;
        _extractor = extractor;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<ReimportMeetingTranscriptResponse> Handle(
        ReimportMeetingTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {Id} for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        var detail = await _plaudClient.GetFileDetailAsync(transcript.PlaudRecordingId, cancellationToken);
        if (!detail.IsGenerated)
        {
            _logger.LogInformation("Recording {RecordingId} not yet generated on Plaud, cannot reimport", transcript.PlaudRecordingId);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.BusinessRuleViolation);
        }

        var rawTranscript = await _plaudClient.GetTranscriptAsync(transcript.PlaudRecordingId, cancellationToken);
        var summaryResult = await _plaudClient.GetSummaryAsync(transcript.PlaudRecordingId, cancellationToken);

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;
        if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;

        var extractedTasks = await _extractor.ExtractAsync(summaryResult.MarkdownContent, rawTranscript, cancellationToken);
        var newTasks = extractedTasks
            .Select(t => new ProposedTask
            {
                Id = Guid.NewGuid(),
                MeetingTranscriptId = transcript.Id,
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                AssigneeEmail = ResolveAssigneeEmail(t),
                DueDate = t.DueDate,
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false
            })
            .ToList();

        await _repository.ReplacePendingTasksAsync(transcript, newTasks, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reimported recording {RecordingId} for transcript {TranscriptId} with {TaskCount} tasks",
            transcript.PlaudRecordingId, transcript.Id, newTasks.Count);

        return new ReimportMeetingTranscriptResponse();
    }

    private string? ResolveAssigneeEmail(ExtractedTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.AssigneeEmail))
            return task.AssigneeEmail;

        return _userDirectory.Resolve(task.Assignee)?.Email;
    }
}
```

- [ ] **Step 5.4: Run all reimport handler tests to verify they pass**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests" \
  --no-build 2>&1 | tail -30
```

Expected: all 8 tests pass (5 existing + 3 new).

- [ ] **Step 5.5: Build to verify**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10
```

- [ ] **Step 5.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs
git commit -m "feat(meeting-tasks): reimport now re-extracts and re-resolves pending tasks"
```

---

## Task 6: Add "Andy" alias and compound assignee splitting

**Files:**
- Modify: `backend/src/Anela.Heblo.API/meeting-users.json`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingUserDirectoryTests.cs`

- [ ] **Step 6.1: Write failing tests for compound assignee splitting**

Add to `MeetingUserDirectoryTests.cs`, in the test data and as new tests:

Update `MeetingUserDirectoryTests` constructor so the temp file also includes a compound-assignee test user:

```csharp
// In the constructor, update the JSON:
File.WriteAllText(_tempFile, """
    [
      { "email": "andrea@anela.cz", "displayName": "Andrea Nováková", "aliases": ["Andy", "Andrea"] },
      { "email": "petr@anela.cz", "displayName": "Petr Svoboda", "aliases": [] },
      { "email": "bara@anela.cz", "displayName": "Bára Kocmánková", "aliases": ["Bára"] }
    ]
    """);
```

Then add these tests:

```csharp
[Fact]
public void Resolve_WithCompoundAmpersandAssignee_ReturnsFirstMatch()
{
    var directory = CreateDirectory(_tempFile);
    var user = directory.Resolve("Andy & Bára");
    user.Should().NotBeNull();
    user!.Email.Should().BeOneOf("andrea@anela.cz", "bara@anela.cz");
}

[Fact]
public void Resolve_WithCompoundCommaAssignee_ReturnsFirstMatch()
{
    var directory = CreateDirectory(_tempFile);
    var user = directory.Resolve("Petr Svoboda, Andrea Nováková");
    user.Should().NotBeNull();
    user!.Email.Should().Be("petr@anela.cz");
}

[Fact]
public void Resolve_WithSingleNameAfterSplit_ReturnsCorrectUser()
{
    var directory = CreateDirectory(_tempFile);
    // Verifies no regression: simple single-name still works after split logic added
    var user = directory.Resolve("Bára");
    user.Should().NotBeNull();
    user!.Email.Should().Be("bara@anela.cz");
}

[Fact]
public void Resolve_WithCompoundWhereNeitherMatches_ReturnsNull()
{
    var directory = CreateDirectory(_tempFile);
    var user = directory.Resolve("Nobody & Else");
    user.Should().BeNull();
}
```

- [ ] **Step 6.2: Run new tests to verify they fail**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingUserDirectoryTests" \
  --no-build 2>&1 | tail -20
```

Expected: compound-split tests fail; existing tests pass.

- [ ] **Step 6.3: Update `MeetingUserDirectory.cs` to split compound assignees**

Replace the `Resolve` method:

```csharp
public MeetingUser? Resolve(string nameOrAlias)
{
    if (string.IsNullOrWhiteSpace(nameOrAlias))
        return null;

    var direct = FindUser(nameOrAlias);
    if (direct is not null)
        return direct;

    var parts = nameOrAlias.Split(['&', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length <= 1)
        return null;

    return parts.Select(FindUser).FirstOrDefault(u => u is not null);
}

private MeetingUser? FindUser(string name) =>
    _users.FirstOrDefault(u =>
        string.Equals(u.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
        u.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
```

- [ ] **Step 6.4: Add "Andy" alias to Andrea Pajgrt Bartošová in `meeting-users.json`**

In `backend/src/Anela.Heblo.API/meeting-users.json`, find the entry for Andrea Pajgrt Bartošová and add "Andy" to her aliases:

```json
{
    "email": "andrea@anela.cz",
    "displayName": "Andrea Pajgrt Bartošová",
    "aliases": ["Andrea", "Pajgrt", "Bartošová", "Andy"]
}
```

- [ ] **Step 6.5: Run all MeetingUserDirectory tests to verify they pass**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingUserDirectoryTests" \
  --no-build 2>&1 | tail -20
```

Expected: all tests pass (5 existing + 4 new).

- [ ] **Step 6.6: Build and run the full MeetingTasks test suite**

```bash
dotnet build backend/backend.sln -c Debug 2>&1 | tail -10 && \
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingTasks|FullyQualifiedName~AnthropicChatClient" \
  --no-build 2>&1 | tail -30
```

Expected: Build succeeded, all MeetingTasks + Anthropic tests pass.

- [ ] **Step 6.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs \
        backend/src/Anela.Heblo.API/meeting-users.json \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingUserDirectoryTests.cs
git commit -m "feat(meeting-tasks): add Andy alias and split compound assignees on & and ,"
```

---

## Task 7: Final verification

- [ ] **Step 7.1: Run the full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/douala-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -20
```

Expected: All tests pass, no failures.

- [ ] **Step 7.2: Run `dotnet format` and check for issues**

```bash
dotnet format backend/backend.sln --verify-no-changes 2>&1 | tail -20
```

If there are formatting issues, run:
```bash
dotnet format backend/backend.sln
git add -u
git commit -m "chore: apply dotnet format"
```

- [ ] **Step 7.3: Verify the startup test (ApplicationStartupTests) still passes**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ApplicationStartupTests" \
  --no-build 2>&1 | tail -20
```

Expected: Application startup test passes (DI wiring is correct, including the keyed client).

---

## Self-Review Checklist

### Spec Coverage

| Spec Item | Covered By |
|-----------|-----------|
| Root cause: 1024-token truncation → Task extractor caps response | Task 1 (per-call override) + Task 3 (8192 budget) |
| `PostAnswerEnrichmentMiddleware` corrupts JSON | Task 2 (raw keyed client) + Task 3 (uses it) |
| Silent failure: LogWarning → LogError; distinguish JSON vs API | Task 3 |
| Reimport re-extracts pending tasks; preserves approved/rejected | Tasks 4 + 5 |
| "Andy" alias missing from meeting-users.json | Task 6 |
| Compound assignees ("Janka & Bára") not resolved | Task 6 |

### Key Invariants

- Global `KnowledgeBase:ChatMaxTokens = 1024` is NOT changed — KB calls stay cheap.
- `ClaudeMeetingSummaryExplainer` still uses the enriched `IChatClient` (it produces prose, not JSON) — unaffected.
- Approved/Rejected tasks survive reimport — only Pending tasks are replaced.
- DI key string `"meeting-extractor"` lives as a constant in `AnthropicAdapterServiceCollectionExtensions.MeetingExtractionClientKey` — no magic strings scattered around.
