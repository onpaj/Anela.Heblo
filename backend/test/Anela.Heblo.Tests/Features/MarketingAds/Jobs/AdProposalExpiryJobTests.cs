using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Jobs;

public class AdProposalExpiryJobTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _status = new();

    private AdProposalExpiryJob Job(AdProposalOptions? options = null) => new(_mediator.Object, _status.Object,
        Microsoft.Extensions.Options.Options.Create(options ?? new AdProposalOptions()), NullLogger<AdProposalExpiryJob>.Instance);

    [Fact]
    public void Metadata_IsAMarketingJobEvery15MinutesInPrague()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("ads-proposal-expiry");
        metadata.CronExpression.Should().Be("*/15 * * * *");
        metadata.TimeZoneId.Should().Be("Europe/Prague");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BadOptions_DoNotThrowInTheConstructor_AndFallBackToTheDefaultCron()
    {
        var job = () => Job(new AdProposalOptions { ExpiryCronExpression = "every day", ProposalTtlHours = -1 });

        job.Should().NotThrow();
        job().Metadata.CronExpression.Should().Be("*/15 * * * *");
    }

    [Fact]
    public void ConfiguredValidCron_IsUsed()
    {
        Job(new AdProposalOptions { ExpiryCronExpression = "0 * * * *" }).Metadata.CronExpression.Should().Be("0 * * * *");
    }

    [Fact]
    public async Task Enabled_SendsTheExpireCommand()
    {
        _status.Setup(s => s.IsJobEnabledAsync("ads-proposal-expiry", It.IsAny<CancellationToken>(), true)).ReturnsAsync(true);
        _mediator.Setup(m => m.Send(It.IsAny<ExpireAdProposalsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ExpireAdProposalsResponse());

        await Job().ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<ExpireAdProposalsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disabled_DoesNothing()
    {
        _status.Setup(s => s.IsJobEnabledAsync("ads-proposal-expiry", It.IsAny<CancellationToken>(), true)).ReturnsAsync(false);

        await Job().ExecuteAsync();

        _mediator.VerifyNoOtherCalls();
    }
}
