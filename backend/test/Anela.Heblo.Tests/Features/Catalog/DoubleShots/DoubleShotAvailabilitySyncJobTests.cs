using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DoubleShots;

public class DoubleShotAvailabilitySyncJobTests
{
    private static EshopStock Row(string code, decimal stock) => new() { Code = code, Stock = stock };

    private static (DoubleShotAvailabilitySyncJob Sut, Mock<IEshopStockClient> Stock) MakeSut(
        List<EshopStock>? rows = null,
        bool jobEnabled = true,
        bool applyChanges = true,
        Exception? listError = null)
    {
        var stock = new Mock<IEshopStockClient>();
        if (listError is null)
        {
            stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows ?? []);
            foreach (var row in rows ?? [])
                SetupLiveStock(stock, row.Code, (double)row.Stock);
        }
        else
            stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(listError);

        var resilience = new Mock<ICatalogResilienceService>();
        resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<List<EshopStock>>> op, string _, CancellationToken ct) => op(ct));

        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var flags = new Mock<IFeatureFlagChecker>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DoubleShotAvailabilitySync, It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyChanges);

        var sut = new DoubleShotAvailabilitySyncJob(
            stock.Object, resilience.Object, statusChecker.Object, flags.Object,
            NullLogger<DoubleShotAvailabilitySyncJob>.Instance);
        return (sut, stock);
    }

    private static void SetupLiveStock(Mock<IEshopStockClient> stock, string code, double amount) =>
        stock.Setup(s => s.GetSupplyAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopStockSupply { Code = code, Amount = amount });

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var (sut, _) = MakeSut();

        sut.Metadata.JobName.Should().Be("doubleshot-availability-sync");
        sut.Metadata.Category.Should().Be(RecurringJobCategory.Catalog);
        sut.Metadata.CronExpression.Should().Be("*/15 * * * *");
        sut.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenJobDisabled()
    {
        var (sut, stock) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        stock.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WritesNothing_WhenFlagOff_DryRun()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)], applyChanges: false);

        await sut.ExecuteAsync();

        stock.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WritesDeltaForEachDifferingVariant_WhenFlagOn()
    {
        var (sut, stock) = MakeSut([
            Row("SA015030V", 0), Row("SA015030S", 213),
            Row("SA016005V", 400), Row("SA016005S", 385),
            Row("SA017100V", 24), Row("SA017100S", 24),
        ]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync("SA015030V", 213d, It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync("SA016005V", -15d, It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync("SA017100V", It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NeverWritesSetCodes_OrRealStock()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213), Row("BAL0001V", 1000)]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync(It.Is<string>(c => !c.StartsWith("SA") || !c.EndsWith('V')), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesAfterOneWriteFails()
    {
        var (sut, stock) = MakeSut([
            Row("SA015030V", 0), Row("SA015030S", 213),
            Row("SA016005V", 0), Row("SA016005S", 385),
        ]);
        stock.Setup(s => s.UpdateStockAsync("SA015030V", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("stock-change-not-allowed"));

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Verify(s => s.UpdateStockAsync("SA016005V", 385d, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CountsHttpTimeoutAsFailure_NotCancellation()
    {
        var (sut, stock) = MakeSut([
            Row("SA015030V", 0), Row("SA015030S", 213),
            Row("SA016005V", 0), Row("SA016005S", 385),
        ]);
        stock.Setup(s => s.UpdateStockAsync("SA015030V", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HttpClient timeout"));

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Verify(s => s.UpdateStockAsync("SA016005V", 385d, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WritesNothing_AndFailsTheRun_WhenStockListFails()
    {
        var (sut, stock) = MakeSut(listError: new HttpRequestException("timeout"));

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_DuringWrites()
    {
        using var cts = new CancellationTokenSource();
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        stock.Setup(s => s.UpdateStockAsync("SA015030V", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => sut.ExecuteAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUnpairedVariant()
    {
        var (sut, stock) = MakeSut([Row("SA099030V", 7)]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_IsIdempotent_WhenCsvIsStaleButLiveStockAlreadyMatches()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        SetupLiveStock(stock, "SA015030V", 213);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WritesTargetMinusLiveStock_NotMinusCsvValue()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        SetupLiveStock(stock, "SA015030V", 100);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync("SA015030V", 113d, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HealsDrift_WhenCsvShowsInSyncButLiveDiffers()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 213), Row("SA015030S", 213)]);
        SetupLiveStock(stock, "SA015030V", 426);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync("SA015030V", -213d, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsVariant_WhenLiveSupplyIsMissing()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        stock.Setup(s => s.GetSupplyAsync("SA015030V", It.IsAny<CancellationToken>())).ReturnsAsync((EshopStockSupply?)null);

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsVariant_WhenLiveAmountIsUnknown()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        stock.Setup(s => s.GetSupplyAsync("SA015030V", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopStockSupply { Code = "SA015030V", Amount = 0, IsAmountKnown = false });

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsVariant_WhenLiveSupplyIsForAnotherCode()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        stock.Setup(s => s.GetSupplyAsync("SA015030V", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopStockSupply { Code = "SA015030V2", Amount = 0 });

        var act = () => sut.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotReadLiveStock()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)], applyChanges: false);

        await sut.ExecuteAsync();

        stock.Verify(s => s.GetSupplyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ExecuteAsync_NeverRetriesAutomatically()
    {
        // CustomAttributeData, not GetCustomAttributes: AutomaticRetryAttribute's constructor reads Hangfire's global
        // LogProvider, which another test's WebApplicationFactory may have left holding a disposed ILoggerFactory.
        var attribute = typeof(DoubleShotAvailabilitySyncJob).GetMethod(nameof(DoubleShotAvailabilitySyncJob.ExecuteAsync))!
            .GetCustomAttributesData().Single(d => d.AttributeType == typeof(AutomaticRetryAttribute));

        attribute.NamedArguments.Single(a => a.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.Should().Be(0);
    }

    [Fact]
    public void ExecuteAsync_DisallowsConcurrentRuns()
    {
        typeof(DoubleShotAvailabilitySyncJob).GetMethod(nameof(DoubleShotAvailabilitySyncJob.ExecuteAsync))!
            .GetCustomAttributesData().Should().Contain(d => d.AttributeType == typeof(DisableConcurrentExecutionAttribute));
    }
}
