### task: extraction-failure-exception

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class MeetingTaskExtractionFailedExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageAttemptCountAndLastRawResponse()
    {
        var ex = new MeetingTaskExtractionFailedException("boom", 3, "not-json{{{");

        ex.Message.Should().Be("boom");
        ex.AttemptCount.Should().Be(3);
        ex.LastRawResponse.Should().Be("not-json{{{");
    }

    [Fact]
    public void Constructor_AllowsNullLastRawResponse()
    {
        var ex = new MeetingTaskExtractionFailedException("boom", 3, null);

        ex.LastRawResponse.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"`
Expected: FAIL to compile — `MeetingTaskExtractionFailedException` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Thrown by <see cref="IMeetingTaskExtractor.ExtractAsync"/> when the LLM's response
/// could not be parsed into a valid, schema-conforming payload after exhausting all
/// retry attempts. Callers must not treat this as "zero tasks found" — it signals the
/// extraction itself failed and no tasks could be recovered.
/// </summary>
public sealed class MeetingTaskExtractionFailedException : Exception
{
    /// <summary>Total number of attempts made (initial call + retries) before giving up.</summary>
    public int AttemptCount { get; }

    /// <summary>The raw (fence-stripped) response text from the final failed attempt, for diagnostics.</summary>
    public string? LastRawResponse { get; }

    public MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse)
        : base(message)
    {
        AttemptCount = attemptCount;
        LastRawResponse = lastRawResponse;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs
git commit -m "feat(meeting-tasks): add MeetingTaskExtractionFailedException"
```
