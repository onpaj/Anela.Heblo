using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class AdProposalOptionsTests
{
    [Fact]
    public void Defaults_AreValid_AndRequeueWaitsLongerThanTheObservedHangfireQueueLatency()
    {
        var options = new AdProposalOptions();

        options.ProposalTtl.Should().Be(TimeSpan.FromHours(72));
        options.ExpiryCronExpression.Should().Be("*/15 * * * *");
        // D19: WorkerCount=1 with 12-15 min queue latency observed; 15 would re-enqueue still-queued jobs.
        options.StuckApprovedRequeueMinutes.Should().Be(60);
        options.Invoking(o => o.EnsureValid()).Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 60, "*/15 * * * *", "Ads:ProposalTtlHours")]
    [InlineData(-1, 60, "*/15 * * * *", "Ads:ProposalTtlHours")]
    [InlineData(72, 0, "*/15 * * * *", "Ads:StuckApprovedRequeueMinutes")]
    [InlineData(72, 60, " ", "Ads:ExpiryCronExpression")]
    public void EnsureValid_NamesTheBadSetting(int ttlHours, int requeueMinutes, string cron, string expectedKey)
    {
        var options = new AdProposalOptions
        {
            ProposalTtlHours = ttlHours,
            StuckApprovedRequeueMinutes = requeueMinutes,
            ExpiryCronExpression = cron,
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>().WithMessage($"*{expectedKey}*");
    }
}
