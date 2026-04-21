using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class AdSyncLogTests
{
    [Fact]
    public void Complete_SetsStatusToSuccess()
    {
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Complete(campaigns: 5, adSets: 10, ads: 20, metricRows: 100);

        log.Status.Should().Be(AdSyncStatus.Success);
    }

    [Fact]
    public void Complete_SetsCounters()
    {
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Complete(campaigns: 3, adSets: 7, ads: 15, metricRows: 60);

        log.CampaignsSynced.Should().Be(3);
        log.AdSetsSynced.Should().Be(7);
        log.AdsSynced.Should().Be(15);
        log.MetricRowsSynced.Should().Be(60);
    }

    [Fact]
    public void Complete_SetsCompletedAt()
    {
        var before = DateTime.UtcNow;
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Complete(campaigns: 1, adSets: 1, ads: 1, metricRows: 1);

        log.CompletedAt.Should().NotBeNull();
        log.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Fail_SetsStatusToFailed()
    {
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Fail("Something went wrong");

        log.Status.Should().Be(AdSyncStatus.Failed);
    }

    [Fact]
    public void Fail_SetsErrorMessage()
    {
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Fail("Connection timeout");

        log.ErrorMessage.Should().Be("Connection timeout");
    }

    [Fact]
    public void Fail_SetsCompletedAt()
    {
        var before = DateTime.UtcNow;
        var log = new AdSyncLog { Status = AdSyncStatus.Running };

        log.Fail("error");

        log.CompletedAt.Should().NotBeNull();
        log.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }
}
