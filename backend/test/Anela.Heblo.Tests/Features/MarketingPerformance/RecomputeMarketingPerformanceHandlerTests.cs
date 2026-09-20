using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class RecomputeMarketingPerformanceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRecomputeEnqueuer> _enqueuer = new();
    private readonly MarketingPerformanceRunGuard _guard = new();

    private RecomputeMarketingPerformanceHandler Handler(int maxRange = 60) => new(
        _enqueuer.Object, _guard,
        Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions { MaxRecomputeRangeMonths = maxRange }),
        new FakeTimeProvider(Now), NullLogger<RecomputeMarketingPerformanceHandler>.Instance);

    [Fact]
    public async Task Handle_ValidRange_EnqueuesAndReturnsJobId()
    {
        _enqueuer.Setup(e => e.Enqueue(new YearMonth(2023, 1), new YearMonth(2024, 12))).Returns("hf-42");

        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2023-01", To = "2024-12" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.JobId.Should().Be("hf-42");
        response.MonthCount.Should().Be(24);
    }

    [Theory]
    [InlineData("2023-01", "2022-12", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("nope", "2024-12", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2026-01", "2026-10", ErrorCodes.MarketingPerformanceInvalidMonthRange)] // future month
    [InlineData("2020-01", "2026-09", ErrorCodes.MarketingPerformanceRangeTooLarge)]
    public async Task Handle_InvalidRange_ReturnsErrorWithoutEnqueueing(string from, string to, ErrorCodes expected)
    {
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = from, To = to }, CancellationToken.None);
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(expected);
        _enqueuer.Verify(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RangeTooLarge_ReportsMaxInParams()
    {
        var response = await Handler(maxRange: 12).Handle(new RecomputeMarketingPerformanceRequest { From = "2025-01", To = "2026-09" }, CancellationToken.None);
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceRangeTooLarge);
        response.Params.Should().ContainKey("maxMonths").WhoseValue.Should().Be("12");
    }

    [Fact]
    public async Task Handle_WhileRunning_ReturnsAlreadyRunning()
    {
        _guard.TryBegin();
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" }, CancellationToken.None);
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceRecomputeAlreadyRunning);
        _guard.End();
    }

    [Fact]
    public async Task Handle_EnqueueReturnsNull_ReturnsEnqueueFailed_AndReleasesTheReservation()
    {
        // Arrange
        _enqueuer.Setup(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>())).Returns((string?)null);

        // Act
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" }, CancellationToken.None);

        // Assert - nothing was enqueued, so nothing will release the guard for us.
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceEnqueueFailed);
        _guard.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TwoConcurrentRequests_AcceptsOneAndRejectsTheOther()
    {
        // Arrange - both requests are handled before either job ever starts, which is
        // exactly the window the old IsRunning check could not close.
        _enqueuer.Setup(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>())).Returns("hf-1");
        var handler = Handler();
        var request = new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" };

        // Act
        var first = await handler.Handle(request, CancellationToken.None);
        var second = await handler.Handle(request, CancellationToken.None);

        // Assert
        first.Success.Should().BeTrue();
        first.JobId.Should().NotBeNull();
        second.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceRecomputeAlreadyRunning);
        second.JobId.Should().BeNull();
        _enqueuer.Verify(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>()), Times.Once);

        _guard.End();
    }

    [Fact]
    public async Task Handle_EnqueueThrows_ReleasesTheReservation()
    {
        // Arrange
        _enqueuer.Setup(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>())).Throws(new InvalidOperationException("hangfire down"));

        // Act
        var act = () => Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _guard.IsRunning.Should().BeFalse();
    }
}
