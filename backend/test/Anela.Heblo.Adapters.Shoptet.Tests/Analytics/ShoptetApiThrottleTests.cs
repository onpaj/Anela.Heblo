using System.Diagnostics;
using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetApiThrottleTests
{
    [Fact]
    public async Task WaitAsync_spaces_consecutive_calls_by_the_configured_interval()
    {
        // Arrange — 20 req/s so the test stays fast; the production setting is ~3.
        var throttle = new ShoptetApiThrottle(requestsPerSecond: 20);
        var stopwatch = Stopwatch.StartNew();

        // Act — the first call goes through immediately, the next four are paced.
        for (var i = 0; i < 5; i++)
            await throttle.WaitAsync(CancellationToken.None);

        // Assert — four intervals of 50 ms, with slack for timer resolution.
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Penalize_pushes_the_next_slot_out()
    {
        // Arrange — this is what a 429 does: everything queued behind the throttle backs off.
        var throttle = new ShoptetApiThrottle(requestsPerSecond: 1000);
        await throttle.WaitAsync(CancellationToken.None);

        throttle.Penalize(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();

        // Act
        await throttle.WaitAsync(CancellationToken.None);

        // Assert
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Penalize_never_shortens_an_existing_back_off()
    {
        // Arrange
        var throttle = new ShoptetApiThrottle(requestsPerSecond: 1000);
        throttle.Penalize(TimeSpan.FromMilliseconds(300));

        // Act — a shorter penalty arriving afterwards must not undo the longer one.
        throttle.Penalize(TimeSpan.FromMilliseconds(10));
        var stopwatch = Stopwatch.StartNew();
        await throttle.WaitAsync(CancellationToken.None);

        // Assert
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Constructor_rejects_a_non_positive_rate()
    {
        var act = () => new ShoptetApiThrottle(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
