using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

public class HangfireAdProposalExecutionEnqueuerTests
{
    private readonly Mock<IBackgroundJobClient> _client = new();
    private readonly Mock<ILogger<HangfireAdProposalExecutionEnqueuer>> _logger = new();

    [Fact]
    public void EnqueueExecution_ReturnsTheHangfireJobId()
    {
        _client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-42");

        var jobId = new HangfireAdProposalExecutionEnqueuer(_client.Object, _logger.Object).EnqueueExecution(7);

        jobId.Should().Be("job-42");
        _client.Verify(c => c.Create(
            It.Is<Job>(j => j.Type == typeof(AdProposalExecutionJob) && j.Method.Name == nameof(AdProposalExecutionJob.RunAsync) && (int)j.Args[0] == 7),
            It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public void EnqueueExecution_WhenHangfireThrows_ReturnsNullAndLogsAnError()
    {
        // The expiry job re-enqueues stuck Approved proposals, so a failed enqueue must surface as null, not throw.
        _client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Throws(new InvalidOperationException("storage down"));

        var jobId = new HangfireAdProposalExecutionEnqueuer(_client.Object, _logger.Object).EnqueueExecution(7);

        jobId.Should().BeNull();
        _logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("7")),
            It.Is<InvalidOperationException>(ex => ex.Message == "storage down"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
