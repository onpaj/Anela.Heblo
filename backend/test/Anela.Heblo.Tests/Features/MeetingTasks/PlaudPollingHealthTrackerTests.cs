using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class PlaudPollingHealthTrackerTests
{
    [Fact]
    public void RecordUpstreamFailure_IncrementsConsecutiveCount()
    {
        var tracker = new PlaudPollingHealthTracker();

        tracker.RecordUpstreamFailure().Should().Be(1);
        tracker.RecordUpstreamFailure().Should().Be(2);
        tracker.RecordUpstreamFailure().Should().Be(3);
        tracker.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void Reset_ReturnsCountToZero()
    {
        var tracker = new PlaudPollingHealthTracker();
        tracker.RecordUpstreamFailure();
        tracker.RecordUpstreamFailure();

        tracker.Reset();

        tracker.ConsecutiveFailures.Should().Be(0);
        tracker.RecordUpstreamFailure().Should().Be(1);
    }

    [Fact]
    public void RecordUpstreamFailure_IsThreadSafe()
    {
        var tracker = new PlaudPollingHealthTracker();

        Parallel.For(0, 1000, _ => tracker.RecordUpstreamFailure());

        tracker.ConsecutiveFailures.Should().Be(1000);
    }
}
