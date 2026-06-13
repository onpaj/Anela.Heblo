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

    [Fact]
    public async Task EnsureFreshAsync_RefreshesAndEmitsNearExpiry_WhenInsideBuffer()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromHours(24)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);

        await sut.EnsureFreshAsync(CancellationToken.None);

        refresh.Verify(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()), Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.NearExpiry,
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("expiresAt") && d.ContainsKey("bufferHours") && d.ContainsKey("tokenIdShort")),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.Is<Dictionary<string, string>>(d => d["triggeredBy"] == "near-expiry"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_ReturnsTrueAndEmitsAuthFailedRetry_WhenRefreshSucceeds()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-10)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeTrue();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.Is<Dictionary<string, string>>(d => d["triggeredBy"] == "auth-failed-retry"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_ReturnsFalseAndEmitsRefreshFailed_WhenRefreshThrows()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("boom"));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeFalse();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.RefreshFailed,
            It.Is<Dictionary<string, string>>(d => d["reason"] == "HttpError"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_ReturnsTrueAndEmitsKvWarning_WhenKvWriteFails()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);
        store.Setup(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new PlaudTokenSaveResult(KeyVaultWriteFailed: true, KeyVaultError: new Exception("kv")));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeTrue();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.KeyVaultWriteFailed,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_ReturnsFalseAndEmitsDiskWriteFailed_WhenStoreThrows()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);
        store.Setup(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()))
             .ThrowsAsync(new IOException("disk full"));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeFalse();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.RefreshFailed,
            It.Is<Dictionary<string, string>>(d => d["reason"] == "DiskWriteFailed"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
}
