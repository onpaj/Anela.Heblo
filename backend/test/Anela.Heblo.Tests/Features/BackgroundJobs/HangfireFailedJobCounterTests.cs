using Anela.Heblo.API.Infrastructure.Hangfire;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public sealed class HangfireFailedJobCounterTests
{
    private readonly Mock<JobStorage> _storageMock = new();
    private readonly Mock<IMonitoringApi> _monitoringApiMock = new();
    private readonly HangfireFailedJobCounter _counter;

    public HangfireFailedJobCounterTests()
    {
        _storageMock.Setup(s => s.GetMonitoringApi()).Returns(_monitoringApiMock.Object);
        _counter = new HangfireFailedJobCounter(_storageMock.Object);
    }

    [Fact]
    public async Task GetFailedCountAsync_ReturnsValueFromMonitoringApi()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(42L);

        var count = await _counter.GetFailedCountAsync();

        count.Should().Be(42L);
    }

    [Fact]
    public async Task GetFailedCountAsync_WhenMonitoringApiThrows_PropagatesException()
    {
        _monitoringApiMock
            .Setup(a => a.FailedCount())
            .Throws(new InvalidOperationException("storage unavailable"));

        var act = async () => await _counter.GetFailedCountAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("storage unavailable");
    }

    [Fact]
    public void Constructor_WithNullJobStorage_ThrowsArgumentNullException()
    {
        Action act = () => _ = new HangfireFailedJobCounter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("jobStorage");
    }
}
