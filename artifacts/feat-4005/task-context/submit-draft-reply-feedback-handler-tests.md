### task: submit-draft-reply-feedback-handler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs`

- [ ] **Step 1: Create the test file with class scaffold, mock fields, and `CreateHandler()` helper**

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SubmitDraftReplyFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SubmitDraftReplyFeedbackHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private SubmitDraftReplyFeedbackHandler CreateHandler() =>
        new(_repository.Object, _currentUserService.Object);
}
```

- [ ] **Step 2: Write FR-1 test — log not found returns `SmartsuppDraftReplyFeedbackLogNotFound`**

Add inside the class body:

```csharp
    [Fact]
    public async Task Handle_LogNotFound_ReturnsNotFound()
    {
        var logId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagInteractionLog?)null);

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound);
        result.Params.Should().ContainKey("logId").WhoseValue.Should().Be(logId.ToString());
        _currentUserService.Verify(s => s.GetCurrentUser(), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 3: Run the test to verify it compiles and fails only if the handler is broken (should pass since no code changed)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_LogNotFound_ReturnsNotFound"`
Expected: PASS (1 test run, 1 passed) — confirms the new test file compiles and the not-found path behaves as specified.

- [ ] **Step 4: Write FR-2 test — wrong `Feature` returns the same not-found error**

```csharp
    [Fact]
    public async Task Handle_WrongFeature_ReturnsNotFound()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            PrecisionScore = null,
            StyleScore = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_WrongFeature_ReturnsNotFound"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 6: Write FR-3 test — ownership mismatch returns `Forbidden`**

```csharp
    [Fact]
    public async Task Handle_OwnershipMismatch_ReturnsForbidden()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-b", "User B", "user-b@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Params.Should().ContainKey("logId").WhoseValue.Should().Be(logId.ToString());
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_OwnershipMismatch_ReturnsForbidden"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 8: Write FR-4 test — `PrecisionScore` already set returns `SmartsuppDraftReplyFeedbackAlreadySubmitted`**

```csharp
    [Fact]
    public async Task Handle_PrecisionScoreAlreadySet_ReturnsAlreadySubmitted()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = 3,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted);
        log.PrecisionScore.Should().Be(3);
        log.StyleScore.Should().BeNull();
        log.FeedbackComment.Should().BeNull();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_PrecisionScoreAlreadySet_ReturnsAlreadySubmitted"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 10: Write FR-5 test — `StyleScore` already set returns `SmartsuppDraftReplyFeedbackAlreadySubmitted`**

```csharp
    [Fact]
    public async Task Handle_StyleScoreAlreadySet_ReturnsAlreadySubmitted()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = 4,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 11: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_StyleScoreAlreadySet_ReturnsAlreadySubmitted"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 12: Write FR-6 test — success path writes scores/comment and saves**

```csharp
    [Fact]
    public async Task Handle_Success_WritesScoresAndSaves()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        log.PrecisionScore.Should().Be(5);
        log.StyleScore.Should().Be(4);
        log.FeedbackComment.Should().Be("Great answer");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 13: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_Success_WritesScoresAndSaves"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 14: Write FR-7 test — success path with `null` comment**

```csharp
    [Fact]
    public async Task Handle_Success_NullComment_WritesNull()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = null,
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        log.FeedbackComment.Should().BeNull();
    }
```

- [ ] **Step 15: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests.Handle_Success_NullComment_WritesNull"`
Expected: PASS (1 test run, 1 passed).

- [ ] **Step 16: Run the full new test class to confirm all 7 tests pass together**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests"`
Expected: PASS — `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

- [ ] **Step 17: Run `dotnet format` and the full backend test suite to confirm no regressions**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (if this reports changes, run `dotnet format backend/Anela.Heblo.sln` and re-verify)
Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds with 0 errors.
Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass (no failures), including the 7 new tests.

- [ ] **Step 18: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs
git commit -m "test: add unit test coverage for SubmitDraftReplyFeedbackHandler"
```
