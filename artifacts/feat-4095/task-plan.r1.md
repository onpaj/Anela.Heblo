# DraftReplyLoggingBehavior Unit Tests Implementation Plan

**Goal:** Add a focused unit test suite for `DraftReplyLoggingBehavior` (currently 0% coverage against a 60% gate) covering its three execution paths — skip-on-failure, happy path, exception-swallow — mirroring the existing sibling test `QuestionLoggingBehaviorTests`.
**Architecture:** No production code changes. One new test file, `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`, invoking `DraftReplyLoggingBehavior.Handle(...)` directly (no MediatR pipeline, no DI container, no database) with Moq mocks for `IRagInteractionLogRepository` and `ICurrentUserService`, and a concrete (non-mocked) `RagInteractionRecorder` populated through its real public API, exactly as the sibling suite does for `QuestionLoggingBehavior`.
**Tech Stack:** .NET 8, xUnit (`[Fact]`), Moq, plain xUnit `Assert.*` (not FluentAssertions — matches the sibling file).

---

### task: add-draft-reply-logging-behavior-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`

**Context you need (this task is fully self-contained — no other task's output is required):**

The system under test, `backend/src/Anela.Heblo.Application/Features/Smartsupp/Pipeline/DraftReplyLoggingBehavior.cs`, is (verbatim, already in the repo, **do not modify it**):

```csharp
using System.Diagnostics;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Pipeline;

public class DraftReplyLoggingBehavior : IPipelineBehavior<GenerateDraftReplyRequest, GenerateDraftReplyResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly IRagInteractionRecorder _recorder;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DraftReplyLoggingBehavior> _logger;

    public DraftReplyLoggingBehavior(
        IRagInteractionLogRepository repository,
        IRagInteractionRecorder recorder,
        ICurrentUserService currentUserService,
        ILogger<DraftReplyLoggingBehavior> logger)
    {
        _repository = repository;
        _recorder = recorder;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateDraftReplyResponse> Handle(
        GenerateDraftReplyRequest request,
        RequestHandlerDelegate<GenerateDraftReplyResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        // Skip failed generations (conversation not found / empty / AI unavailable).
        if (!response.Success || !_recorder.HasInteraction)
            return response;

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var log = RagInteractionLogFactory.Build(
                _recorder,
                currentUser.Id,
                sw.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);

            await _repository.SaveAsync(log, cancellationToken);
            response.Id = log.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write RAG interaction log for Smartsupp draft reply. Conversation: {ConversationId}", request.ConversationId);
        }

        return response;
    }
}
```

Collaborator types referenced by the test (all pre-existing, unmodified):

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyRequest.cs`:
```csharp
public class GenerateDraftReplyRequest : IRequest<GenerateDraftReplyResponse>
{
    public string ConversationId { get; set; } = null!;
    public string? Topic { get; set; }
}
```

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs`:
```csharp
public class GenerateDraftReplyResponse : BaseResponse
{
    public Guid? Id { get; set; }
    public string Answer { get; set; } = string.Empty;
    public List<DraftReplySource> Sources { get; set; } = new();
    public GenerateDraftReplyResponse() { }
    public GenerateDraftReplyResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```
`BaseResponse` (in `Anela.Heblo.Application.Shared`) carries a `Success` bool (true by default when constructed with the parameterless constructor, false when constructed with an `ErrorCodes` argument) — the sibling test sets `Success = false` via object initializer on the parameterless-constructed response, and this test does the same.

`backend/src/Anela.Heblo.Application/Shared/Rag/RagInteractionRecorder.cs` — concrete class to use directly (not mocked):
```csharp
public interface IRagInteractionRecorder
{
    RagFeature? Feature { get; }
    string? Question { get; }
    string? ExpandedQuery { get; }
    int TopK { get; }
    IReadOnlyList<RagRetrievedChunk> Chunks { get; }
    string? SystemPrompt { get; }
    string? Answer { get; }
    string? ConversationId { get; }
    string? Topic { get; }
    bool HasInteraction { get; }
    void RecordRetrieval(string expandedQuery, int topK, IReadOnlyList<RagRetrievedChunk> chunks);
    void RecordInteraction(
        RagFeature feature,
        string question,
        string systemPrompt,
        string answer,
        string? conversationId = null,
        string? topic = null);
}

public class RagInteractionRecorder : IRagInteractionRecorder
{
    // HasInteraction becomes true only after RecordInteraction(...) is called.
    // ... (full implementation already in repo, unmodified)
}
```

`backend/src/Anela.Heblo.Application/Shared/Rag/RagInteractionLogFactory.cs`:
```csharp
public static class RagInteractionLogFactory
{
    public static RagInteractionLog Build(
        IRagInteractionRecorder recorder,
        string? userId,
        long durationMs,
        DateTimeOffset createdAt)
    {
        return new RagInteractionLog
        {
            Id = Guid.NewGuid(),
            Feature = recorder.Feature ?? RagFeature.KnowledgeBase,
            CreatedAt = createdAt,
            UserId = userId,
            Question = recorder.Question ?? string.Empty,
            ExpandedQuery = recorder.ExpandedQuery,
            TopK = recorder.TopK,
            SourceCount = recorder.Chunks.Count,
            RetrievedChunksJson = JsonSerializer.Serialize(recorder.Chunks),
            SystemPrompt = recorder.SystemPrompt ?? string.Empty,
            Answer = recorder.Answer ?? string.Empty,
            ConversationId = recorder.ConversationId,
            Topic = recorder.Topic,
            DurationMs = durationMs,
        };
    }
}
```

`backend/src/Anela.Heblo.Domain/Features/Rag/RagFeature.cs`:
```csharp
public enum RagFeature
{
    KnowledgeBase = 0,
    SmartsuppDraftReply = 1,
}
```

`backend/src/Anela.Heblo.Domain/Features/Rag/IRagInteractionLogRepository.cs` — relevant member: `Task SaveAsync(RagInteractionLog log, CancellationToken ct = default);` (to be mocked with Moq).

`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`:
```csharp
public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);
}
```

`backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs`:
```csharp
public record CurrentUser(
    string? Id,
    string? Name,
    string? Email,
    bool IsAuthenticated
);
```

The structural template this new file must mirror, `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs`, already exists in the repo verbatim as follows (read for reference — do not modify it):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class QuestionLoggingBehaviorTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<QuestionLoggingBehavior>> _logger = new();

    // Prefer a real recorder over a mock: it matches the production flow, where the retrieval
    // and feature handlers populate the scoped recorder that this behavior reads back.
    private readonly RagInteractionRecorder _recorder = new();

    private QuestionLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _recorder, _userService.Object, _logger.Object);

    private void RecordInteraction(string question = "Test?", int topK = 5, string answer = "Test answer.") =>
        RecordInteraction(question, topK, answer, []);

    private void RecordInteraction(
        string question,
        int topK,
        string answer,
        IReadOnlyList<RagRetrievedChunk> chunks)
    {
        _recorder.RecordRetrieval(question, topK, chunks);
        _recorder.RecordInteraction(RagFeature.KnowledgeBase, question, "system prompt", answer);
    }

    // ... six [Fact] tests: Handle_WritesLogRow_AndReturnsResponse, Handle_WhenDbWriteFails_StillReturnsResponse,
    // Handle_WhenLogSaved_SetsResponseIdToLogId, Handle_WhenDbWriteFails_ResponseIdRemainsNull,
    // Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId, Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId
}
```

The new test project namespace convention is `Anela.Heblo.Tests.<FolderPath>` (folder `Smartsupp/Pipeline` → namespace `Anela.Heblo.Tests.Smartsupp.Pipeline`), matching how `KnowledgeBase/Pipeline` maps to `Anela.Heblo.Tests.KnowledgeBase.Pipeline`. No `.csproj` changes are needed — `Anela.Heblo.Tests.csproj` already references `Moq` and `xUnit` (confirmed via the sibling file's usings) and xUnit auto-discovers `[Fact]`s in the compiled assembly regardless of folder.

- [ ] **Step 1: Create the directory and confirm it does not already exist**

Run:
```bash
ls backend/test/Anela.Heblo.Tests/Smartsupp 2>&1 || true
mkdir -p backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline
```
Expected: the `ls` reports "No such file or directory" (confirming this is a new top-level folder, distinct from the pre-existing `backend/test/Anela.Heblo.Tests/Features/Smartsupp/`), then `mkdir -p` succeeds silently.

- [ ] **Step 2: Write the full test file**

Create `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` with the following complete content:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Pipeline;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Smartsupp.Pipeline;

public class DraftReplyLoggingBehaviorTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<DraftReplyLoggingBehavior>> _logger = new();

    // Prefer a real recorder over a mock: it matches the production flow, where the retrieval
    // and feature handlers populate the scoped recorder that this behavior reads back.
    private readonly RagInteractionRecorder _recorder = new();

    private DraftReplyLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _recorder, _userService.Object, _logger.Object);

    private void RecordInteraction(
        string question = "How do I return an order?",
        int topK = 5,
        string answer = "You can return it within 14 days.",
        string conversationId = "conv-1") =>
        RecordInteraction(question, topK, answer, conversationId, []);

    private void RecordInteraction(
        string question,
        int topK,
        string answer,
        string conversationId,
        IReadOnlyList<RagRetrievedChunk> chunks)
    {
        _recorder.RecordRetrieval(question, topK, chunks);
        _recorder.RecordInteraction(RagFeature.SmartsuppDraftReply, question, "system prompt", answer, conversationId);
    }

    [Fact]
    public async Task Handle_WritesLogRow_AndReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction(question: "How do I return an order?", topK: 5, answer: "You can return it within 14 days.");

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse
        {
            Answer = "You can return it within 14 days.",
            Sources = []
        };

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedLog);
        Assert.Equal(RagFeature.SmartsuppDraftReply, capturedLog.Feature);
        Assert.Equal("How do I return an order?", capturedLog.Question);
        Assert.Equal("You can return it within 14 days.", capturedLog.Answer);
        Assert.Equal(5, capturedLog.TopK);
        Assert.Equal(0, capturedLog.SourceCount);
        Assert.Equal("user-1", capturedLog.UserId);
        Assert.True(capturedLog.DurationMs >= 0);
        _repository.Verify(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLogSaved_SetsResponseIdToLogId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.NotNull(capturedLog);
        Assert.Equal(capturedLog.Id, result.Id);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_ResponseIdRemainsNull()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Null(result.Id);
    }

    [Fact]
    public async Task Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        // Deliberately do not record an interaction: recorder.HasInteraction stays false.

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
        Assert.Null(result.Id);
        _repository.Verify(
            r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var failedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [], Success = false };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(failedResponse), default);

        Assert.Null(result.Id);
        _repository.Verify(
            r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

Notes on why this file is written exactly this way (do not deviate):
- `ConversationId = "conv-1"` is set on every request (never left `null`), since `DraftReplyLoggingBehavior`'s catch block interpolates `request.ConversationId` into a log message — a `null` there would not itself throw (string interpolation of `null` yields an empty string, not an NRE) but the spec/arch-review explicitly call for a concrete value in every test to remove ambiguity about what "no exception propagates" is proving. Keep it concrete everywhere for consistency, matching the arch-review's stated risk mitigation.
- `RagFeature.SmartsuppDraftReply` (not `RagFeature.KnowledgeBase`) is passed to `RecordInteraction`, since this is the Smartsupp sibling of `QuestionLoggingBehaviorTests` — this is the one substitution beyond renaming types.
- Only `UserId`, `Feature`, `Question`, `Answer`, `TopK`, `SourceCount`, `DurationMs` are asserted on the captured log — matching the sibling's assertion set. Do not assert `SystemPrompt`, `RetrievedChunksJson`, `ExpandedQuery`, `ConversationId`, `Topic`, or `CreatedAt` on the captured log; these are `RagInteractionLogFactory` internals not part of this behavior's contract (per spec FR-6 / arch-review risk table).
- The logger mock (`_logger`) is constructed only to satisfy the constructor and is never asserted against, matching the sibling file exactly.
- No `[Fact]` calls `Assert.ThrowsAsync` — both exception-path tests `await` the call directly, proving the exception is swallowed rather than merely expected.

- [ ] **Step 2 (verification): Build the test project and confirm the new file compiles**

Run:
```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded.` with 0 errors. If it fails on a missing `using` or type mismatch, re-check the exact type signatures quoted above in this task's context section (do not guess — they are copied verbatim from the current source).

- [ ] **Step 3: Run only the new test class and confirm all 6 tests pass**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Smartsupp.Pipeline.DraftReplyLoggingBehaviorTests"
```
Expected output: `Passed! - Failed: 0, Passed: 6, Skipped: 0` (or equivalent "Passed" summary showing exactly 6 passed, 0 failed). If any test fails:
- `Handle_WritesLogRow_AndReturnsResponse` or `Handle_WhenLogSaved_SetsResponseIdToLogId` failing on a captured-log assertion means the `RecordInteraction` helper's parameters don't match what's asserted — double check the default `question`/`answer`/`topK` values used in the no-args `RecordInteraction()` call sites (`"How do I return an order?"` / `5` / `"You can return it within 14 days."`) against what each test's asserts expect.
- `Handle_WhenDbWriteFails_*` failing with an unhandled exception means the `ThrowsAsync` setup on `_repository` didn't match — confirm the `Setup(r => r.SaveAsync(...))` argument matchers use `It.IsAny<RagInteractionLog>()` and `It.IsAny<CancellationToken>()` exactly as written above.
- A compile error referencing `GenerateDraftReplyResponse.Success` not existing means `BaseResponse` doesn't expose a settable `Success` — in that case inspect `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs` directly and adjust only the `Success = false` object-initializer line in `Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId` to match its actual member (this is the only line in this file where the exact `BaseResponse` shape matters).

- [ ] **Step 4: Run the full test project to confirm no regressions elsewhere**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `dotnet build` succeeds; `dotnet format --verify-no-changes` reports no formatting violations (if it reports violations in the new file, run `dotnet format` without `--verify-no-changes` to auto-fix, then re-verify); the full test run passes with 0 failures (the previously-passing suite plus the 6 new tests).

- [ ] **Step 5: Commit**

Run:
```bash
cd /home/user/worktrees/feature-4095-Coverage-Gap-Smartsupp-Draftreplyloggingbehavior-S
git add backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs
git commit -m "$(cat <<'EOF'
Add unit tests for DraftReplyLoggingBehavior

Covers the skip-on-failure, skip-on-no-interaction, happy-path
(log persisted + response.Id stamped), and exception-swallow paths,
mirroring the existing QuestionLoggingBehaviorTests sibling suite.
Closes the 0%-vs-60% coverage gap on this file; no production code
changed.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PxjK24crMcC9emWXe7ff82
EOF
)"
```
Expected: commit succeeds; `git status` shows a clean working tree (no other files modified, per the spec's test-only scope).
