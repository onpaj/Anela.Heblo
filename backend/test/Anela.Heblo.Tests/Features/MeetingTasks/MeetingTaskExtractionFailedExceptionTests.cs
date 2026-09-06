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
