using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenManagerTests
{
    private static long UnixSecondsFromNow(TimeSpan offset) =>
        DateTimeOffset.UtcNow.Add(offset).ToUnixTimeSeconds();

    private static (PlaudTokenManager Sut,
                    Mock<IPlaudTokenStore> Store,
                    Mock<IPlaudTokenRefreshClient> Refresh,
                    Mock<ITelemetryService> Telemetry)
        CreateSut(PlaudTokens initial, PlaudCredentialsOptions? opts = null)
    {
        var store = new Mock<IPlaudTokenStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(initial);
        store.Setup(s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new PlaudTokenSaveResult(false, null));

        var refresh = new Mock<IPlaudTokenRefreshClient>();
        var telemetry = new Mock<ITelemetryService>();

        var sut = new PlaudTokenManager(
            store.Object,
            refresh.Object,
            telemetry.Object,
            Options.Create(opts ?? new PlaudCredentialsOptions()),
            NullLogger<PlaudTokenManager>.Instance);

        return (sut, store, refresh, telemetry);
    }

    [Fact]
    public async Task EnsureFreshAsync_DoesNothing_WhenTokenIsWellOutsideBuffer()
    {
        var initial = new PlaudTokens("a", "r", UnixSecondsFromNow(TimeSpan.FromDays(20)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        await sut.EnsureFreshAsync(CancellationToken.None);

        refresh.Verify(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        telemetry.Verify(t => t.TrackBusinessEvent(
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, double>>()),
            Times.Never);
    }
}
