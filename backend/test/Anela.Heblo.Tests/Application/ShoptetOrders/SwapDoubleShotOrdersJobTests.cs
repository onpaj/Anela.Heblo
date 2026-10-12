using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class SwapDoubleShotOrdersJobTests
{
    private readonly Mock<IDoubleShotOrderSwapService> _swap = new();
    private readonly Mock<IRecurringJobStatusChecker> _status = new();
    private readonly Mock<IFeatureFlagChecker> _flags = new();

    private SwapDoubleShotOrdersJob CreateSut(bool jobEnabled, bool flagOn)
    {
        _status.Setup(s => s.IsJobEnabledAsync("swap-double-shot-orders", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(jobEnabled);
        _flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, It.IsAny<CancellationToken>()))
            .ReturnsAsync(flagOn);
        _swap.Setup(s => s.SweepAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DoubleShotSwapSummary(0, 0, 0, 0, 0));
        return new SwapDoubleShotOrdersJob(_swap.Object, _status.Object, _flags.Object, NullLogger<SwapDoubleShotOrdersJob>.Instance);
    }

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var job = CreateSut(true, false);

        job.Metadata.JobName.Should().Be("swap-double-shot-orders");
        job.Metadata.CronExpression.Should().Be("*/5 * * * *");
        job.Metadata.Category.Should().Be(RecurringJobCategory.Warehouse);
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_DoesNothing_WhenJobDisabled()
    {
        await CreateSut(jobEnabled: false, flagOn: true).ExecuteAsync();

        _swap.Verify(s => s.SweepAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_PassesFlagAsApplyChanges(bool flagOn)
    {
        await CreateSut(jobEnabled: true, flagOn: flagOn).ExecuteAsync();

        _swap.Verify(s => s.SweepAsync(flagOn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickingPreparer_SweepsWithFlagAsApplyChanges(bool flagOn)
    {
        CreateSut(jobEnabled: true, flagOn: flagOn);
        var adapter = new Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure
            .ShoptetOrdersDoubleShotPickingPreparerAdapter(_swap.Object, _flags.Object);

        await adapter.PrepareAsync();

        _swap.Verify(s => s.SweepAsync(flagOn, It.IsAny<CancellationToken>()), Times.Once);
    }
}
